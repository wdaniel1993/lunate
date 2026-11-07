using System.Globalization;
using Lunate.Agent;
using Lunate.Extensibility;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The print-mode skills parity: the same discovery + index path as the interactive session, the
/// non-interactive trust gate and the recorded prompt-section entry — through the real composition.
/// </summary>
public sealed class SkillsPrintModeTests
{
    private static void WriteSkill(
        string directory,
        string name,
        string description = "Does a thing",
        string body = "Body."
    )
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: {description}\n---\n\n{body}\n"
        );
    }

    [Fact]
    public async Task A_print_run_carries_the_index_and_records_the_section_entry()
    {
        using var temp = new TempDirectory();
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        WriteSkill(Path.Combine(worktree, ".agents", "skills", "greek"), "greek");
        string store = temp.File("store");
        var workspace = new Workspace(worktree);
        var scan = SkillScanner.ScanProject(
            new SkillScanRequest(worktree, workspace.RepoRoot, "", [])
        );
        List<SkillDescriptor> project =
        [
            .. scan.Skills.Where(skill => skill is { Valid: true, Scope: SkillScope.Project }),
        ];
        string hash = SkillTrust.ComputeContentHash(project, workspace.RepoRoot ?? worktree);
        SkillTrust.Record(
            new ExtensionTrustStore(new ExtensionHostOptions { StorePath = store }),
            workspace.GitCommonDir ?? worktree,
            workspace.WorktreeRoot,
            SkillTrust.Names(project),
            hash,
            DateTimeOffset.UtcNow
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            WorkingDirectory = worktree,
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
            },
        };

        (int exitCode, string output, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        // The real composed request carries the skills section.
        string system = Assert
            .Single(client.Requests[0], message => message.Role == ChatRole.System)
            .Text;
        Assert.Contains("- greek: Does a thing", system, StringComparison.Ordinal);
        // And the session records the section for replay.
        string path = Assert.Single(Directory.GetFiles(temp.File("sessions"), "*.jsonl"));
        SessionPromptSectionEntry section = Assert.Single(
            Session.Load(path).Entries.OfType<SessionPromptSectionEntry>()
        );
        Assert.Equal("skills", section.Section);
        Assert.Contains("- greek:", section.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_appended_directive_stays_before_the_skills_section()
    {
        using var temp = new TempDirectory();
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        WriteSkill(Path.Combine(worktree, ".agents", "skills", "greek"), "greek");
        string store = temp.File("store");
        var workspace = new Workspace(worktree);
        var scan = SkillScanner.ScanProject(
            new SkillScanRequest(worktree, workspace.RepoRoot, "", [])
        );
        List<SkillDescriptor> project =
        [
            .. scan.Skills.Where(skill => skill is { Valid: true, Scope: SkillScope.Project }),
        ];
        string hash = SkillTrust.ComputeContentHash(project, workspace.RepoRoot ?? worktree);
        SkillTrust.Record(
            new ExtensionTrustStore(new ExtensionHostOptions { StorePath = store }),
            workspace.GitCommonDir ?? worktree,
            workspace.WorktreeRoot,
            SkillTrust.Names(project),
            hash,
            DateTimeOffset.UtcNow
        );
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            WorkingDirectory = worktree,
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
            },
            Launch = new LaunchOptions { AppendSystemPrompt = "the appendix" },
        };

        (int exitCode, _, _) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        string system = Assert
            .Single(client.Requests[0], message => message.Role == ChatRole.System)
            .Text;
        Assert.Contains("the appendix", system, StringComparison.Ordinal);
        Assert.True(
            system.IndexOf("the appendix", StringComparison.Ordinal)
                < system.IndexOf("- greek:", StringComparison.Ordinal)
        );
        string path = Assert.Single(Directory.GetFiles(temp.File("sessions"), "*.jsonl"));
        SessionPromptSectionEntry[] sections =
        [
            .. Session.Load(path).Entries.OfType<SessionPromptSectionEntry>(),
        ];
        Assert.Equal(["system-append", "skills"], sections.Select(entry => entry.Section));
    }

    [Fact]
    public async Task A_print_run_without_trust_keeps_project_skills_out_and_warns()
    {
        using var temp = new TempDirectory();
        string worktree = Path.Combine(temp.Root, "worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        WriteSkill(Path.Combine(worktree, ".agents", "skills", "greek"), "greek");
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            WorkingDirectory = worktree,
            Extensions = new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);

        Assert.Equal(0, exitCode);
        Assert.Equal("ok", output);
        Assert.Contains("project skills were not trusted", errors, StringComparison.Ordinal);
        string system = Assert
            .Single(client.Requests[0], message => message.Role == ChatRole.System)
            .Text;
        Assert.DoesNotContain("greek", system, StringComparison.Ordinal);
    }

    /// <summary>
    /// A trusted print run over the given worktree with a model of the given context window
    /// (declared in the run's own models.json), returning the scripted client and the run result.
    /// </summary>
    internal static async Task<(
        ScriptedChatClient Client,
        int ExitCode,
        string Output,
        string Errors
    )> RunTrustedAsync(
        TempDirectory temp,
        string worktree,
        string modelId,
        int contextWindow,
        string? settingsJson = null
    )
    {
        string store = temp.File("store");
        var workspace = new Workspace(worktree);
        var scan = SkillScanner.ScanProject(
            new SkillScanRequest(worktree, workspace.RepoRoot, "", [])
        );
        List<SkillDescriptor> project =
        [
            .. scan.Skills.Where(skill => skill is { Valid: true, Scope: SkillScope.Project }),
        ];
        string hash = SkillTrust.ComputeContentHash(project, workspace.RepoRoot ?? worktree);
        SkillTrust.Record(
            new ExtensionTrustStore(new ExtensionHostOptions { StorePath = store }),
            workspace.GitCommonDir ?? worktree,
            workspace.WorktreeRoot,
            SkillTrust.Names(project),
            hash,
            DateTimeOffset.UtcNow
        );
        File.WriteAllText(
            temp.File("models.json"),
            $$"""{"schemaVersion":1,"models":[{"id":"{{modelId}}","provider":"openai","contextWindow":{{contextWindow.ToString(CultureInfo.InvariantCulture)}},"supportsTools":true}]}"""
        );
        File.WriteAllText(temp.File("settings.json"), settingsJson ?? """{"schemaVersion":2}""");
        var client = new ScriptedChatClient().Enqueue(Scripts.Text("ok"), Scripts.Stop());
        PrintModeOptions options = PrintModeTestSupport.BaseOptions(
            temp,
            new FakeChatClientFactory(client)
        ) with
        {
            Prompt = "Hi",
            WorkingDirectory = worktree,
            Environment = PrintModeTestSupport.Environment(model: modelId),
            Extensions = new ExtensionSeams
            {
                StorePath = store,
                Log = new RecordingExtensionLog(),
            },
        };

        (int exitCode, string output, string errors) = await PrintModeTestSupport.RunAsync(options);
        return (client, exitCode, output, errors);
    }

    [Fact]
    public async Task Twenty_one_skills_on_a_large_context_model_are_all_indexed_with_no_warning()
    {
        using var outer = new TempDirectory();
        string worktree = Path.Combine(outer.Root, "worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        for (var index = 0; index < 21; index++)
        {
            WriteSkill(
                Path.Combine(worktree, ".agents", "skills", $"skill-{index:D2}"),
                $"skill-{index:D2}"
            );
        }

        (ScriptedChatClient client, int exitCode, string _, string errors) = await RunTrustedAsync(
            outer,
            worktree,
            "large-model",
            300_000
        );

        Assert.Equal(0, exitCode);
        string system = Assert
            .Single(client.Requests[0], message => message.Role == ChatRole.System)
            .Text;
        for (var index = 0; index < 21; index++)
        {
            Assert.Contains($"- skill-{index:D2}: Does a thing", system, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("more skills (not indexed)", system, StringComparison.Ordinal);
        Assert.DoesNotContain("skills index", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_pack_over_the_soft_threshold_indexes_completely_and_warns()
    {
        using var outer = new TempDirectory();
        string worktree = Path.Combine(outer.Root, "worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        for (var index = 0; index < 6; index++)
        {
            WriteSkill(
                Path.Combine(worktree, ".agents", "skills", $"skill-{index:D2}"),
                $"skill-{index:D2}",
                description: new string('d', 120)
            );
        }

        (ScriptedChatClient client, int exitCode, string _, string errors) = await RunTrustedAsync(
            outer,
            worktree,
            "small-model",
            10_000
        );

        Assert.Equal(0, exitCode);
        string system = Assert
            .Single(client.Requests[0], message => message.Role == ChatRole.System)
            .Text;
        for (var index = 0; index < 6; index++)
        {
            Assert.Contains($"- skill-{index:D2}: ", system, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("more skills (not indexed)", system, StringComparison.Ordinal);
        Assert.Contains("stays fully indexed", errors, StringComparison.Ordinal);
        Assert.Contains("soft threshold", errors, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_small_context_model_crossing_the_ceiling_keeps_AGENTS_md_skills_and_names_the_count()
    {
        using var outer = new TempDirectory();
        string worktree = Path.Combine(outer.Root, "worktree");
        Directory.CreateDirectory(Path.Combine(worktree, ".git"));
        for (var index = 0; index < 10; index++)
        {
            WriteSkill(
                Path.Combine(worktree, ".agents", "skills", $"skill-{index:D2}"),
                $"skill-{index:D2}",
                description: new string('d', 1000)
            );
        }

        File.WriteAllText(
            Path.Combine(worktree, "AGENTS.md"),
            "Always run skill-00 first and skill-09 second.\n"
        );

        (ScriptedChatClient client, int exitCode, string _, string errors) = await RunTrustedAsync(
            outer,
            worktree,
            "tiny-model",
            4_000
        );

        Assert.Equal(0, exitCode);
        string system = Assert
            .Single(client.Requests[0], message => message.Role == ChatRole.System)
            .Text;
        // AGENTS.md-named skills keep their full lines; the rest overflow names-only.
        Assert.Contains("- skill-00: ", system, StringComparison.Ordinal);
        Assert.Contains("- skill-09: ", system, StringComparison.Ordinal);
        Assert.Contains("more skills (not indexed):", system, StringComparison.Ordinal);
        // The visible warning names how many skills lost their description.
        Assert.Contains("without their description", errors, StringComparison.Ordinal);
        Assert.Matches(@"\b[1-9]\d* of 10 skills", errors);
    }
}
