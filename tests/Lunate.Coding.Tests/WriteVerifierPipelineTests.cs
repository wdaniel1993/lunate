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
        var verifier = new RecordingVerifier((_, _) =>
        {
            Assert.Equal(["approved"], order);
            order.Add("verified");
            return Verified();
        });
        var tools = new ToolRegistry();
        tools.Add(new WriteTool(new Workspace(temp.Root), verifierSelector: _ => verifier));
        var approver = new OrderedApprover(order);
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "write", Scripts.Args(("path", "code.cs"), ("content", "class C {}\n"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        var harness = new AgentHarness(
            client,
            tools,
            new AgentHarnessOptions
            {
                WorkingDirectory = temp.Root,
                Approver = approver,
            }
        );

        await foreach (AgentEvent _ in harness.RunAsync("write code", Ct)) { }

        Assert.Equal(["approved", "verified"], order);
        Assert.Equal("class C {}\n", File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task Write_verifier_receives_exact_normalized_baseline_and_staged_text()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("code.cs"), "old\r\n");
        WriteVerificationRequest? captured = null;
        var verifier = new RecordingVerifier((request, _) =>
        {
            captured = request;
            return Verified();
        });

        ToolResult result = await new WriteTool(
                new Workspace(temp.Root),
                verifierSelector: _ => verifier
            )
            .ExecuteAsync(Args(new { path = "code.cs", content = "new\n" }), Context(temp.Root), Ct);

        Assert.False(result.IsError);
        Assert.Equal("code.cs", captured!.RelativePath);
        Assert.Equal("old\r\n", captured.BaselineContent);
        Assert.Equal("new" + Environment.NewLine, captured.StagedContent);
    }

    [Fact]
    public async Task Edit_verifier_receives_exact_normalized_baseline_and_staged_text()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("code.cs"), "first\r\nsecond\r\n");
        WriteVerificationRequest? captured = null;
        var verifier = new RecordingVerifier((request, _) =>
        {
            captured = request;
            return Verified();
        });

        ToolResult result = await new EditTool(
                new Workspace(temp.Root),
                verifierSelector: _ => verifier
            )
            .ExecuteAsync(
                Args(new { path = "code.cs", old_text = "second", new_text = "changed" }),
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
            )
            .ExecuteAsync(
                Args(new { path = "code.cs", content = "original staged text\n" }),
                Context(temp.Root),
                Ct
            );

        Assert.False(result.IsError);
        Assert.Equal("original staged text\n", File.ReadAllText(temp.File("code.cs")));
    }

    [Fact]
    public async Task Refusal_leaves_file_bytes_unchanged()
    {
        using var temp = new TempDirectory();
        const string original = "class C {}\n";
        File.WriteAllText(temp.File("code.cs"), original);
        var verifier = new RecordingVerifier((_, _) =>
            new WriteVerificationResult(WriteVerificationStatus.Refused, "introduced error")
        );

        ToolResult result = await new WriteTool(
                new Workspace(temp.Root),
                verifierSelector: _ => verifier
            )
            .ExecuteAsync(
                Args(new { path = "code.cs", content = "class C { invalid }\n" }),
                Context(temp.Root),
                Ct
            );

        Assert.True(result.IsError);
        Assert.Contains("introduced error", result.Output, StringComparison.Ordinal);
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
            )
            .ExecuteAsync(
                Args(new { path = "code.txt", content = "ordinary text\n" }),
                Context(temp.Root),
                Ct
            );

        Assert.False(result.IsError);
        Assert.Equal(0, verifier.Calls);
        Assert.Equal("ordinary text\n", File.ReadAllText(temp.File("code.txt")));
    }

    private static WriteVerificationResult Verified() =>
        new(WriteVerificationStatus.Verified);

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

    private sealed class QuietEvents : IAgentEvents
    {
        public void Emit(AgentEvent agentEvent) { }
    }
}
