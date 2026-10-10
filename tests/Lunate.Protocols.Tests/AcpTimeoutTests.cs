using System.Text.Json;
using Acp.JsonRpc;
using Acp.Schema;
using Lunate.Agent;
using Lunate.Coding;
using Lunate.Protocols.Acp;

namespace Lunate.Protocols.Tests;

public sealed class AcpTimeoutTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_file_request_the_client_ignores_fails_the_tool_as_an_io_error()
    {
        using var temp = new TempDirectory();
        await using RawAcpRuntime runtime = AcpTestSupport.StartRaw();
        runtime.State.IgnoreFileReads = true;
        var files = new ClientTextFileAccess(
            runtime.Server,
            new SessionId("timeout-session"),
            new SessionRunState(),
            TimeSpan.FromMilliseconds(100)
        );
        var tool = new ReadTool(new Workspace(temp.Root), files);
        var args = JsonDocument.Parse("""{"path":"a.txt"}""").RootElement.Clone();
        var context = new ToolContext(temp.Root, new NullAgentEvents());

        ToolResult result = await tool.ExecuteAsync(args, context, Ct)
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.True(result.IsError);
        Assert.Equal(
            "could not be read: the editor did not answer the file request within 0.1s",
            result.Output
        );
    }

    [Fact]
    public async Task A_permission_request_the_client_ignores_declines_after_the_timeout()
    {
        await using RawAcpRuntime runtime = AcpTestSupport.StartRaw();
        runtime.State.IgnorePermissions = true;
        var log = new List<string>();
        var approver = new ClientApprover(
            runtime.Server,
            new SessionId("timeout-session"),
            log.Add,
            TimeSpan.FromMilliseconds(100)
        );
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);

        bool approved = await approver
            .ApproveAsync(tool, default, Ct)
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.False(approved);
        Assert.Single(runtime.State.PermissionRequests);
        Assert.Contains(
            log,
            line => line.Contains("no answer within 0.1s", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_failure_answered_after_the_timeout_is_observed()
    {
        using var temp = new TempDirectory();
        await using RawAcpRuntime runtime = AcpTestSupport.StartRaw();
        runtime.State.IgnoreFileReads = true;
        var files = new ClientTextFileAccess(
            runtime.Server,
            new SessionId("timeout-session"),
            new SessionRunState(),
            TimeSpan.FromMilliseconds(100)
        );
        var tool = new ReadTool(new Workspace(temp.Root), files);
        var args = JsonDocument.Parse("""{"path":"a.txt"}""").RootElement.Clone();
        var context = new ToolContext(temp.Root, new NullAgentEvents());

        ToolResult result = await tool.ExecuteAsync(args, context, Ct)
            .WaitAsync(TimeSpan.FromSeconds(10), Ct);
        Assert.True(result.IsError);
        Assert.Contains("did not answer the file request", result.Output, StringComparison.Ordinal);

        // Flush faults finalizable since earlier tests before attaching the hook, so only
        // this request's late failure is judged. The held request is still pending here.
        for (var flush = 0; flush < 5; flush++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20, Ct);
        }

        Assert.True(
            runtime.State.FailHeldRead(
                RequestErrorException.InternalError(additionalMessage: "late boom")
            )
        );
        // A follow-up round trip; the bounded GC loop below also gives the late error's
        // processing on the agent's receive loop time to settle before each collection.
        runtime.State.IgnoreFileReads = false;
        runtime.State.FileReadHandler = _ => "second";
        Assert.Equal("second", files.ReadAllText(temp.File("b.txt")));

        var lateFailures = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> hook = (_, eventArgs) =>
        {
            eventArgs.SetObserved();
            // Scope to this request's own late error: a sibling test abandons a permission
            // request whose connection-closed fault (out of scope for the file seam) can also
            // surface while the hook is attached.
            if (
                eventArgs
                    .Exception.Flatten()
                    .InnerExceptions.Any(exception =>
                        exception is RequestErrorException
                        && exception.Message.Contains("late boom", StringComparison.Ordinal)
                    )
            )
            {
                Interlocked.Increment(ref lateFailures);
            }
        };

        TaskScheduler.UnobservedTaskException += hook;
        try
        {
            for (var attempt = 0; attempt < 20 && Volatile.Read(ref lateFailures) == 0; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                await Task.Delay(50, Ct);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= hook;
        }

        Assert.True(
            lateFailures == 0,
            $"the abandoned request's late failure surfaced as an unobserved task exception {lateFailures} time(s)"
        );
    }

    [Fact]
    public async Task A_timed_out_permission_request_continues_the_run_with_a_denial()
    {
        await using RawAcpRuntime runtime = AcpTestSupport.StartRaw();
        runtime.State.IgnorePermissions = true;
        var log = new List<string>();
        var clientApprover = new ClientApprover(
            runtime.Server,
            new SessionId("timeout-session"),
            log.Add,
            TimeSpan.FromMilliseconds(100)
        );
        var tool = new RecordingRiskTool("write-probe", ToolRisk.Write);
        var model = new AcpScriptedChatClient()
            .Enqueue(
                AcpScripts.Call(
                    "call-1",
                    "write-probe",
                    new Dictionary<string, object?> { ["path"] = "a.cs" }
                ),
                AcpScripts.ToolCalls()
            )
            .Enqueue(AcpScripts.Text("done"), AcpScripts.Stop());
        var registry = new ToolRegistry();
        registry.Add(tool);
        var harness = new AgentHarness(
            model,
            registry,
            new AgentHarnessOptions
            {
                MaxRetries = 0,
                Approver = new AcpApprover(ApprovalPolicy.Ask, clientApprover),
            }
        );

        var events = new List<AgentEvent>();
        await foreach (AgentEvent agentEvent in harness.RunAsync("edit it", Ct))
        {
            events.Add(agentEvent);
        }

        Assert.Equal(0, tool.Executions);
        Assert.Contains(
            events,
            agentEvent =>
                agentEvent is ToolCallResult { IsError: true } result
                && result.Output.Contains("Denied", StringComparison.Ordinal)
        );
        Assert.Contains(events, agentEvent => agentEvent is RunFinished);
    }
}
