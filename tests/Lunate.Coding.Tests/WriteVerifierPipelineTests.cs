using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class WriteVerifierPipelineTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Verifier_runs_after_approval()
    {
        using var temp = new TempDirectory();
        var order = new List<string>();
        var verifier = new RecordingVerifier(
            (_, _) =>
            {
                Assert.Equal(["approved"], order);
                order.Add("verified");
                return Verified();
            }
        );
        var tools = new ToolRegistry();
        tools.Add(new WriteTool(new Workspace(temp.Root), verifierSelector: _ => verifier));
        var approver = new OrderedApprover(order);
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "code.cs"), ("content", "class C {}\n"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        var harness = new AgentHarness(
            client,
            tools,
            new AgentHarnessOptions { WorkingDirectory = temp.Root, Approver = approver }
        );

        await foreach (AgentEvent _ in harness.RunAsync("write code", Ct)) { }

        Assert.Equal(["approved", "verified"], order);
        Assert.Equal(NewFile("class C {}\n"), File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task Write_verifier_receives_exact_normalized_baseline_and_staged_text()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("code.cs"), "old\r\n");
        WriteVerificationRequest? captured = null;
        var verifier = new RecordingVerifier(
            (request, _) =>
            {
                captured = request;
                return Verified();
            }
        );

        ToolResult result = await new WriteTool(
            new Workspace(temp.Root),
            verifierSelector: _ => verifier
        ).ExecuteAsync(Args(new { path = "code.cs", content = "new\n" }), Context(temp.Root), Ct);

        Assert.False(result.IsError);
        Assert.Equal("code.cs", captured!.RelativePath);
        Assert.Equal("old\r\n", captured.BaselineContent);
        Assert.Equal("new\r\n", captured.StagedContent);
    }

    [Fact]
    public async Task Edit_verifier_receives_exact_normalized_baseline_and_staged_text()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("code.cs"), "first\r\nsecond\r\n");
        WriteVerificationRequest? captured = null;
        var verifier = new RecordingVerifier(
            (request, _) =>
            {
                captured = request;
                return Verified();
            }
        );

        ToolResult result = await new EditTool(
            new Workspace(temp.Root),
            verifierSelector: _ => verifier
        ).ExecuteAsync(
            Args(
                new
                {
                    path = "code.cs",
                    old_text = "second",
                    new_text = "changed",
                }
            ),
            Context(temp.Root),
            Ct
        );

        Assert.False(result.IsError);
        Assert.Equal("first\r\nsecond\r\n", captured!.BaselineContent);
        Assert.Equal("first\r\nchanged\r\n", captured.StagedContent);
    }

    [Fact]
    public async Task Verifier_cannot_replace_the_host_staged_content()
    {
        using var temp = new TempDirectory();
        var verifier = new RecordingVerifier((_, _) => Verified());

        ToolResult result = await new WriteTool(
            new Workspace(temp.Root),
            verifierSelector: _ => verifier
        ).ExecuteAsync(
            Args(new { path = "code.cs", content = "original staged text\n" }),
            Context(temp.Root),
            Ct
        );

        Assert.False(result.IsError);
        Assert.Equal(NewFile("original staged text\n"), File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task Refusal_leaves_file_bytes_unchanged()
    {
        using var temp = new TempDirectory();
        const string original = "class C {}\n";
        File.WriteAllText(temp.File("code.cs"), original);
        var verifier = new RecordingVerifier(
            (_, _) =>
                new WriteVerificationResult(WriteVerificationStatus.Refused, "introduced error")
        );

        ToolResult result = await new WriteTool(
            new Workspace(temp.Root),
            verifierSelector: _ => verifier
        ).ExecuteAsync(
            Args(new { path = "code.cs", content = "class C { invalid }\n" }),
            Context(temp.Root),
            Ct
        );

        Assert.True(result.IsError);
        Assert.Contains("introduced error", result.Output, StringComparison.Ordinal);
        Assert.Equal(original, File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task WarnWritesUnverifiedWithDiagnostics()
    {
        using var temp = new TempDirectory();
        JsonElement diagnostics = Diagnostics();
        var verifier = new RecordingVerifier(
            (_, _) =>
                new WriteVerificationResult(
                    WriteVerificationStatus.Refused,
                    "CS1002: ; expected; CS1513: } expected",
                    diagnostics
                )
        );

        ToolResult result = await new WriteTool(
            new Workspace(temp.Root),
            WriteVerificationMode.Warn,
            _ => verifier
        ).ExecuteAsync(
            Args(new { path = "code.cs", content = "class C { invalid }\n" }),
            Context(temp.Root),
            Ct
        );

        Assert.False(result.IsError);
        Assert.Contains("unverified", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CS1002: ; expected", result.Output, StringComparison.Ordinal);
        Assert.Contains("CS1513: } expected", result.Output, StringComparison.Ordinal);
        Assert.Equal(diagnostics.GetRawText(), result.StructuredContent?.GetRawText());
        Assert.Equal(NewFile("class C { invalid }\n"), File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task OffSkipsVerifier()
    {
        using var temp = new TempDirectory();
        var verifier = new RecordingVerifier((_, _) => Verified());
        var selections = 0;

        ToolResult result = await new WriteTool(
            new Workspace(temp.Root),
            WriteVerificationMode.Off,
            _ =>
            {
                selections++;
                return verifier;
            }
        ).ExecuteAsync(
            Args(new { path = "code.cs", content = "unverified\n" }),
            Context(temp.Root),
            Ct
        );

        Assert.False(result.IsError);
        Assert.Equal(0, selections);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal(NewFile("unverified\n"), File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task ThirdAttemptWritesUnverifiedAfterTwoRefusals()
    {
        using var temp = new TempDirectory();
        Directory.CreateSymbolicLink(Path.Combine(temp.Root, "alias"), temp.Root);
        JsonElement diagnostics = Diagnostics();
        var verifier = new RecordingVerifier(
            (_, _) =>
                new WriteVerificationResult(
                    WriteVerificationStatus.Refused,
                    "CS1002: ; expected; CS1513: } expected",
                    diagnostics
                )
        );
        var tool = new WriteTool(
            new Workspace(temp.Root),
            WriteVerificationMode.Block,
            _ => verifier
        );
        ToolContext context = Context(temp.Root) with { RunId = "run-1" };

        ToolResult first = await tool.ExecuteAsync(
            Args(new { path = "code.cs", content = "first attempt\n" }),
            context,
            Ct
        );
        ToolResult second = await tool.ExecuteAsync(
            Args(new { path = "alias/code.cs", content = "second attempt\n" }),
            context,
            Ct
        );
        ToolResult third = await tool.ExecuteAsync(
            Args(new { path = "code.cs", content = "third attempt\n" }),
            context,
            Ct
        );

        foreach (ToolResult refusal in new[] { first, second })
        {
            Assert.True(refusal.IsError);
            Assert.Contains("CS1002: ; expected", refusal.Output, StringComparison.Ordinal);
            Assert.Contains("CS1513: } expected", refusal.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("unverified", refusal.Output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "two refusals",
                refusal.Output,
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Equal(diagnostics.GetRawText(), refusal.StructuredContent?.GetRawText());
        }

        Assert.False(third.IsError);
        Assert.Contains("unverified", third.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("two refusals", third.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, verifier.Calls);
        Assert.Equal(NewFile("third attempt\n"), File.ReadAllText(temp.File("code.cs")));
        Assert.Equal(diagnostics.GetRawText(), third.StructuredContent?.GetRawText());
    }

    [Fact]
    public async Task Verification_timeout_writes_unverified_with_elapsed_and_reason()
    {
        using var temp = new TempDirectory();
        var time = new ManualTimeProvider();
        var verifier = new WaitingVerifier();
        var tool = new WriteTool(
            new Workspace(temp.Root),
            verifierSelector: _ => verifier,
            timeProvider: time
        );
        Task<ToolResult> execution = tool.ExecuteAsync(
            Args(new { path = "code.cs", content = "staged\n" }),
            Context(temp.Root),
            Ct
        );
        await verifier.Started.Task.WaitAsync(WaitBudgets.InProcess, Ct);

        time.Advance(WriteVerificationPipeline.Deadline);
        ToolResult result = await execution.WaitAsync(WaitBudgets.InProcess, Ct);

        Assert.False(result.IsError);
        Assert.Contains("unverified", result.Output, StringComparison.Ordinal);
        Assert.Contains("10.0 s", result.Output, StringComparison.Ordinal);
        Assert.Contains("timeout", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(NewFile("staged\n"), File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task Run_cancellation_during_verification_cancels_without_writing()
    {
        using var temp = new TempDirectory();
        const string original = "before\n";
        File.WriteAllText(temp.File("code.cs"), original);
        using var cancellation = new CancellationTokenSource();
        var verifier = new WaitingVerifier();
        var tool = new WriteTool(new Workspace(temp.Root), verifierSelector: _ => verifier);
        Task<ToolResult> execution = tool.ExecuteAsync(
            Args(new { path = "code.cs", content = "staged\n" }),
            Context(temp.Root),
            cancellation.Token
        );
        await verifier.Started.Task.WaitAsync(WaitBudgets.InProcess, Ct);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(original, File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task A_file_without_a_matching_verifier_keeps_existing_write_behavior()
    {
        using var temp = new TempDirectory();
        var verifier = new RecordingVerifier((_, _) => Verified());

        ToolResult result = await new WriteTool(
            new Workspace(temp.Root),
            verifierSelector: _ => null
        ).ExecuteAsync(
            Args(new { path = "code.txt", content = "ordinary text\n" }),
            Context(temp.Root),
            Ct
        );

        Assert.False(result.IsError);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal(NewFile("ordinary text\n"), File.ReadAllText(temp.File("code.txt")));
    }

    /// <summary>
    /// A new file follows the platform's line endings, so the exact bytes a write test expects are
    /// LF on POSIX and CRLF on Windows.
    /// </summary>
    private static string NewFile(string text) => text.ReplaceLineEndings();

    private static WriteVerificationResult Verified() => new(WriteVerificationStatus.Verified);

    private static JsonElement Diagnostics() =>
        JsonDocument
            .Parse(
                """
                {"diagnostics":[{"severity":"error","code":"CS1002","message":"; expected"},{"severity":"error","code":"CS1513","message":"} expected"}]}
                """
            )
            .RootElement.Clone();

    private static ToolContext Context(string root) => new(root, new QuietEvents());

    private static JsonElement Args<T>(T value) => JsonSerializer.SerializeToElement(value);

    private sealed class RecordingVerifier(
        Func<WriteVerificationRequest, CancellationToken, WriteVerificationResult> verify
    ) : IWriteVerifier
    {
        public int Calls { get; private set; }

        public ValueTask<WriteVerificationResult> VerifyAsync(
            WriteVerificationRequest request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return ValueTask.FromResult(verify(request, cancellationToken));
        }
    }

    private sealed class OrderedApprover(List<string> order) : IToolApprover
    {
        public ValueTask<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
        {
            order.Add("approved");
            return ValueTask.FromResult(true);
        }
    }

    private sealed class WaitingVerifier : IWriteVerifier
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<WriteVerificationResult> VerifyAsync(
            WriteVerificationRequest request,
            CancellationToken cancellationToken
        )
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Verified();
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _timestamp;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period
        )
        {
            var timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            _timestamp += elapsed.Ticks;
            foreach (ManualTimer timer in _timers.ToArray())
            {
                timer.FireIfDue();
            }
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state
        ) : ITimer
        {
            private long _dueAt = long.MaxValue;
            private TimeSpan _period;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueAt =
                    dueTime == Timeout.InfiniteTimeSpan
                        ? long.MaxValue
                        : owner.GetTimestamp() + dueTime.Ticks;
                return true;
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue()
            {
                if (_disposed || owner.GetTimestamp() < _dueAt)
                {
                    return;
                }

                _dueAt =
                    _period > TimeSpan.Zero ? owner.GetTimestamp() + _period.Ticks : long.MaxValue;
                callback(state);
            }
        }
    }

    private sealed class QuietEvents : IAgentEvents
    {
        public void Emit(AgentEvent agentEvent) { }
    }
}
