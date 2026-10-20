namespace Lunate.Coding.Tests;

/// <summary>
/// The launch directives over the composed prompt (design D5): a replacement is the whole prompt,
/// an append is one section after it, and no directive keeps the composed bytes unchanged.
/// </summary>
public sealed class SystemPromptDirectiveTests
{
    private static readonly string[] Tools = ["read", "write", "edit", "bash"];

    private static readonly ShellResolver Shell = new("/bin/sh");

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_replacement_directive_returns_exactly_the_given_text()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        var directives = new LaunchOptions { SystemPrompt = "Use only the caller's contract." };

        string prompt = SystemPrompt.Compose(workspace, Tools, directives);

        Assert.Equal("Use only the caller's contract.", prompt);
        Assert.DoesNotContain("Tools:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Read a file before you edit it.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("{tools}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_append_directive_keeps_the_composed_prompt_and_adds_one_section()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        var directives = new LaunchOptions { AppendSystemPrompt = "Keep answers short." };

        string composed = SystemPrompt.Compose(workspace, Tools, Shell, Now);
        string prompt = SystemPrompt.Compose(workspace, Tools, directives, Shell, Now);

        Assert.Equal(composed + "\n\n" + "Keep answers short.", prompt);
    }

    [Fact]
    public void No_directive_leaves_the_composed_bytes_identical()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);

        string expected = SystemPrompt.Compose(workspace, Tools, Shell, Now);
        string prompt = SystemPrompt.Compose(workspace, Tools, new LaunchOptions(), Shell, Now);

        Assert.Equal(expected, prompt);
    }

    [Fact]
    public void A_replacement_wins_when_both_directives_are_given()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        var directives = new LaunchOptions
        {
            SystemPrompt = "Replace everything.",
            AppendSystemPrompt = "This is not used.",
        };

        string prompt = SystemPrompt.Compose(workspace, Tools, directives);

        Assert.Equal("Replace everything.", prompt);
    }
}
