using Lunate.TestSupport;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The end-to-end proof that the state root is real: a full print-mode session — real composition,
/// a real tool call through the scripted provider — runs with <c>LUNATE_HOME</c> pointed at a temp
/// directory and <c>HOME</c>/<c>USERPROFILE</c> pointing at a directory that does not exist. It must
/// exit successfully, write its session under the redirected root, and leave that home nonexistent:
/// any write to it would create it, which is the strongest proof available on every platform (a
/// chmod-based one says nothing on Windows).
/// <para>
/// The whole proof runs in a child process, because <c>HOME</c> is process-wide and the tests beside
/// this one keep running.
/// </para>
/// </summary>
public sealed class LunateHomeIsolationTests
{
    private const string Method = nameof(
        A_full_session_writes_only_the_redirected_root_and_leaves_the_home_nonexistent
    );

    private const string StateVariable = "LUNATE_TEST_STATE";
    private const string WorktreeVariable = "LUNATE_TEST_WORKTREE";
    private const string MissingHomeVariable = "LUNATE_TEST_MISSING_HOME";

    [Fact]
    public async Task A_full_session_writes_only_the_redirected_root_and_leaves_the_home_nonexistent()
    {
        if (TestChildProcess.IsChild(Method))
        {
            await ProbeAsync();
            return;
        }

        using var temp = new TempDirectory();
        string state = temp.File("state");
        string worktree = temp.File("worktree");
        string missingHome = temp.File("missing-home");
        Directory.CreateDirectory(worktree);
        Assert.False(Directory.Exists(missingHome));

        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["LUNATE_HOME"] = state,
            [StateVariable] = state,
            [WorktreeVariable] = worktree,
            [MissingHomeVariable] = missingHome,
            ["HOME"] = missingHome,
            ["USERPROFILE"] = missingHome,
            ["LUNATE_MODEL"] = PrintModeTestSupport.ModelId,
        };

        (int exitCode, string output) = await TestChildProcess.RunAsync(
            typeof(LunateHomeIsolationTests),
            Method,
            environment
        );

        Assert.True(
            exitCode == 0,
            $"the isolated session failed (exit {exitCode}):{Environment.NewLine}{output}"
        );

        // The parent's own read: the child left no trace of the home it was told not to touch.
        Assert.False(
            Directory.Exists(missingHome),
            "the session created the profile directory it was told not to touch"
        );
        Assert.True(
            Directory.Exists(Path.Combine(state, "sessions")),
            "the session did not write under the redirected root"
        );
    }

    private static async Task ProbeAsync()
    {
        string state = Required(StateVariable);
        string worktree = Required(WorktreeVariable);
        string missingHome = Required(MissingHomeVariable);

        Assert.Equal(state, Environment.GetEnvironmentVariable("LUNATE_HOME"));
        Assert.Equal(missingHome, Environment.GetEnvironmentVariable("HOME"));
        Assert.Equal(missingHome, Environment.GetEnvironmentVariable("USERPROFILE"));
        Assert.False(Directory.Exists(missingHome));

        File.WriteAllText(Path.Combine(worktree, "note.txt"), "read-me-from-the-tool");

        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "read", Scripts.Args(("path", "note.txt"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());

        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exitCode = await PrintMode.RunAsync(
            // Deliberately no explicit settings, auth, session, extension, MCP or home paths: every
            // one of them resolves through the state root, which is the thing under test.
            new PrintModeOptions
            {
                Prompt = "read note.txt",
                WorkingDirectory = worktree,
                Factory = new FakeChatClientFactory(client),
            },
            output,
            errors,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        Assert.Equal("done", output.ToString());
        Assert.DoesNotContain("LUNATE_HOME", errors.ToString(), StringComparison.Ordinal);

        // The turn really ran a tool: the second provider request carries the read's result.
        Assert.Equal(2, client.Requests.Count);
        Assert.Contains(
            client.Requests[1].SelectMany(message => message.Contents),
            content =>
                content is FunctionResultContent result
                && string.Equals(result.CallId, "call-1", StringComparison.Ordinal)
                && (result.Result?.ToString() ?? string.Empty).Contains(
                    "read-me-from-the-tool",
                    StringComparison.Ordinal
                )
        );

        string sessions = Path.Combine(state, "sessions");
        Assert.True(Directory.Exists(sessions), "no session directory under the redirected root");
        Assert.NotEmpty(Directory.GetFiles(sessions, "*.jsonl", SearchOption.AllDirectories));

        // Nothing was written into the worktree, and the home the process was told about is still
        // absent — a single write to it would have created it.
        Assert.False(Directory.Exists(Path.Combine(worktree, ".lunate")));
        Assert.False(
            Directory.Exists(missingHome),
            "the session wrote into the profile directory it was told not to touch"
        );
    }

    private static string Required(string variable) =>
        Environment.GetEnvironmentVariable(variable)
        ?? throw new InvalidOperationException($"{variable} is not set for the child probe.");
}
