using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveSessionRoutingCostTests
{
    [Fact]
    public async Task Cost_reports_persisted_decision_usage_with_its_method()
    {
        using var temp = new TempDirectory();
        string modelsPath = temp.File("models.json");
        File.WriteAllText(
            modelsPath,
            """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "default-model", "provider": "openai", "contextWindow": 64000, "supportsTools": true, "pricing": { "input": 1, "output": 2 } },
                { "id": "fast-model", "provider": "openai", "contextWindow": 64000, "supportsTools": true, "pricing": { "input": 1, "output": 2 } },
                { "id": "decision-model", "provider": "openai", "contextWindow": 64000, "supportsTools": true, "pricing": { "input": 1, "output": 2 } }
              ]
            }
            """
        );
        ScriptedChatClient decision = new();
        decision.Enqueue(
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new TextContent(
                        """{"results":{"routing":{"answer":"fast","probabilities":{"default":0.1,"fast":0.9}}}}"""
                    ),
                    new UsageContent(
                        new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 100 }
                    ),
                ]
            )
            {
                FinishReason = ChatFinishReason.Stop,
            }
        );
        ScriptedChatClient fast = new();
        fast.Enqueue(
            new ChatResponseUpdate(
                ChatRole.Assistant,
                [
                    new TextContent("fast answer"),
                    new UsageContent(
                        new UsageDetails { InputTokenCount = 1000, OutputTokenCount = 100 }
                    ),
                ]
            )
            {
                FinishReason = ChatFinishReason.Stop,
            }
        );
        var factory = new RoutingFactory(
            new Dictionary<string, IChatClient>(StringComparer.Ordinal)
            {
                ["decision-model"] = decision,
                ["fast-model"] = fast,
            }
        );
        using var host = new InteractiveSessionHost(factory: factory, modelsPath: modelsPath);
        File.WriteAllText(
            host.Temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "profiles": {
                "default": { "provider": "router", "model": "default-model" },
                "fast": { "provider": "router", "model": "fast-model" },
                "decision": { "provider": "router", "model": "decision-model" }
              },
              "usages": { "chat": "default" },
              "decisions": {
                "profile": "decision",
                "routing": "enforce",
                "routingProfiles": ["default", "fast"]
              }
            }
            """
        );
        ProviderStore.Save(
            AuthStore.ProviderStoreFilePath(host.Temp.File("auth.json")),
            [Provider()]
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() => host.Events.OfType<RunFinished>().Any());
        Assert.Single(host.Events.OfType<DecisionEvaluated>());
        host.Console.SendText("/cost");
        host.Console.SendEnter();
        string report = await InteractiveSessionCommandTests.WaitForScrollbackAsync(
            host,
            "cost by usage:"
        );

        Assert.Contains("decision (adapter)", report, StringComparison.Ordinal);
        Assert.Contains("decision-model", report, StringComparison.Ordinal);
        Assert.Contains("fast-model", report, StringComparison.Ordinal);

        string sessionPath = Assert.Single(
            Directory.EnumerateFiles(
                host.Temp.File("sessions"),
                "*.jsonl",
                SearchOption.AllDirectories
            )
        );
        Session persisted = Session.Load(sessionPath);
        SessionModelUsageEntry persistedDecision = Assert.Single(
            persisted.Entries.OfType<SessionModelUsageEntry>()
        );
        Assert.Equal("decision-model", persistedDecision.Model);
        Assert.Equal(new SessionUsage(1000, 100), persistedDecision.Usage);
        Assert.Equal(
            new ModelUsageAttribution("decision", "decision", "router", "openai", "adapter", 0.9d),
            persistedDecision.Attribution
        );
        Assert.Null(persistedDecision.DecisionOption);

        host.Console.Complete();
        await run;
    }

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

    private sealed class RoutingFactory(IReadOnlyDictionary<string, IChatClient> clients)
        : IChatClientFactory
    {
        public IChatClient Create(ModelInfo model) => clients[model.Id];

        public IChatClient Create(ModelInfo model, string? apiKey) => Create(model);
    }
}
