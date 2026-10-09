using System.Runtime.InteropServices;

namespace Lunate.Coding.Tests;

public sealed class SystemPromptTests
{
    private static readonly string[] DefaultTools = ["read", "write", "edit", "bash"];

    [Fact]
    public void Composer_fills_every_named_token_with_runtime_facts()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        DateTimeOffset now = new(2026, 10, 9, 12, 30, 0, TimeSpan.Zero);

        string prompt = SystemPrompt.Compose(
            workspace,
            DefaultTools,
            new ShellResolver("/bin/sh"),
            now
        );

        Assert.Contains($"at {workspace.WorktreeRoot}", prompt, StringComparison.Ordinal);
        Assert.Contains(RuntimeInformation.OSDescription, prompt, StringComparison.Ordinal);
        // /bin/sh is the platform-independent resolver branch: its display name is "/bin/sh"
        // on every OS, unlike "/bin/bash" which maps to "Git Bash" on Windows. This keeps the
        // assertion a real falsifier (a composer ignoring the resolver would print "bash" or
        // "Git Bash", never "/bin/sh") while staying deterministic on all three runners.
        Assert.Contains("shell: /bin/sh", prompt, StringComparison.Ordinal);
        Assert.Contains("date (UTC): 2026-10-09", prompt, StringComparison.Ordinal);
        Assert.Contains("Tools: read, write, edit, bash", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{cwd}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{os}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{shell}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{tools}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{date}", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{agents}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_reports_no_tools_explicitly()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);

        string prompt = SystemPrompt.Compose(workspace, [], new ShellResolver("/bin/bash"));

        Assert.Contains("Tools: none", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Composed_prompt_stays_under_the_token_budget_with_an_empty_chain()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);

        string prompt = SystemPrompt.Compose(
            workspace,
            DefaultTools,
            new ShellResolver("/bin/bash")
        );

        // Budget heuristic (design): English prose averages about four characters per
        // token; no tokenizer dependency. The template keeps headroom below the cap.
        int estimatedTokens = prompt.Length / 4;
        Assert.True(estimatedTokens < 1000, $"estimated {estimatedTokens} tokens");
        Assert.True(
            estimatedTokens < 750,
            $"estimated {estimatedTokens} tokens: headroom too small"
        );
    }

    [Fact]
    public void Braces_in_agents_content_are_not_treated_as_format_tokens()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Root, "AGENTS.md"), "Use {braces} and {} literally.");
        var workspace = new Workspace(temp.Root);

        string prompt = SystemPrompt.Compose(
            workspace,
            DefaultTools,
            new ShellResolver("/bin/bash")
        );

        Assert.Contains("Use {braces} and {} literally.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Agents_chain_is_appended_after_the_working_rules()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Root, ".git"));
        File.WriteAllText(Path.Combine(temp.Root, "AGENTS.md"), "root rules");
        var workspace = new Workspace(temp.Root);

        string prompt = SystemPrompt.Compose(
            workspace,
            DefaultTools,
            new ShellResolver("/bin/bash")
        );

        int rulesIndex = prompt.IndexOf(
            "Read a file before you edit it.",
            StringComparison.Ordinal
        );
        int agentsIndex = prompt.IndexOf("root rules", StringComparison.Ordinal);
        Assert.True(rulesIndex >= 0 && agentsIndex > rulesIndex);
        Assert.Contains("### AGENTS.md (AGENTS.md)", prompt, StringComparison.Ordinal);
    }
}
