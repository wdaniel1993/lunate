using Lunate.Extensibility;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>The SkillSourceExtension fixture copied into this test project's output.</summary>
internal static class SkillExtensionFixture
{
    internal static string SourceDirectory =>
        Path.Combine(AppContext.BaseDirectory, "TestExtensions", "SkillSourceExtension");

    /// <summary>
    /// Installs the fixture (global or project) with one skill directory per entry under its
    /// <c>skills/</c> folder; each directory is a valid <c>SKILL.md</c> skill.
    /// </summary>
    internal static string Install(
        string targetDirectory,
        string id = "skills-ext",
        params (string Name, string Description)[] skills
    )
    {
        Directory.CreateDirectory(targetDirectory);
        File.Copy(
            Path.Combine(SourceDirectory, "SkillSourceExtension.dll"),
            Path.Combine(targetDirectory, "SkillSourceExtension.dll"),
            overwrite: true
        );
        File.WriteAllText(
            Path.Combine(targetDirectory, "extension.json"),
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"SkillSourceExtension.dll"}"""
        );
        foreach ((string name, string description) in skills)
        {
            string directory = Path.Combine(targetDirectory, "skills", name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "SKILL.md"),
                $"---\nname: {name}\ndescription: {description}\n---\n\nExtension body of {name}.\n"
            );
        }

        return targetDirectory;
    }
}

/// <summary>
/// Extension-contributed skills end to end: the tagged index and /skills view, the read tool's
/// access to contributed files, the per-extension budget drop, trust-denied contributions and the
/// shadowed copy rule — all through the real composition.
/// </summary>
public sealed class SkillExtensionTests
{
    [Fact]
    public void A_contributed_skill_is_tagged_and_its_files_are_readable_through_composition()
    {
        using var temp = new TempDirectory();
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        string store = temp.File("store");
        string extension = SkillExtensionFixture.Install(
            Path.Combine(store, "extensions", "skills-ext"),
            skills: [("greek", "From the extension")]
        );
        string skillFile = Path.Combine(extension, "skills", "greek", "SKILL.md");
        var client = new ScriptedChatClient()
            .Enqueue(
                Scripts.Call("call-1", "read", Scripts.Args(("path", skillFile))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("done"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Read the skill",
            WorkingDirectory = worktree,
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
            },
        };
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exitCode = Cli.Run(
            ["-p", "--json", "Read the skill"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            options,
            extensions: options.Extensions
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("Extension body of greek.", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(errors.ToString().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_interactive_view_tags_contributed_skills()
    {
        using var temp = new TemporarySkillStore();
        string store = temp.Store;
        SkillExtensionFixture.Install(
            Path.Combine(store, "extensions", "skills-ext"),
            skills: [("greek", "From the extension")]
        );
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: temp.Worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skills");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "greek (extension 'skills-ext')"
        );

        Assert.Contains(
            "greek (extension 'skills-ext') — From the extension",
            host.LastFrame,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Over_budget_contributions_drop_and_log()
    {
        using var temp = new TemporarySkillStore();
        string store = temp.Store;
        SkillExtensionFixture.Install(
            Path.Combine(store, "extensions", "skills-ext"),
            skills:
            [
                .. Enumerable
                    .Range(0, 40)
                    .Select(index => ($"ext-skill-{index:D2}", new string('d', 200))),
            ]
        );
        var log = new RecordingExtensionLog();
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = log },
            workingDirectory: temp.Worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        Assert.True(
            log.Contains("exceeded its context budget (2000"),
            "the drop must be logged; log:\n" + string.Join("\n", log.Messages)
        );

        // The merged index carries only the lines inside the budget.
        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        string system = host
            .Client.Requests[0]
            .Single(message => message.Role == Microsoft.Extensions.AI.ChatRole.System)
            .Text;
        int carried = system.Split("ext-skill-").Length - 1;
        Assert.True(carried > 0, "the budget must keep some lines");
        Assert.True(carried < 40, $"budget dropped nothing: {carried} lines carried");
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "ok");

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_untrusted_project_extension_contributes_nothing()
    {
        using var temp = new TemporarySkillStore();
        string store = temp.Store;
        SkillExtensionFixture.Install(
            Path.Combine(temp.Worktree, ".lunate", "extensions", "skills-ext"),
            skills: [("greek", "From the extension")]
        );
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
                Prompt = new ScriptedTrustPrompt(false),
            },
            workingDirectory: temp.Worktree
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skills");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "skills: none found");

        Assert.DoesNotContain("greek", host.Frames, StringComparison.Ordinal);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_project_skill_shadows_the_contributed_copy_and_warns()
    {
        using var temp = new TemporarySkillStore();
        string store = temp.Store;
        WriteSkill(Path.Combine(temp.Worktree, ".agents", "skills", "greek"), "greek");
        var workspace = new Workspace(temp.Worktree);
        var scan = SkillScanner.ScanProject(
            new SkillScanRequest(temp.Worktree, workspace.RepoRoot, "", [])
        );
        string hash = SkillTrust.ComputeContentHash(
            [.. scan.Skills.Where(skill => skill is { Valid: true, Scope: SkillScope.Project })],
            workspace.RepoRoot ?? temp.Worktree
        );
        new ExtensionTrustStore(new ExtensionHostOptions { StorePath = store }).RecordSkillsTrusted(
            workspace.GitCommonDir ?? temp.Worktree,
            workspace.WorktreeRoot,
            hash,
            DateTimeOffset.UtcNow
        );
        SkillExtensionFixture.Install(
            Path.Combine(store, "extensions", "skills-ext"),
            skills: [("greek", "From the extension")]
        );
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams { StorePath = store, Log = new RecordingExtensionLog() },
            workingDirectory: temp.Worktree
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        // The collision warning is a one-time session notice.
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "shadows");
        host.Console.SendText("/skills");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "greek (project, shadows extension 'skills-ext')"
        );

        // The shadowed copy is not listed separately: one greek segment, none from the extension.
        Assert.Equal(1, host.LastFrame.Split("greek (").Length - 1);
        Assert.DoesNotContain("From the extension", host.LastFrame, StringComparison.Ordinal);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static void WriteSkill(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: Project copy\n---\n\nProject body.\n"
        );
    }

    /// <summary>A worktree and store pair sharing one temp root.</summary>
    private sealed class TemporarySkillStore : IDisposable
    {
        private readonly TempDirectory _temp = new();

        internal TemporarySkillStore()
        {
            Worktree = Path.Combine(_temp.Root, "worktree");
            Directory.CreateDirectory(Path.Combine(Worktree, ".git"));
            Store = _temp.File("store");
        }

        internal string Worktree { get; }

        internal string Store { get; }

        public void Dispose() => _temp.Dispose();
    }
}
