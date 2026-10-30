using System.Text.Json;
using Lunate.Agent;
using Lunate.TestSupport;

namespace Lunate.Protocols.Tests;

public sealed class McpFormElicitationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Client_negotiates_form_elicitation_without_url_mode()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));
        ITool tool = (await host.GetToolsAsync(Ct)).Single(candidate =>
            candidate.Name == "server__elicit_form"
        );

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            McpTestEnvironment.Context,
            Ct
        );

        Assert.False(result.IsError, result.Output);
        Assert.Contains(
            TestMcpServer.MarkerLines(marker),
            line => line == "elicitation capability=form:True;url:False"
        );
    }

    [Fact]
    public async Task Form_request_maps_to_shared_questions_and_returns_schema_valid_answers()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));
        ITool tool = (await host.GetToolsAsync(Ct)).Single(candidate =>
            candidate.Name == "server__elicit_form"
        );
        IReadOnlyList<UserQuestion>? presented = null;
        ToolContext context = McpTestEnvironment.Context with
        {
            AskUserAsync = (questions, _) =>
            {
                presented = questions;
                return Task.FromResult(
                    new UserQuestionResult(
                        UserQuestionStatus.Answered,
                        [
                            new UserQuestionAnswer([], "new project"),
                            new UserQuestionAnswer([], "3"),
                            new UserQuestionAnswer([], "true"),
                            new UserQuestionAnswer(["guided"], null),
                            new UserQuestionAnswer(["safe", "fast"], null),
                        ]
                    )
                );
            },
        };

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            context,
            Ct
        );

        Assert.False(result.IsError);
        Assert.NotNull(presented);
        Assert.Equal(5, presented.Count);
        Assert.Equal("server: Display name", presented[0].Header);
        Assert.Equal("server: Worker count", presented[1].Header);
        Assert.Equal("server: Enable feature", presented[2].Header);
        Assert.Equal("server: Plan", presented[3].Header);
        Assert.Equal("server: Tags", presented[4].Header);
        Assert.All(
            presented,
            question =>
                Assert.Contains(
                    "Choose settings for the generated project.",
                    question.Question,
                    StringComparison.Ordinal
                )
        );
        Assert.Equal("Display name", presented[0].Header.Split(": ", 2).Last());
        Assert.True(presented[0].AllowFreeText);
        Assert.Equal(["guided", "manual"], presented[3].Options.Select(option => option.Label));
        Assert.False(presented[3].MultiSelect);
        Assert.Equal(["safe", "fast"], presented[4].Options.Select(option => option.Label));
        Assert.True(presented[4].MultiSelect);

        using JsonDocument response = JsonDocument.Parse(result.Output);
        Assert.Equal("accept", response.RootElement.GetProperty("Action").GetString());
        JsonElement content = response.RootElement.GetProperty("Content");
        Assert.Equal("new project", content.GetProperty("display").GetString());
        Assert.Equal(3, content.GetProperty("count").GetInt32());
        Assert.True(content.GetProperty("enabled").GetBoolean());
        Assert.Equal("guided", content.GetProperty("plan").GetString());
        Assert.Equal(
            ["safe", "fast"],
            content.GetProperty("tags").EnumerateArray().Select(value => value.GetString())
        );
    }

    [Fact]
    public async Task Invalid_answer_is_declined_without_content()
    {
        using var temp = new TempDirectory();
        string marker = temp.File("marker.txt");
        await using var host = new McpServerHost(McpTestEnvironment.Options(marker));
        ITool tool = (await host.GetToolsAsync(Ct)).Single(candidate =>
            candidate.Name == "server__elicit_form"
        );
        ToolContext context = McpTestEnvironment.Context with
        {
            AskUserAsync = (questions, _) =>
                Task.FromResult(
                    new UserQuestionResult(
                        UserQuestionStatus.Answered,
                        [
                            new UserQuestionAnswer([], "new project"),
                            new UserQuestionAnswer([], "3"),
                            new UserQuestionAnswer([], "true"),
                            new UserQuestionAnswer(["unknown"], null),
                            new UserQuestionAnswer(["safe"], null),
                        ]
                    )
                ),
        };

        ToolResult result = await tool.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { }),
            context,
            Ct
        );

        Assert.False(result.IsError);
        using JsonDocument response = JsonDocument.Parse(result.Output);
        Assert.Equal("decline", response.RootElement.GetProperty("Action").GetString());
        Assert.True(
            !response.RootElement.TryGetProperty("Content", out JsonElement content)
                || content.ValueKind == JsonValueKind.Null
        );
    }
}
