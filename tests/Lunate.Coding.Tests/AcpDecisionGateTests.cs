using Lunate.Agent;
using Lunate.Ai;
using Lunate.Protocols.Acp;

namespace Lunate.Coding.Tests;

/// <summary>
/// ACP mode's decision wiring (issue #439): the approval gate composes with the editor's
/// permission panel, so an enforce-mode <c>deny</c> above the threshold escalates to the client
/// prompt that carries the escalation; without a client prompt the call is denied fail-closed.
/// </summary>
public sealed class AcpDecisionGateTests
{
    private const string DenyResponse =
        """{"results":{"approval":{"answer":"deny","probabilities":{"approve":0.13,"deny":0.87}}}}""";

    [Fact]
    public async Task An_enforce_deny_escalates_to_the_client_prompt_and_its_answer_decides()
    {
        using var temp = new TempDirectory();
        string modelsPath = temp.File("models.json");
        File.WriteAllText(modelsPath, ModelsJson());
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "note.txt"), ("content", "hi"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text(DenyResponse), Scripts.Stop())
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        var clientApprover = new PromptRecordingApprover();
        ProviderStore.Save(AuthStore.ProviderStoreFilePath(temp.File("auth.json")), [Provider()]);
        AgentHarness harness = AcpMode.CreateHarness(
            new AcpSessionContext(temp.Root, clientApprover, null),
            null,
            [],
            [],
            null,
            new FakeChatClientFactory(client),
            temp.File("sessions"),
            Settings(ApprovalPolicy.AutoEdit),
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

        ApprovalRequest request = Assert.Single(clientApprover.PromptRequests);
        Assert.True(request.EscalationRequested);
        Assert.Equal("deny", request.DecisionRecommendation);
        Assert.Equal(0.87, request.DecisionProbability!.Value, precision: 10);
        Assert.Equal("decision-model", request.DecisionModelId);
        // The client's answer decides the call, so the write happened.
        Assert.True(File.Exists(temp.File("note.txt")));
    }

    [Fact]
    public async Task A_deny_without_a_client_prompt_is_denied_fail_closed()
    {
        using var temp = new TempDirectory();
        string modelsPath = temp.File("models.json");
        File.WriteAllText(modelsPath, ModelsJson());
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "note.txt"), ("content", "hi"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text(DenyResponse), Scripts.Stop())
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        ProviderStore.Save(AuthStore.ProviderStoreFilePath(temp.File("auth.json")), [Provider()]);
        AgentHarness harness = AcpMode.CreateHarness(
            new AcpSessionContext(temp.Root, null, null),
            null,
            [],
            [],
            null,
            new FakeChatClientFactory(client),
            temp.File("sessions"),
            Settings(ApprovalPolicy.AutoEdit),
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

        // The ordinary policy would allow the write, so the file's absence is the fail-closed
        // denial: the decision escalated and no user was present to ask.
        Assert.False(File.Exists(temp.File("note.txt")));
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result =>
                result.Output.Contains("decision escalated", StringComparison.OrdinalIgnoreCase)
                && result.Output.Contains("no user was present", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task An_unresolvable_decision_profile_degrades_to_no_gate_with_a_warning()
    {
        using var temp = new TempDirectory();
        string modelsPath = temp.File("models.json");
        File.WriteAllText(modelsPath, ModelsJson());
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        // No provider store: the configured decision profile cannot resolve its provider.
        var warnings = new List<UserNotice>();
        AgentHarness harness = AcpMode.CreateHarness(
            new AcpSessionContext(temp.Root, null, null),
            null,
            [],
            [],
            null,
            new FakeChatClientFactory(client),
            temp.File("sessions"),
            Settings(ApprovalPolicy.AutoEdit),
            ModelCatalog.Load(modelsPath),
            authPath: temp.File("auth.json"),
            warn: warnings.Add
        );

        var events = new List<AgentEvent>();
        await foreach (
            AgentEvent agentEvent in harness.RunAsync("Hi", TestContext.Current.CancellationToken)
        )
        {
            events.Add(agentEvent);
        }

        UserNotice warning = Assert.Single(warnings);
        Assert.Contains(
            "decision profile 'decision-profile' is unavailable; the decision layer is off",
            warning.Message,
            StringComparison.Ordinal
        );
        // The run continues with the ordinary approval flow and its answer.
        string? lastText = events.OfType<TextMessageContent>().LastOrDefault()?.Text ?? "";
        Assert.Contains("ok", lastText, StringComparison.Ordinal);
    }

    private static AgentSettings Settings(ApprovalPolicy approval) =>
        new()
        {
            Model = "gpt-4o-mini",
            Approval = approval,
            Decisions = new DecisionsSettings
            {
                Profile = "decision-profile",
                Approvals = DecisionMode.Enforce,
            },
            Profiles = new Dictionary<string, ModelProfile>(StringComparer.Ordinal)
            {
                ["decision-profile"] = new(
                    "decision-profile",
                    "decision-provider",
                    "decision-model"
                ),
            },
        };

    private static string ModelsJson() =>
        """
            {
              "schemaVersion": 2,
              "models": [
                { "id": "gpt-4o-mini", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": true },
                { "id": "decision-model", "provider": "openai", "contextWindow": 64000,
                  "supportsTools": false }
              ]
            }
            """;

    private static ProviderEntry Provider()
    {
        bool valid = ProviderEntry.TryCreate(
            "decision-provider",
            "openai",
            new Uri("https://provider.example.test/v1"),
            "auth:decision-provider",
            headers: null,
            region: null,
            out ProviderEntry? provider,
            out string? error
        );

        Assert.True(valid, error);
        return Assert.IsType<ProviderEntry>(provider);
    }

    private sealed class PromptRecordingApprover : IToolApprover, IUserPromptToolApprover
    {
        public List<ApprovalRequest> PromptRequests { get; } = [];

        public ValueTask<bool> PromptAsync(ApprovalRequest request, CancellationToken ct)
        {
            PromptRequests.Add(request);
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct) =>
            ValueTask.FromResult(true);
    }
}
