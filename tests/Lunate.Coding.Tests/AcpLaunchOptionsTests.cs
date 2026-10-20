using System.Text;
using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>
/// The flags ACP may carry: the composition flags parse through the one launch parser, the print
/// and selection flags stay usage errors, and a launch model resolves over the settings at
/// <c>session/new</c> with the catalog's actionable message when it is unknown.
/// </summary>
public sealed class AcpLaunchOptionsTests
{
    public static TheoryData<string[]> CompositionFlags =>
        new()
        {
            { ["--model", "gpt-4o", "--thinking", "high"] },
            { ["--tools", "bash,read"] },
            { ["--no-tools"] },
            { ["--system-prompt", "be brief"] },
            { ["--append-system-prompt", "extra"] },
        };

    public static TheoryData<string[]> PrintFlags =>
        new()
        {
            { ["-p", "hi"] },
            { ["--json"] },
            { ["--yolo"] },
        };

    public static TheoryData<string[]> SelectionFlags =>
        new()
        {
            { ["--continue"] },
            { ["--resume"] },
            { ["--session", "s_1"] },
            { ["--no-session"] },
        };

    [Theory]
    [MemberData(nameof(CompositionFlags))]
    public void Acp_accepts_the_composition_flags(string[] args)
    {
        Assert.True(
            Cli.TryParseAcpOptions(args, out LaunchOptions launch, out string? error),
            error
        );
        Assert.False(launch.Print);
    }

    [Fact]
    public void Acp_parses_the_model_and_thinking_flags()
    {
        Assert.True(
            Cli.TryParseAcpOptions(
                ["--model", "gpt-4o", "--thinking", "high"],
                out LaunchOptions launch,
                out string? error
            ),
            error
        );

        Assert.Equal("gpt-4o", launch.Model);
        Assert.Equal(ThinkingLevel.High, launch.Thinking);
    }

    [Theory]
    [MemberData(nameof(PrintFlags))]
    public void Acp_rejects_print_flags(string[] args)
    {
        Assert.False(Cli.TryParseAcpOptions(args, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [MemberData(nameof(SelectionFlags))]
    public void Acp_rejects_session_selection_flags(string[] args)
    {
        Assert.False(Cli.TryParseAcpOptions(args, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Acp_rejects_a_positional_prompt()
    {
        Assert.False(Cli.TryParseAcpOptions(["hi"], out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void An_acp_launch_model_not_in_the_catalog_fails_the_session_creation()
    {
        using var temp = new TempDirectory();
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new StringWriter();
        Write(
            input,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientInfo":{"name":"test","version":"0.0.1"},"clientCapabilities":{}}}"""
        );
        Write(
            input,
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "session/new",
                    @params = new { cwd = temp.Root, mcpServers = Array.Empty<string>() },
                }
            )
        );
        input.Position = 0;

        int exitCode = AcpMode.Run(
            errors,
            input,
            output,
            TestContext.Current.CancellationToken,
            TestExtensionSeams.Empty(temp),
            mcpConfigPath: temp.File("global-mcp.json"),
            mcpAgentsConfigPath: temp.File("agents-mcp.json"),
            launch: new LaunchOptions { Model = "nope-9000" }
        );

        Assert.Equal(0, exitCode);
        string stdout = Encoding.UTF8.GetString(output.ToArray());
        // The protocol escapes the apostrophes in the JSON error frame; the id and the catalog
        // sentence are the contract.
        Assert.Contains("nope-9000", stdout, StringComparison.Ordinal);
        Assert.Contains("is not in the catalog", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public void An_acp_unknown_tool_name_fails_the_session_creation()
    {
        using var temp = new TempDirectory();
        using var input = new MemoryStream();
        using var output = new MemoryStream();
        using var errors = new StringWriter();
        Write(
            input,
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":1,"clientInfo":{"name":"test","version":"0.0.1"},"clientCapabilities":{}}}"""
        );
        Write(
            input,
            JsonSerializer.Serialize(
                new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "session/new",
                    @params = new { cwd = temp.Root, mcpServers = Array.Empty<string>() },
                }
            )
        );
        input.Position = 0;

        int exitCode = AcpMode.Run(
            errors,
            input,
            output,
            TestContext.Current.CancellationToken,
            TestExtensionSeams.Empty(temp),
            mcpConfigPath: temp.File("global-mcp.json"),
            mcpAgentsConfigPath: temp.File("agents-mcp.json"),
            launch: new LaunchOptions { Tools = ["nope"] }
        );

        Assert.Equal(0, exitCode);
        string stdout = Encoding.UTF8.GetString(output.ToArray());
        Assert.Contains("unknown tools", stdout, StringComparison.Ordinal);
        Assert.Contains("nope", stdout, StringComparison.Ordinal);
        Assert.Contains("read", stdout, StringComparison.Ordinal);
    }

    private static void Write(Stream stream, string line) =>
        stream.Write(Encoding.UTF8.GetBytes(line + "\n"));
}
