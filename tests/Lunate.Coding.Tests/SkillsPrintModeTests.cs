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
        string hash = SkillTrust.ComputeContentHash(
            [.. scan.Skills.Where(skill => skill is { Valid: true, Scope: SkillScope.Project })],
            workspace.RepoRoot ?? worktree
        );
        new ExtensionTrustStore(new ExtensionHostOptions { StorePath = store }).RecordSkillsTrusted(
            workspace.GitCommonDir ?? worktree,
            workspace.WorktreeRoot,
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
}
