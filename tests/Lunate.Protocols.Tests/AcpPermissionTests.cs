using System.Text.Json;
using Acp.Schema;
using Lunate.Agent;
using Lunate.Coding;
using ChatRole = Microsoft.Extensions.AI.ChatRole;
using FunctionResultContent = Microsoft.Extensions.AI.FunctionResultContent;
using IChatClient = Microsoft.Extensions.AI.IChatClient;

namespace Lunate.Protocols.Tests;

public sealed class AcpPermissionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_asks_the_editor_and_allow_once_executes()
    {
        using var temp = new TempDirectory();
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);
        var model = new AcpScriptedChatClient()
            .Enqueue(
                AcpScripts.Call(
                    "call-1",
                    "write-probe",
                    new Dictionary<string, object?> { ["path"] = "src/app.cs" }
                ),
                AcpScripts.ToolCalls()
            )
            .Enqueue(AcpScripts.Text("done"), AcpScripts.Stop());
        await using AcpRuntime runtime = Start(model, ApprovalPolicy.Ask, tool);
        NewSessionResponse session = await NewSession(runtime, temp);
        runtime.State.PermissionHandler = (request, _) =>
            Task.FromResult(Answer(request, PermissionOptionKind.AllowOnce));

        PromptResponse response = await Prompt(runtime, session, "edit it");

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(1, tool.Executions);
        RequestPermissionRequest request = Assert.Single(runtime.State.PermissionRequests);
        Assert.Equal(session.SessionId.Value, request.SessionId.Value);
        Assert.Equal("write-probe: src/app.cs", request.ToolCall.Title);
        Assert.Equal(ToolKind.Edit, request.ToolCall.Kind!.Value);
        Assert.Equal("src/app.cs", request.ToolCall.RawInput?.GetProperty("path").GetString());
        Assert.Equal(
            [
                PermissionOptionKind.AllowOnce,
                PermissionOptionKind.AllowAlways,
                PermissionOptionKind.RejectOnce,
                PermissionOptionKind.RejectAlways,
            ],
            request.Options.Select(option => option.Kind)
        );
    }

    [Fact]
    public async Task A_policy_allowed_read_never_asks_the_editor()
    {
        using var temp = new TempDirectory();
        var tool = new RecordingRiskTool("read-probe", ToolRisk.ReadOnly);
        var model = Scripted("read-probe");
        await using AcpRuntime runtime = Start(model, ApprovalPolicy.Ask, tool);
        NewSessionResponse session = await NewSession(runtime, temp);

        PromptResponse response = await Prompt(runtime, session, "read it");

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(1, tool.Executions);
        Assert.Empty(runtime.State.PermissionRequests);
    }

    [Fact]
    public async Task A_rejection_declines_the_call_as_a_tool_error()
    {
        using var temp = new TempDirectory();
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);
        var model = Scripted("write-probe");
        await using AcpRuntime runtime = Start(model, ApprovalPolicy.Ask, tool);
        NewSessionResponse session = await NewSession(runtime, temp);
        runtime.State.PermissionHandler = (request, _) =>
            Task.FromResult(Answer(request, PermissionOptionKind.RejectOnce));

        PromptResponse response = await Prompt(runtime, session, "edit it");

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(0, tool.Executions);
        Assert.Contains(
            runtime.State.Updates,
            update =>
                AcpTestSupport.Wire(update.Update).Contains("Denied", StringComparison.Ordinal)
        );
        Assert.Contains(
            DeniedResults(model),
            result => result.Contains("write-probe", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Allow_always_is_remembered_for_the_session()
    {
        using var temp = new TempDirectory();
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);
        var model = Scripted("write-probe", "write-probe");
        await using AcpRuntime runtime = Start(model, ApprovalPolicy.Ask, tool);
        NewSessionResponse session = await NewSession(runtime, temp);
        runtime.State.PermissionHandler = (request, _) =>
            Task.FromResult(Answer(request, PermissionOptionKind.AllowAlways));

        PromptResponse response = await Prompt(runtime, session, "edit both");

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(2, tool.Executions);
        Assert.Single(runtime.State.PermissionRequests);
    }

    [Fact]
    public async Task Reject_always_auto_declines_the_next_call_without_a_request()
    {
        using var temp = new TempDirectory();
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);
        var model = Scripted("write-probe", "write-probe");
        await using AcpRuntime runtime = Start(model, ApprovalPolicy.Ask, tool);
        NewSessionResponse session = await NewSession(runtime, temp);
        runtime.State.PermissionHandler = (request, _) =>
            Task.FromResult(Answer(request, PermissionOptionKind.RejectAlways));

        PromptResponse response = await Prompt(runtime, session, "edit both");

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(0, tool.Executions);
        Assert.Single(runtime.State.PermissionRequests);
        Assert.Equal(2, DeniedResults(model).Count());
    }

    [Fact]
    public async Task AutoEdit_runs_writes_without_asking()
    {
        using var temp = new TempDirectory();
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);
        var model = Scripted("write-probe");
        await using AcpRuntime runtime = Start(model, ApprovalPolicy.AutoEdit, tool);
        NewSessionResponse session = await NewSession(runtime, temp);

        PromptResponse response = await Prompt(runtime, session, "edit it");

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(1, tool.Executions);
        Assert.Empty(runtime.State.PermissionRequests);
    }

    [Theory]
    [InlineData(ApprovalPolicy.Ask)]
    [InlineData(ApprovalPolicy.AutoEdit)]
    public async Task Commands_prompt_under_every_policy(ApprovalPolicy policy)
    {
        using var temp = new TempDirectory();
        var tool = new RecordingRiskTool("command-probe", ToolRisk.Execute);
        var model = Scripted("command-probe");
        await using AcpRuntime runtime = Start(model, policy, tool);
        NewSessionResponse session = await NewSession(runtime, temp);
        runtime.State.PermissionHandler = (request, _) =>
            Task.FromResult(Answer(request, PermissionOptionKind.AllowOnce));

        PromptResponse response = await Prompt(runtime, session, "run it");

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(1, tool.Executions);
        Assert.Single(runtime.State.PermissionRequests);
    }

    [Fact]
    public async Task Cancelling_during_a_permission_request_declines_it()
    {
        using var temp = new TempDirectory();
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);
        var model = new AcpScriptedChatClient { ParkWhenExhausted = true }.Enqueue(
            AcpScripts.Call(
                "call-1",
                "write-probe",
                new Dictionary<string, object?> { ["path"] = "a.cs" }
            ),
            AcpScripts.ToolCalls()
        );
        await using AcpRuntime runtime = Start(model, ApprovalPolicy.Ask, tool);
        NewSessionResponse session = await NewSession(runtime, temp);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runtime.State.PermissionHandler = async (_, _) =>
        {
            started.TrySetResult();
            await parked.Task.ConfigureAwait(false);
            return new RequestPermissionResponse { Outcome = new CancelledPermissionOutcome() };
        };

        Task<PromptResponse> prompt = Prompt(runtime, session, "edit it");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.Equal(0, tool.Executions);

        await runtime.Client.CancelAsync(
            new CancelNotification { SessionId = session.SessionId },
            Ct
        );
        PromptResponse response = await prompt.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        parked.TrySetResult();

        Assert.Equal(StopReason.Cancelled, response.StopReason);
        Assert.Equal(0, tool.Executions);
    }

    private static AcpRuntime Start(
        IChatClient client,
        ApprovalPolicy policy,
        params ITool[] tools
    ) =>
        AcpTestSupport.Start(context =>
        {
            var registry = new ToolRegistry();
            foreach (ITool tool in tools)
            {
                registry.Add(tool);
            }

            return new AgentHarness(
                client,
                registry,
                new AgentHarnessOptions
                {
                    MaxRetries = 0,
                    Approver = context.ClientApprover is { } approver
                        ? new AcpApprover(policy, approver)
                        : null,
                }
            );
        });

    private static async Task<NewSessionResponse> NewSession(AcpRuntime runtime, TempDirectory temp)
    {
        await runtime.Client.InitializeAsync(AcpTestSupport.InitializeRequest, Ct);
        return await runtime.Client.NewSessionAsync(
            new NewSessionRequest { Cwd = temp.Root, McpServers = [] },
            Ct
        );
    }

    private static Task<PromptResponse> Prompt(
        AcpRuntime runtime,
        NewSessionResponse session,
        string text
    ) =>
        runtime.Client.PromptAsync(
            new PromptRequest
            {
                SessionId = session.SessionId,
                Prompt = [new TextContent { Text = text }],
            },
            Ct
        );

    private static RequestPermissionResponse Answer(
        RequestPermissionRequest request,
        PermissionOptionKind kind
    ) =>
        new()
        {
            Outcome = new SelectedPermissionOutcome
            {
                OptionId = request.Options.First(option => option.Kind == kind).OptionId,
            },
        };

    private static IEnumerable<string> DeniedResults(AcpScriptedChatClient model) =>
        model
            .Requests[^1]
            .Where(message => message.Role == ChatRole.Tool)
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => result.Result?.ToString() ?? string.Empty)
            .Where(result => result.Contains("Denied", StringComparison.Ordinal));

    private static AcpScriptedChatClient Scripted(params string[] toolNames)
    {
        var model = new AcpScriptedChatClient();
        for (var index = 0; index < toolNames.Length; index++)
        {
            model.Enqueue(
                AcpScripts.Call(
                    $"{toolNames[index]}-call-{index}",
                    toolNames[index],
                    new Dictionary<string, object?>()
                ),
                AcpScripts.ToolCalls()
            );
        }

        return model.Enqueue(AcpScripts.Text("done"), AcpScripts.Stop());
    }
}

internal sealed class RecordingRiskTool(string name, ToolRisk risk) : ITool
{
    private int executions;

    public string Name { get; } = name;

    public string Description => "Records its executions for the ACP pipe tests.";

    public JsonElement ParametersSchema { get; } =
        JsonSerializer.SerializeToElement(new { type = "object" });

    public ToolRisk Risk { get; } = risk;

    public int Executions => Volatile.Read(ref executions);

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        Interlocked.Increment(ref executions);
        return Task.FromResult(new ToolResult($"{Name} ran", IsError: false));
    }
}
