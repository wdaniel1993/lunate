using Lunate.Protocols;

namespace Lunate.Coding.Tests;

/// <summary>
/// The per-server approval prompt: the interactive question names the server, its source file and
/// the full untruncated command; the non-interactive prompt always refuses.
/// </summary>
public sealed class McpServerApprovalPromptTests
{
    private const string Source = "/work/.agents/mcp.json";

    [Fact]
    public async Task The_tui_question_names_the_server_source_and_full_command()
    {
        (string? title, IReadOnlyList<string>? details) = (null, null);
        var prompt = new TuiMcpServerApprovalPrompt(
            (askedTitle, askedDetails, _) =>
            {
                (title, details) = (askedTitle, askedDetails);
                return ValueTask.FromResult(true);
            }
        );
        string longArgument = new('x', 200);
        McpServerOptions server = new("srv", "dotnet", ["exec", longArgument], Source: Source);

        bool approved = await prompt.ApproveAsync(server, TestContext.Current.CancellationToken);

        Assert.True(approved);
        Assert.Equal(
            $"Run project MCP server 'srv' from '{Source}' — it sees your shell environment",
            title
        );
        string detail = Assert.Single(details!);
        Assert.Equal($"dotnet exec {longArgument}", detail);
        Assert.DoesNotContain("…", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_interactive_refuses_without_output()
    {
        var prompt = new NonInteractiveMcpServerApprovalPrompt();

        bool approved = await prompt.ApproveAsync(
            new McpServerOptions("srv", "run", [], Source: Source),
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
    }
}
