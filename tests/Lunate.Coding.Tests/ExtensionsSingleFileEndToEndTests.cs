using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The phase-1 end-to-end proof (spec: "A file with a header appears in the extension list" and
/// "A file without a header is not compiled"): a <c>.cs</c> file in a session's project extensions
/// directory is discovered and shown by <c>/extensions</c>, and nothing compiles, restores or
/// prompts — the composed session makes no model call and the store gains no cache or trust file.
/// It shares the interactive tests' serial collection, like the other suites that compose a full
/// session with a console and a frame pump.
/// </summary>
[Collection("mcp-processes")]
public sealed class ExtensionsSingleFileEndToEndTests
{
    private const string Header = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue

        public static class Acme
        {
            public static void Register() { }
        }
        """;

    [Fact]
    public async Task A_single_file_extension_reads_not_built_and_nothing_is_built()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        string extensionsRoot = Path.Combine(worktree, ".lunate", "extensions");
        Directory.CreateDirectory(extensionsRoot);
        string file = Path.Combine(extensionsRoot, "acme.cs");
        File.WriteAllText(file, Header);
        string store = temp.File("store");
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "not-built");

        Assert.Contains(
            "acme-tools 1.0.0 (project, single file) not-built, tools 1, commands 0, hooks 0, services 0",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);
        Assert.Empty(Directory.GetFiles(extensionsRoot, "*.dll"));
        Assert.False(Directory.Exists(Path.Combine(store, "cache")));
        Assert.False(File.Exists(Path.Combine(store, "trust.json")));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_file_without_a_header_reads_invalid_no_extension_in_the_notice()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        string extensionsRoot = Path.Combine(worktree, ".lunate", "extensions");
        Directory.CreateDirectory(extensionsRoot);
        File.WriteAllText(Path.Combine(extensionsRoot, "broken.cs"), "public static class B { }\n");
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/extensions");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "invalid");

        Assert.Contains(
            "broken.cs (project, single file) invalid (no #:extension)",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Extensions_reload_answers_for_an_id_no_file_declares()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        string extensionsRoot = Path.Combine(worktree, ".lunate", "extensions");
        Directory.CreateDirectory(extensionsRoot);
        File.WriteAllText(Path.Combine(extensionsRoot, "acme.cs"), Header);
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
            workingDirectory: worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // The command is wired to the reload service and reports what the service reported, naming
        // the extension: an id no file declares is a refusal, not a crash or a silent no-op.
        host.Console.SendText("/extensions reload absent");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "was not reloaded");

        Assert.Contains(
            "extension 'absent' was not reloaded",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "is not a discovered single-file extension",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public void The_cli_lists_a_single_file_candidate_without_composing_a_model()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        string extensionsRoot = Path.Combine(worktree, ".lunate", "extensions");
        Directory.CreateDirectory(extensionsRoot);
        File.WriteAllText(Path.Combine(extensionsRoot, "acme.cs"), Header);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["extensions"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
            workingDirectory: worktree
        );

        Assert.Equal(ExtensionsCommand.ExitOk, exitCode);
        Assert.Equal(string.Empty, errors.ToString());
        Assert.Equal(
            "acme-tools 1.0.0 (project, single file) not-built, tools 1, commands 0, hooks 0, services 0",
            output.ToString().Trim()
        );
    }
}
