using Lunate.Extensibility;
using Lunate.Extensibility.Restore;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The end-to-end run (tasks.md 6.4): a fixture feed with no network, a bundle carrying one tool, one
/// skill and one dependency, a real session over the content the install wrote, a clean
/// <c>lunate packages --verify</c>, and an uninstall that leaves nothing behind — then the same
/// bundle installed into a project worktree.
/// </summary>
public sealed class PackageInstallEndToEndTests
{
    [Fact]
    public async Task A_user_install_loads_its_tool_and_skill_and_verifies_its_content()
    {
        using var feed = new PackageFeedFixture();
        feed.Dependency("Acme.Lib", "1.0.0");
        feed.Bundle(
            skills: ["greek"],
            dependencies: ["Acme.Lib"],
            tools: """["echo"]""",
            entryAssemblyFrom: "ToolExtension"
        );

        Assert.Equal(
            0,
            feed.Run(["Acme.Toolkit@1.4.2"], out string installed, out string installErrors)
        );
        Assert.Empty(installErrors);
        Assert.Contains("dependencies: Acme.Lib 1.0.0", installed, StringComparison.Ordinal);
        string extension = Path.Combine(feed.Store, "extensions", "acme-tools");
        Assert.True(File.Exists(Path.Combine(extension, "Acme.Lib.dll")));
        WriteToolSettings(feed.Store, "acme-tools");

        var prompt = new RecordingPrompt(true);
        var log = new RecordingExtensionLog();
        using var host = Session(feed, prompt, log);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // Installing is not trusting: the first load asks once, naming the package.
        Assert.Single(prompt.Requests);
        Assert.Equal("from package Acme.Toolkit@1.4.2 (local)", prompt.Requests[0].FromPackage);

        // The session offers the tool the package's extension registered.
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        await host.SendLineAsync("say hi");
        await host.WaitUntilAsync(() => host.Client.Requests.Count > 0);
        Assert.True(
            host.Client.RequestOptions[0]!
                .Tools!.OfType<AIFunction>()
                .Any(tool => tool.Name.EndsWith("echo", StringComparison.Ordinal)),
            "the packaged extension's tool was not offered; log:\n"
                + string.Join("\n", log.Messages)
        );

        // And lists the skill with its provenance.
        host.Console.SendText("/skills");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "greek (user");
        Assert.Contains("from Acme.Toolkit@1.4.2", host.LastFrame, StringComparison.Ordinal);

        Assert.Equal(PackagesCommand.ExitOk, Verify(feed));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    /// <summary>
    /// Uninstall as it happens in practice: in a process that never loaded the extension. On Windows
    /// a loaded assembly is mapped and cannot be deleted, which is the platform's rule and why the
    /// uninstall reports such a file as in use instead of pretending — the POSIX half of that path is
    /// asserted in the restore suite. Loading and removing in one process would be testing something
    /// no user can do.
    /// </summary>
    [Fact]
    public async Task An_install_uninstalls_completely_when_nothing_loaded_it()
    {
        using var feed = new PackageFeedFixture();
        feed.Dependency("Acme.Lib", "1.0.0");
        feed.Bundle(
            skills: ["greek"],
            dependencies: ["Acme.Lib"],
            tools: """["echo"]""",
            entryAssemblyFrom: "ToolExtension"
        );

        Assert.Equal(0, feed.Run(["Acme.Toolkit@1.4.2"], out _, out string installErrors));
        Assert.Empty(installErrors);
        string extension = Path.Combine(feed.Store, "extensions", "acme-tools");
        Assert.True(File.Exists(Path.Combine(extension, "Acme.Lib.dll")));
        Assert.Equal(PackagesCommand.ExitOk, Verify(feed));

        Assert.Equal(
            0,
            feed.RunUninstall(["Acme.Toolkit"], out string removed, out string uninstallErrors)
        );
        Assert.Empty(uninstallErrors);
        Assert.Contains("removed: ", removed, StringComparison.Ordinal);

        // Everything the record named is gone, the dependency assembly included.
        Assert.False(Directory.Exists(extension));
        Assert.False(Directory.Exists(Path.Combine(feed.Store, "skills", "greek")));
        Assert.False(
            File.Exists(Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json"))
        );
    }

    [Fact]
    public async Task A_project_install_commits_its_content_keeps_its_record_local_and_asks_once()
    {
        using var feed = new PackageFeedFixture();
        GitTestRepo.Init(feed.Worktree);
        feed.Dependency("Acme.Lib", "1.0.0");
        feed.Bundle(
            skills: ["greek"],
            dependencies: ["Acme.Lib"],
            tools: """["echo"]""",
            entryAssemblyFrom: "ToolExtension"
        );

        Assert.Equal(
            0,
            feed.Run(
                ["Acme.Toolkit@1.4.2", "--project"],
                out string installed,
                out string installErrors
            )
        );
        Assert.Empty(installErrors);
        Assert.Contains("scope: project", installed, StringComparison.Ordinal);

        // Content in the worktree, record in the state root.
        string extension = Path.Combine(feed.Worktree, ".lunate", "extensions", "acme-tools");
        Assert.True(File.Exists(Path.Combine(extension, "Acme.Lib.dll")));
        Assert.True(
            File.Exists(Path.Combine(feed.Worktree, ".agents", "skills", "greek", "SKILL.md"))
        );
        Assert.True(
            File.Exists(Path.Combine(feed.Store, "packages", "acme.toolkit", "install.json"))
        );
        string status = GitTestRepo.Output(feed.Worktree, "status", "--porcelain");
        Assert.Contains(".lunate/", status, StringComparison.Ordinal);
        Assert.Contains(".agents/", status, StringComparison.Ordinal);

        // What the session will ask is exactly what the pending set prints, provenance included.
        using var trustOut = new StringWriter();
        using var trustErr = new StringWriter();
        Assert.Equal(
            TrustCommand.ExitPending,
            TrustCommand.Run(
                [],
                feed.Worktree,
                trustOut,
                trustErr,
                new ExtensionSeams { StorePath = feed.Store }
            )
        );
        Assert.Contains(
            "  from package Acme.Toolkit@1.4.2 (local)",
            trustOut.ToString(),
            StringComparison.Ordinal
        );

        WriteToolSettings(feed.Store, "acme-tools");
        var prompt = new RecordingPrompt(true);
        var log = new RecordingExtensionLog();
        using var host = Session(feed, prompt, log);
        Task run = host.RunAsync();
        await ComposeAnsweringTrustAsync(host);

        // One extension question with the package named; the project skills question is the
        // session's own and is answered in the live area by the helper above.
        Assert.Single(prompt.Requests);
        Assert.Equal("from package Acme.Toolkit@1.4.2 (local)", prompt.Requests[0].FromPackage);

        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        await host.SendLineAsync("say hi");
        await host.WaitUntilAsync(() => host.Client.Requests.Count > 0);
        Assert.Contains(
            host.Client.RequestOptions[0]!.Tools!.OfType<AIFunction>(),
            tool => tool.Name.EndsWith("echo", StringComparison.Ordinal)
        );

        host.Console.SendText("/skills");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "greek (project");
        Assert.Contains("from Acme.Toolkit@1.4.2", host.LastFrame, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static InteractiveSessionHost Session(
        PackageFeedFixture feed,
        RecordingPrompt prompt,
        RecordingExtensionLog log
    )
    {
        var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = feed.Store,
                Log = log,
                Prompt = prompt,
            },
            workingDirectory: feed.Worktree,
            // The session's user scope is the state root the install wrote: one store, the same
            // rule the real CLI follows.
            homeDirectory: feed.Home
        );
        host.Console.Size = new ConsoleSize(600, 24);
        return host;
    }

    /// <summary>
    /// Waits for composition, approving the session's own project-skills question when it appears:
    /// only question in a session that is not the extension seam.
    /// </summary>
    private static async Task ComposeAnsweringTrustAsync(InteractiveSessionHost host)
    {
        DateTime deadline = DateTime.UtcNow + WaitBudgets.CrossTurn;
        while (!host.Session.IsComposed && DateTime.UtcNow < deadline)
        {
            if (host.Session.PendingTrustPrompt is not null)
            {
                host.Console.SendText("y");
            }

            await Task.Delay(20);
        }

        Assert.True(
            host.Session.IsComposed,
            "the session did not compose; a trust question may still be waiting:\n"
                + host.Session.PendingTrustPrompt?.Title
        );
    }

    /// <summary>The tool settings a packaged ToolExtension needs to register its tool.</summary>
    private static void WriteToolSettings(string store, string id)
    {
        string directory = Path.Combine(store, "extensions-settings");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, id + ".json"), """{"tools":[{"name":"echo"}]}""");
    }

    private static int Verify(PackageFeedFixture feed)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exit = PackagesCommand.Run(
            ["--verify"],
            feed.Worktree,
            output,
            errors,
            new ExtensionSeams { StorePath = feed.Store }
        );
        Assert.Empty(errors.ToString());
        return exit;
    }

    /// <summary>
    /// A prompt that records every question and answers from a fixed queue; exhaustion throws, so a
    /// test that expected no question fails loudly instead of approving implicitly.
    /// </summary>
    private sealed class RecordingPrompt(params bool[] decisions) : IExtensionTrustPrompt
    {
        private readonly Queue<bool> _decisions = new(decisions);

        internal List<ExtensionTrustRequest> Requests { get; } = [];

        public ValueTask<bool> ApproveAsync(
            ExtensionTrustRequest request,
            CancellationToken cancellationToken
        )
        {
            ArgumentNullException.ThrowIfNull(request);
            Requests.Add(request);
            if (!_decisions.TryDequeue(out bool decision))
            {
                throw new InvalidOperationException(
                    $"no decision queued for extension '{request.Descriptor.Id}'; the load asked a question it should not have asked."
                );
            }

            return ValueTask.FromResult(decision);
        }
    }
}
