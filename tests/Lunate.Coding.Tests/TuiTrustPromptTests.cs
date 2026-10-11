using Lunate.Extensibility;
using Lunate.Protocols;

namespace Lunate.Coding.Tests;

public sealed class TuiTrustPromptTests
{
    private const string ConfigPath = "/work/.lunate/mcp.json";

    [Fact]
    public async Task Extension_question_names_id_version_scope_and_directory()
    {
        (string? title, IReadOnlyList<string>? details) = (null, null);
        var prompt = new TuiExtensionTrustPrompt(
            (askedTitle, askedDetails, _) =>
            {
                (title, details) = (askedTitle, askedDetails);
                return ValueTask.FromResult(true);
            }
        );

        bool approved = await prompt.ApproveAsync(
            new ExtensionTrustRequest(
                ExtensionTrustPromptTests.Descriptor("hello", "/work/.lunate/extensions/hello"),
                FilesChanged: false
            ),
            TestContext.Current.CancellationToken
        );

        Assert.True(approved);
        Assert.Equal(
            "Load project extension 'hello' 0.1.0 from /work/.lunate/extensions/hello",
            title
        );
        Assert.Empty(details!);
    }

    [Fact]
    public async Task A_changed_files_re_prompt_says_so()
    {
        string? title = null;
        var prompt = new TuiExtensionTrustPrompt(
            (askedTitle, _, _) =>
            {
                title = askedTitle;
                return ValueTask.FromResult(false);
            }
        );

        bool approved = await prompt.ApproveAsync(
            new ExtensionTrustRequest(
                ExtensionTrustPromptTests.Descriptor("hello", "/work/.lunate/extensions/hello"),
                FilesChanged: true
            ),
            TestContext.Current.CancellationToken
        );

        Assert.False(approved);
        Assert.Equal(
            "Load project extension 'hello' 0.1.0 from /work/.lunate/extensions/hello — its files changed since this repository trusted it. Load the new version",
            title
        );
    }

    [Fact]
    public async Task Mcp_question_names_the_path_the_servers_their_commands_and_the_count()
    {
        (string? title, IReadOnlyList<string>? details) = (null, null);
        var prompt = new TuiMcpConfigTrustPrompt(
            (askedTitle, askedDetails, _) =>
            {
                (title, details) = (askedTitle, askedDetails);
                return ValueTask.FromResult(true);
            }
        );
        List<McpServerOptions> servers =
        [
            new("alpha", "dotnet", ["exec", "a.dll"]),
            new("beta", "node", ["b.js"]),
        ];

        bool approved = await prompt.ApproveAsync(
            ConfigPath,
            servers,
            TestContext.Current.CancellationToken
        );

        Assert.True(approved);
        Assert.Equal("Start 2 servers from project mcp.json '/work/.lunate/mcp.json'", title);
        Assert.Equal(["alpha — dotnet exec a.dll", "beta — node b.js"], details);
    }

    [Fact]
    public async Task One_server_reads_in_the_singular()
    {
        string? title = null;
        var prompt = new TuiMcpConfigTrustPrompt(
            (askedTitle, _, _) =>
            {
                title = askedTitle;
                return ValueTask.FromResult(true);
            }
        );

        await prompt.ApproveAsync(
            ConfigPath,
            [new McpServerOptions("alpha", "node", ["a.js"])],
            TestContext.Current.CancellationToken
        );

        Assert.Equal("Start 1 server from project mcp.json '/work/.lunate/mcp.json'", title);
    }

    [Fact]
    public async Task A_long_server_command_is_truncated()
    {
        IReadOnlyList<string>? details = null;
        var prompt = new TuiMcpConfigTrustPrompt(
            (_, askedDetails, _) =>
            {
                details = askedDetails;
                return ValueTask.FromResult(true);
            }
        );

        await prompt.ApproveAsync(
            ConfigPath,
            [new McpServerOptions("alpha", "dotnet", [new string('x', 200)])],
            TestContext.Current.CancellationToken
        );

        string detail = Assert.Single(details!);
        Assert.StartsWith("alpha — dotnet xxx", detail, StringComparison.Ordinal);
        Assert.EndsWith("…", detail, StringComparison.Ordinal);
        Assert.Equal("alpha — ".Length + 61, detail.Length);
    }
}
