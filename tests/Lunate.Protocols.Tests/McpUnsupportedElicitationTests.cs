using System.Text.Json;
using Lunate.Agent;
using Lunate.TestSupport;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Lunate.Protocols.Tests;

public sealed class McpUnsupportedElicitationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Url_mode_is_rejected_with_actionable_invalid_params()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));
        ITool tool = (await host.GetToolsAsync(Ct)).Single(candidate =>
            candidate.Name == "server__elicit_url"
        );
        bool interactionOpened = false;
        ToolContext context = McpTestEnvironment.Context with
        {
            AskUserAsync = (_, _) =>
            {
                interactionOpened = true;
                return Task.FromResult(new UserQuestionResult(UserQuestionStatus.Skipped, []));
            },
        };

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            context,
            Ct
        );

        Assert.False(result.IsError, result.Output);
        Assert.False(interactionOpened);
        IReadOnlyList<string> lines = TestMcpServer.MarkerLines(marker);
        Assert.True(
            lines.Any(line =>
                line.Contains(
                    "url elicitation error=InvalidOperationException",
                    StringComparison.Ordinal
                )
                && line.Contains(
                    "Client does not support URL mode elicitation requests",
                    StringComparison.Ordinal
                )
            ),
            string.Join(Environment.NewLine, lines)
        );
    }

    [Fact]
    public async Task Url_mode_handler_returns_actionable_invalid_params()
    {
        var request = new ElicitRequestParams
        {
            Mode = "url",
            Url = "https://example.invalid/elicitation",
            ElicitationId = "test-url-elicitation",
            Message = "Complete the external setup.",
        };

        McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(async () =>
            await McpElicitationHandler.HandleAsync(
                request,
                "server",
                new McpElicitationOutcome(),
                Ct
            )
        );

        Assert.Equal(McpErrorCode.InvalidParams, exception.ErrorCode);
        Assert.Contains("URL mode is not supported", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mode 'form'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Esc_cancels_form_without_returning_content()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));
        ITool tool = (await host.GetToolsAsync(Ct)).Single(candidate =>
            candidate.Name == "server__elicit_form"
        );
        ToolContext context = McpTestEnvironment.Context with
        {
            AskUserAsync = (_, _) =>
                Task.FromResult(new UserQuestionResult(UserQuestionStatus.Skipped, [])),
        };

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            context,
            Ct
        );

        if (result.IsError)
        {
            throw new InvalidOperationException(
                result.Output
                    + Environment.NewLine
                    + string.Join(Environment.NewLine, TestMcpServer.MarkerLines(marker))
            );
        }
        using JsonDocument response = ParseMcpResponse(result.Output);
        Assert.Equal("cancel", response.RootElement.GetProperty("Action").GetString());
        Assert.True(
            !response.RootElement.TryGetProperty("Content", out JsonElement content)
                || content.ValueKind == JsonValueKind.Null
        );
    }

    private static JsonDocument ParseMcpResponse(string output)
    {
        int elicitation = output.IndexOf("\nMCP elicitation from server", StringComparison.Ordinal);
        return JsonDocument.Parse(elicitation < 0 ? output : output[..elicitation]);
    }
}
