using Lunate.Agent;
using Lunate.Ai;
using Lunate.Protocols.Acp;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// Routing wiring outside the interactive session (issue #445): print mode and ACP pass
/// <c>decisions.routing</c> and <c>routingProfiles</c> from the resolved settings and resolve the
/// profile clients through <see cref="DecisionUsageModels"/>, so a routed selection applies
/// exactly where the user configured it, not only in the interactive session.
/// </summary>
public sealed class RoutingDecisionWiringTests
{
    private const string RoutingResponse =
        """{"results":{"routing":{"answer":"fast","probabilities":{"default":0.1,"fast":0.9}}}}""";

    [Fact]
    public async Task Print_mode_routes_the_run_through_the_selected_profile()
    {
        using var temp = new TempDirectory();
        var decision = new ScriptedChatClient().Enqueue(
            Scripts.Text(RoutingResponse),
            Scripts.Stop()
        );
        var fast = new ScriptedChatClient().Enqueue(Scripts.Text("fast answer"), Scripts.Stop());
        var fallback = new ScriptedChatClient().Enqueue(
            Scripts.Text("default answer"),
            Scripts.Stop()
        );
        WriteFixture(temp);

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(
            PrintModeTestSupport.BaseOptions(
                temp,
                new ModelCatalogChatClientFactory(
                    new Dictionary<string, IChatClient>(StringComparer.Ordinal)
                    {
                        ["decision-model"] = decision,
                        ["fast-model"] = fast,
                        ["default-model"] = fallback,
                        [PrintModeTestSupport.ModelId] = fallback,
                    }
                )
            ) with
            {
                Prompt = "Hi",
            }
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("fast answer", output);
        Assert.DoesNotContain("unavailable", errors, StringComparison.Ordinal);
        // "fast answer" can only come from the routed selection: the un-routed profile never
        // answered anything, so the decision consumed applied outside the interactive run.
        Assert.Empty(fallback.Requests);
    }

    [Fact]
    public async Task Acp_routes_the_run_through_the_selected_profile()
    {
        using var temp = new TempDirectory();
        var decision = new ScriptedChatClient().Enqueue(
            Scripts.Text(RoutingResponse),
            Scripts.Stop()
        );
        var fast = new ScriptedChatClient().Enqueue(Scripts.Text("fast answer"), Scripts.Stop());
        var fallback = new ScriptedChatClient().Enqueue(
            Scripts.Text("default answer"),
            Scripts.Stop()
        );
        string modelsPath = temp.File("models.json");
        File.WriteAllText(modelsPath, ModelsJson());
        ProviderStore.Save(
            AuthStore.ProviderStoreFilePath(temp.File("auth.json")),
            [Provider()]
        );
        AgentHarness harness = AcpMode.CreateHarness(
            new AcpSessionContext(temp.Root, null, null),
            null,
            [],
            [],
            null,
            new ModelCatalogChatClientFactory(
                new Dictionary<string, IChatClient>(StringComparer.Ordinal)
                {
                    ["decision-model"] = decision,
                    ["fast-model"] = fast,
                    ["default-model"] = fallback,
                    ["gpt-4o-mini"] = fallback,
                }
            ),
            temp.File("sessions"),
            Settings(),
            ModelCatalog.Load(modelsPath),
            authPath: temp.File("auth.json")
        );

        var events = new List<AgentEvent>();
        await foreach (
            AgentEvent agentEvent in harness.RunAsync("Hi", TestContext.Current.CancellationToken)
        )
        {
            events.Add(agentEvent);
        }

        string? lastText = events.OfType<TextMessageContent>().LastOrDefault()?.Text ?? "";
        Assert.Contains("fast answer", lastText, StringComparison.Ordinal);
        // "fast answer" can only come from the routed selection: the un-routed profile never
        // answered anything, so the routing decision applied outside the interactive session.
        Assert.Empty(fallback.Requests);
    }

    private static void WriteFixture(TempDirectory temp)
    {
        File.WriteAllText(temp.File("models.json"), ModelsJson());
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "model": "gpt-4o-mini",
              "profiles": {
                "default": { "provider": "router", "model": "default-model" },
                "fast": { "provider": "router", "model": "fast-model" },
                "decision": { "provider": "router", "model": "decision-model" }
              },
              "decisions": {
                "profile": "decision",
                "routing": "enforce",
                "routingProfiles": ["default", "fast"]
              }
            }
            """
        );
        ProviderStore.Save(
            AuthStore.ProviderStoreFilePath(temp.File("auth.json")),
            [Provider()]
        );
    }

    private static AgentSettings Settings() =>
        new()
        {
            Model = "gpt-4o-mini",
            Approval = ApprovalPolicy.AutoEdit,
            Profiles = new Dictionary<string, ModelProfile>(StringComparer.Ordinal)
            {
                ["default"] = new("default", "router", "default-model"),
                ["fast"] = new("fast", "router", "fast-model"),
                ["decision"] = new("decision", "router", "decision-model"),
            },
            Decisions = new DecisionsSettings
            {
                Profile = "decision",
                Routing = DecisionMode.Enforce,
                RoutingProfiles = ["default", "fast"],
            },
        };

    private static string ModelsJson() =>
        """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "gpt-4o-mini", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true },
                { "id": "default-model", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true },
                { "id": "fast-model", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true },
                { "id": "decision-model", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": false }
              ]
            }
            """;

    private static ProviderEntry Provider()
    {
        bool valid = ProviderEntry.TryCreate(
            "router",
            "openai",
            new Uri("https://provider.example.test/v1"),
            "auth:router",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }

    /// <summary>One scripted client per catalog model id, like the per-model factory the routing fixtures use.</summary>
    private sealed class ModelCatalogChatClientFactory(
        IReadOnlyDictionary<string, IChatClient> clients
    ) : IChatClientFactory
    {
        public IChatClient Create(ModelInfo model) =>
            clients.TryGetValue(
                model.Id,
                out IChatClient? client
            )
                ? client
                : throw new InvalidOperationException($"no scripted client for {model.Id}");

        public IChatClient Create(ModelInfo model, string? apiKey) => Create(model);
    }
}
