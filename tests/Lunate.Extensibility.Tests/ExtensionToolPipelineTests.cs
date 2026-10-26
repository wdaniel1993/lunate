using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The extension tool through the real agent pipeline: the registration's annotations reach the
/// ToolCalling hook, a tool without hints is an execute the approver sees, an approved call runs
/// with the model's arguments, a throwing handler becomes a teaching error result and the run
/// continues, and results are bounded (design D5, D7, D10).
/// </summary>
public sealed class ExtensionToolPipelineTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Annotations_reach_the_tool_calling_hook()
    {
        List<IReadOnlyList<string>> observed = [];
        var hooks = new HookRunner();
        hooks.Register(
            "observer",
            new TestToolCallingHandler(
                (payload, _) =>
                {
                    observed.Add(payload.Annotations);
                    return ValueTask.FromResult<ToolCallingResult>(
                        new ToolCallingResult.Proceed(null)
                    );
                }
            )
        );
        ExtensionToolAdapter tool = Register(
            "write",
            new ToolAnnotations(Destructive: true),
            Echo("done")
        );
        (AgentHarness harness, _) = Harness(tool, hooks);

        List<AgentEvent> events = await AdapterTestSupport.Run(harness, Ct);

        Assert.Equal(["destructive"], Assert.Single(observed));
        Assert.Equal("done", Assert.Single(events.OfType<ToolCallResult>()).Output);
    }

    [Fact]
    public async Task A_tool_without_hints_is_an_execute_and_the_approver_decides()
    {
        var approver = new RecordingApprover(approve: false);
        ExtensionToolAdapter tool = Register("spawn", annotations: null, Echo("done"));
        (AgentHarness harness, _) = Harness(tool, approver: approver);

        List<AgentEvent> events = await AdapterTestSupport.Run(harness, Ct);

        Assert.Equal(ToolRisk.Execute, Assert.Single(approver.Seen).Risk);
        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Contains("Denied", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_approved_call_runs_with_the_model_arguments()
    {
        string? seen = null;
        ExtensionToolAdapter tool = Register(
            "spawn",
            annotations: null,
            new DelegateToolHandler(
                (args, _, _) =>
                {
                    seen = args.GetRawText();
                    return Task.FromResult(new ToolResult("ran", false));
                }
            )
        );
        (AgentHarness harness, _) = Harness(tool, approver: new RecordingApprover(approve: true));

        List<AgentEvent> events = await AdapterTestSupport.Run(harness, Ct);

        Assert.Equal("""{"name":"x"}""", seen);
        Assert.Equal("ran", Assert.Single(events.OfType<ToolCallResult>()).Output);
    }

    [Fact]
    public async Task A_throwing_handler_becomes_a_teaching_error_and_the_run_continues()
    {
        ExtensionToolAdapter tool = Register(
            "explode",
            annotations: null,
            new DelegateToolHandler((_, _, _) => throw new InvalidOperationException("boom"))
        );
        (AgentHarness harness, FakeChatClient client) = Harness(tool);

        List<AgentEvent> events = await AdapterTestSupport.Run(harness, Ct);

        ToolCallResult result = Assert.Single(events.OfType<ToolCallResult>());
        Assert.True(result.IsError);
        Assert.Equal(
            "Tool 'tools__explode' failed: boom. Fix the call or try a different approach.",
            result.Output
        );
        Assert.Equal(StopReasons.Stop, Assert.Single(events.OfType<RunFinished>()).StopReason);
        Assert.Contains(
            client.Requests[1],
            message =>
                message
                    .Contents.OfType<FunctionResultContent>()
                    .Any(result =>
                        result
                            .Result?.ToString()
                            ?.Contains("failed: boom", StringComparison.Ordinal) == true
                    )
        );
    }

    [Fact]
    public async Task Results_are_bounded_by_the_runs_output_limit()
    {
        string longOutput = new('x', 300);
        ExtensionToolAdapter tool = Register("long", annotations: null, Echo(longOutput));
        (AgentHarness harness, _) = Harness(tool, outputLimit: 40);

        List<AgentEvent> events = await AdapterTestSupport.Run(harness, Ct);

        string output = Assert.Single(events.OfType<ToolCallResult>()).Output;
        Assert.Contains("characters truncated", output, StringComparison.Ordinal);
        Assert.True(output.Length < longOutput.Length);
    }

    [Fact]
    public void The_declaration_carries_the_prefixed_name_and_the_authored_schema()
    {
        JsonElement schema = JsonDocument
            .Parse("""{"type":"object","properties":{"query":{"type":"string"}}}""")
            .RootElement.Clone();
        var tools = new ToolRegistry();
        tools.Add(Adapter("search", schema));

        AIFunction declaration = Assert.Single(tools.Declarations);

        Assert.Equal("tools__search", declaration.Name);
        Assert.Equal(schema.GetRawText(), declaration.JsonSchema.GetRawText());
    }

    private static (AgentHarness Harness, FakeChatClient Client) Harness(
        ITool tool,
        HookRunner? hooks = null,
        IToolApprover? approver = null,
        int outputLimit = ToolOutput.DefaultLimit
    )
    {
        var client = new FakeChatClient()
            .Enqueue(
                new ChatResponseUpdate(
                    ChatRole.Assistant,
                    [
                        new FunctionCallContent(
                            "call-1",
                            tool.Name,
                            new Dictionary<string, object?> { ["name"] = "x" }
                        ),
                    ]
                ),
                new ChatResponseUpdate(ChatRole.Assistant, [])
                {
                    FinishReason = ChatFinishReason.ToolCalls,
                }
            )
            .Enqueue(
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")])
                {
                    FinishReason = ChatFinishReason.Stop,
                }
            );
        var registry = new ToolRegistry();
        registry.Add(tool);
        var harness = new AgentHarness(
            client,
            registry,
            new AgentHarnessOptions
            {
                WorkingDirectory = "/work",
                Hooks = hooks is null ? null : new AgentHookAdapter(hooks),
                Approver = approver,
                ToolOutputLimit = outputLimit,
            }
        );
        return (harness, client);
    }

    private static ExtensionToolAdapter Register(
        string name,
        ToolAnnotations? annotations,
        IExtensionToolHandler handler
    )
    {
        var registry = new ExtensionToolRegistry();
        registry.Register(
            "tools",
            new ExtensionToolRegistration
            {
                Name = name,
                Description = "Does a thing.",
                ParametersSchema = Schema(),
                Annotations = annotations,
                Handler = handler,
            }
        );
        return Assert.Single(registry.List());
    }

    private static ExtensionToolAdapter Adapter(string name, JsonElement schema)
    {
        var registry = new ExtensionToolRegistry();
        registry.Register(
            "tools",
            new ExtensionToolRegistration
            {
                Name = name,
                Description = "Does a thing.",
                ParametersSchema = schema,
                Handler = Echo("ok"),
            }
        );
        return Assert.Single(registry.List());
    }

    private static IExtensionToolHandler Echo(string output) =>
        new DelegateToolHandler((_, _, _) => Task.FromResult(new ToolResult(output, false)));

    private static JsonElement Schema() =>
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    private sealed class RecordingApprover(bool approve) : IToolApprover
    {
        public List<ITool> Seen { get; } = [];

        public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
        {
            Seen.Add(request.Tool);
            return ValueTask.FromResult(approve);
        }
    }
}
