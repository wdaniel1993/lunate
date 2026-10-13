using System.Text.RegularExpressions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-path proof: a real project extension subscribing through <c>core/file-bus</c>
/// receives one full-payload event per successful matching mutation in a print run, and nothing
/// for filtered or refused mutations.
/// </summary>
public sealed class FileChangeWiringEndToEndTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void A_print_run_publishes_full_payloads_for_matching_mutations_only()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string store = temp.File("store");
        HelloFixture.Install(Path.Combine(worktree, ".lunate", "extensions", "hello"));
        var log = new RecordingExtensionLog();
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call(
                    "call-1",
                    "write",
                    Scripts.Args(("path", "src/a.cs"), ("content", "class A {}\n"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(
                Scripts.Call(
                    "call-2",
                    "edit",
                    Scripts.Args(
                        ("path", "src/a.cs"),
                        ("old_text", "class A {}"),
                        ("new_text", "class A { }")
                    )
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(
                Scripts.Call(
                    "call-3",
                    "write",
                    Scripts.Args(("path", "notes.txt"), ("content", "plain\n"))
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(
                Scripts.Call(
                    "call-4",
                    "edit",
                    Scripts.Args(
                        ("path", "src/a.cs"),
                        ("old_text", "does not exist"),
                        ("new_text", "x")
                    )
                ),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        var workspace = new Workspace(worktree);
        var seams = new ExtensionSeams
        {
            StorePath = store,
            Prompt = new ScriptedTrustPrompt(true),
            Log = log,
        };
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "edit files",
            WorkingDirectory = worktree,
            Extensions = seams,
        };
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["-p", "--yolo", "edit files"],
            output,
            errors,
            Ct,
            options,
            extensions: seams
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output.ToString());
        Assert.True(File.Exists(Path.Combine(worktree, "notes.txt")));

        List<string> changed =
        [
            .. log.Messages.Where(message =>
                message.Contains("hello file changed:", StringComparison.Ordinal)
            ),
        ];
        Assert.Equal(2, changed.Count);
        string absolute = Path.Combine(workspace.WorktreeRoot, "src", "a.cs");
        string prefix =
            $"hello file changed: path={absolute}, relative=src/a.cs, workspace={workspace.WorktreeRoot}, run=";
        string created = Assert.Single(
            changed,
            message => message.Contains(", kind=created", StringComparison.Ordinal)
        );
        string modified = Assert.Single(
            changed,
            message => message.Contains(", kind=modified", StringComparison.Ordinal)
        );
        Assert.StartsWith(prefix, created, StringComparison.Ordinal);
        Assert.StartsWith(prefix, modified, StringComparison.Ordinal);

        Match firstRun = Regex.Match(created, @"run=(run_[0-9a-f]{32}),");
        Match secondRun = Regex.Match(modified, @"run=(run_[0-9a-f]{32}),");
        Assert.True(firstRun.Success, $"the created event carries no run id: {created}");
        Assert.True(secondRun.Success, $"the modified event carries no run id: {modified}");
        Assert.Equal(firstRun.Groups[1].Value, secondRun.Groups[1].Value);
    }
}
