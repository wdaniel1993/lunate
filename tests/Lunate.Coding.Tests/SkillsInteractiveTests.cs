using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The skills surfaces end to end through the real interactive session: the /skills view, the
/// trust question, the /skill:&lt;name&gt; invocation (wrapped user turn, arguments, notices, busy
/// queue parity), the once-per-session index and /context.
/// </summary>
public sealed class SkillsInteractiveTests
{
    /// <summary>One interactive session over a repository worktree, a store and a home, all in temp.</summary>
    private sealed class SkillsHost : IDisposable
    {
        private readonly TempDirectory _shared = new();

        internal SkillsHost()
        {
            Worktree = Path.Combine(_shared.Root, "worktree");
            Directory.CreateDirectory(Worktree);
            Directory.CreateDirectory(Path.Combine(Worktree, ".git"));
            Store = Path.Combine(_shared.Root, "store");
            Host = new InteractiveSessionHost(
                extensions: new ExtensionSeams
                {
                    StorePath = Store,
                    Log = new RecordingExtensionLog(),
                },
                workingDirectory: Worktree
            );
        }

        internal string Worktree { get; }

        internal string Store { get; }

        internal InteractiveSessionHost Host { get; }

        internal string Home => Host.Temp.File("home");

        internal void ProjectSkill(
            string name,
            string description = "Does a thing",
            string body = "Body of the skill.",
            string? extra = null
        ) =>
            WriteSkill(
                Path.Combine(Worktree, ".agents", "skills", name),
                name,
                description,
                body,
                extra
            );

        internal void UserSkill(
            string name,
            string description = "Does a thing",
            string body = "Body of the skill.",
            string? extra = null
        ) =>
            WriteSkill(
                Path.Combine(Home, ".agents", "skills", name),
                name,
                description,
                body,
                extra
            );

        internal void TrustProjectSkills()
        {
            var workspace = new Workspace(Worktree);
            List<SkillDescriptor> project =
            [
                .. SkillScanner
                    .ScanProject(new SkillScanRequest(Worktree, workspace.RepoRoot, "", []))
                    .Skills.Where(skill => skill is { Valid: true, Scope: SkillScope.Project }),
            ];
            string hash = SkillTrust.ComputeContentHash(project, workspace.RepoRoot ?? Worktree);
            new ExtensionTrustStore(
                new ExtensionHostOptions { StorePath = Store }
            ).RecordSkillsTrusted(
                workspace.GitCommonDir ?? Worktree,
                workspace.WorktreeRoot,
                hash,
                DateTimeOffset.UtcNow
            );
        }

        public void Dispose()
        {
            Host.Dispose();
            _shared.Dispose();
        }
    }

    private static void WriteSkill(
        string directory,
        string name,
        string description,
        string body,
        string? extra
    )
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: {description}\n{extra ?? string.Empty}---\n\n{body}\n"
        );
    }

    private static string SystemText(IReadOnlyList<ChatMessage> request) =>
        request.Single(message => message.Role == ChatRole.System).Text;

    private static List<SessionMessageEntry> Messages(InteractiveSessionHost host) =>
        [
            .. Session
                .Load(Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl")))
                .Entries.OfType<SessionMessageEntry>(),
        ];

    [Fact]
    public async Task Skills_lists_scopes_and_markers_without_a_model_call()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("greek");
        fixture.UserSkill("secret", extra: "disable-model-invocation: true\n");
        string broken = Path.Combine(fixture.Worktree, ".agents", "skills", "broken");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "SKILL.md"), "---\nname: broken\n---\n");
        fixture.TrustProjectSkills();
        InteractiveSessionHost host = fixture.Host;
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skills");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "skills: greek (project)");

        string frame = host.LastFrame;
        Assert.Contains("greek (project) — Does a thing", frame, StringComparison.Ordinal);
        Assert.Contains("secret (user, command-only) — ", frame, StringComparison.Ordinal);
        Assert.Contains(
            "broken (invalid) — frontmatter is missing a 'description'",
            frame,
            StringComparison.Ordinal
        );
        Assert.Empty(host.Client.Requests);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Skills_with_nothing_found_reads_none_found()
    {
        using var fixture = new SkillsHost();
        InteractiveSessionHost host = fixture.Host;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skills");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "skills: none found");

        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_trust_question_lists_the_names_and_y_loads_them()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("greek");
        InteractiveSessionHost host = fixture.Host;
        Task run = host.RunAsync();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Load project skills from this repository"
        );

        Assert.Contains("skills: greek", host.Frames, StringComparison.Ordinal);
        host.Console.SendText("y");
        await host.WaitUntilAsync(() => host.Session.IsComposed);
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "remembered for this repository"
        );

        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.Contains("greek", SystemText(host.Client.Requests[0]), StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Declining_leaves_project_skills_untrusted_and_out_of_the_index()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("greek");
        InteractiveSessionHost host = fixture.Host;
        Task run = host.RunAsync();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Load project skills from this repository"
        );

        host.Console.SendText("n");
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skills");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "greek (project, untrusted)");

        host.Client.Enqueue(Scripts.Text("ok"), Scripts.Stop());
        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        Assert.DoesNotContain(
            "greek",
            SystemText(host.Client.Requests[0]),
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Invoking_a_skill_submits_the_wrapped_body_and_records_the_metadata()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("greek", body: "Speak Greek.");
        fixture.TrustProjectSkills();
        InteractiveSessionHost host = fixture.Host;
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skill:greek");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "done");

        string userText = host
            .Client.Requests[0]
            .Single(message => message.Role == ChatRole.User)
            .Text;
        Assert.Contains("<skill name=\"greek\" dir=\"", userText, StringComparison.Ordinal);
        Assert.Contains("Speak Greek.", userText, StringComparison.Ordinal);
        Assert.EndsWith("</skill>", userText, StringComparison.Ordinal);
        Assert.Single(host.Client.Requests);

        SessionMessageEntry entry = Assert.Single(
            Messages(host),
            message => message.Skill is not null
        );
        Assert.Equal("greek", entry.Skill!.Name);
        Assert.Equal(
            Workspace.Canonicalize(Path.Combine(fixture.Worktree, ".agents", "skills", "greek")),
            entry.Skill.Dir
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Arguments_substitute_or_append()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("sub", body: "Translate $ARGUMENTS now.");
        fixture.ProjectSkill("tail", body: "No token here.");
        fixture.TrustProjectSkills();
        InteractiveSessionHost host = fixture.Host;
        host.Client.Enqueue(Scripts.Text("one"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("two"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skill:sub alpha beta");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "one");
        Assert.Contains(
            "Translate alpha beta now.",
            host.Client.Requests[0].Single(m => m.Role == ChatRole.User).Text,
            StringComparison.Ordinal
        );

        host.Console.SendText("/skill:tail alpha beta");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "two");
        string second = host.Client.Requests[1].Last(m => m.Role == ChatRole.User).Text;
        Assert.Contains("No token here.", second, StringComparison.Ordinal);
        Assert.Contains("ARGUMENTS: alpha beta\n", second, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Arguments_token_without_arguments_substitutes_empty()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("sub", body: "Translate $ARGUMENTS now.");
        fixture.TrustProjectSkills();
        InteractiveSessionHost host = fixture.Host;
        host.Client.Enqueue(Scripts.Text("one"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skill:sub");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "one");
        string text = host.Client.Requests[0].Single(m => m.Role == ChatRole.User).Text;
        Assert.Contains("Translate  now.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("$ARGUMENTS", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ARGUMENTS: ", text, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_unknown_skill_is_a_dim_notice_without_a_run()
    {
        using var fixture = new SkillsHost();
        InteractiveSessionHost host = fixture.Host;
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skill:ghost");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "unknown skill: ghost (see /skills)"
        );

        Assert.Empty(host.Client.Requests);
        Assert.False(host.Session.IsRunning);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task An_untrusted_skill_refusal_names_the_fix()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("greek");
        InteractiveSessionHost host = fixture.Host;
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Load project skills from this repository"
        );
        host.Console.SendText("n");
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/skill:greek");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "is not trusted in this repository"
        );

        Assert.Contains("lunate trust", host.LastFrame, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task A_skill_command_is_refused_by_the_follow_up_queue()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("greek");
        fixture.TrustProjectSkills();
        InteractiveSessionHost host = fixture.Host;
        host.Client.Enqueue(Scripts.Text("first"), Scripts.Stop());
        host.Client.Gate(1);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("hello");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        host.Console.SendText("/skill:greek");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/skill:greek");
        host.Console.SendEnter();
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            return host.LastFrame.Contains(
                "slash command cannot be queued",
                StringComparison.Ordinal
            );
        });

        Assert.Equal(0, host.Session.QueuedFollowUpCount);
        Assert.Equal("/skill:greek", host.Session.Input.Text);
        Assert.Single(host.Client.Requests);

        host.Client.Release(1);
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendCtrlC();
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_index_is_built_once_and_rebuilt_only_on_a_reload()
    {
        using var fixture = new SkillsHost();
        string skillDirectory = Path.Combine(fixture.Worktree, ".agents", "skills", "greek");
        fixture.ProjectSkill("greek", description: "First description");
        fixture.TrustProjectSkills();
        InteractiveSessionHost host = fixture.Host;
        host.Client.Enqueue(Scripts.Text("a1"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a2"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a3"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "a1");
        Assert.Contains(
            "First description",
            SystemText(host.Client.Requests[0]),
            StringComparison.Ordinal
        );

        // A mid-run skill change never rebuilds the running session's index.
        WriteSkill(skillDirectory, "greek", "Second description", "Body.", null);
        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "a2");
        Assert.Equal(SystemText(host.Client.Requests[0]), SystemText(host.Client.Requests[1]));

        // The content change re-prompts on the reload; approve again, then the new index loads.
        host.Console.SendText("/new");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Load project skills from this repository"
        );
        host.Console.SendText("y");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");
        await host.WaitUntilAsync(() => !host.Session.IsRunning);
        host.Console.SendText("three");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(3);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "a3");
        Assert.Contains(
            "Second description",
            SystemText(host.Client.Requests[2]),
            StringComparison.Ordinal
        );
        Assert.DoesNotContain(
            "First description",
            SystemText(host.Client.Requests[2]),
            StringComparison.Ordinal
        );

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_session_records_the_skills_section_entry_once()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("greek");
        fixture.TrustProjectSkills();
        InteractiveSessionHost host = fixture.Host;
        host.Client.Enqueue(Scripts.Text("a1"), Scripts.Stop());
        host.Client.Enqueue(Scripts.Text("a2"), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("one");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "a1");
        host.Console.SendText("two");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "a2");

        string path = Assert.Single(Directory.GetFiles(host.Temp.File("sessions"), "*.jsonl"));
        SessionPromptSectionEntry section = Assert.Single(
            Session.Load(path).Entries.OfType<SessionPromptSectionEntry>()
        );
        Assert.Equal("skills", section.Section);
        Assert.Contains("- greek:", section.Text, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task Context_reports_the_index_size_without_a_model_call()
    {
        using var fixture = new SkillsHost();
        fixture.ProjectSkill("greek");
        fixture.TrustProjectSkills();
        InteractiveSessionHost host = fixture.Host;
        host.Console.Size = new ConsoleSize(400, 24);
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/context");
        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "skills: 1 (index ~");

        Assert.Contains("tokens)", host.LastFrame, StringComparison.Ordinal);
        Assert.Empty(host.Client.Requests);
        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }
}
