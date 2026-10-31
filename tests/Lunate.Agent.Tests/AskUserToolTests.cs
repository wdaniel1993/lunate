using System.Text.Json;

namespace Lunate.Agent.Tests;

public sealed class AskUserToolTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Accepts_one_through_four_questions(int count)
    {
        IReadOnlyList<UserQuestion>? received = null;
        var tool = new AskUserTool();
        ToolContext context = CreateContext(
            (questions, _) =>
            {
                received = questions;
                return AnswerAll(questions);
            }
        );

        ToolResult result = await Execute(tool, context, Arguments(count, 2));

        Assert.False(result.IsError);
        Assert.Equal(count, received!.Count);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Accepts_two_through_four_options(int count)
    {
        IReadOnlyList<UserQuestion>? received = null;
        var tool = new AskUserTool();
        ToolContext context = CreateContext(
            (questions, _) =>
            {
                received = questions;
                return AnswerAll(questions);
            }
        );

        ToolResult result = await Execute(tool, context, Arguments(1, count));

        Assert.False(result.IsError);
        Assert.Equal(count, received![0].Options.Count);
    }

    [Fact]
    public async Task Preserves_question_headers_and_option_descriptions()
    {
        IReadOnlyList<UserQuestion>? received = null;
        var tool = new AskUserTool();
        ToolContext context = CreateContext(
            (questions, _) =>
            {
                received = questions;
                return AnswerAll(questions);
            }
        );

        await Execute(tool, context, Arguments(1, 2));

        Assert.Equal("Question 1", received![0].Header);
        Assert.Equal("Prompt 1", received[0].Question);
        Assert.Equal("Description 1", received[0].Options[0].Description);
    }

    [Fact]
    public async Task Preserves_single_and_multi_select_modes()
    {
        IReadOnlyList<UserQuestion>? received = null;
        var tool = new AskUserTool();
        ToolContext context = CreateContext(
            (questions, _) =>
            {
                received = questions;
                return AnswerAll(questions);
            }
        );

        await Execute(tool, context, Arguments(2, 2));

        Assert.False(received![0].MultiSelect);
        Assert.True(received[1].MultiSelect);
    }

    [Fact]
    public async Task Moves_the_recommended_option_first_without_selecting_it()
    {
        IReadOnlyList<UserQuestion>? received = null;
        var tool = new AskUserTool();
        ToolContext context = CreateContext(
            (questions, _) =>
            {
                received = questions;
                return Task.FromResult(new UserQuestionResult(UserQuestionStatus.Skipped, []));
            }
        );
        using JsonDocument arguments = JsonDocument.Parse(
            """{"questions":[{"header":"Target","question":"Where?","options":[{"label":"Local","description":"On this machine"},{"label":"Remote","description":"On a server"}],"multiSelect":false,"recommendedOption":"Remote"}]}"""
        );

        ToolResult result = await tool.ExecuteAsync(
            arguments.RootElement,
            context,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("Remote", received![0].Options[0].Label);
        Assert.True(received[0].Options[0].Recommended);
        Assert.Equal("Local", received[0].Options[1].Label);
        Assert.False(received[0].Options[1].Recommended);
        Assert.Contains("unanswered", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Makes_free_text_available_alongside_selected_options()
    {
        IReadOnlyList<UserQuestion>? received = null;
        var tool = new AskUserTool();
        ToolContext context = CreateContext(
            (questions, _) =>
            {
                received = questions;
                return Task.FromResult(
                    new UserQuestionResult(
                        UserQuestionStatus.Answered,
                        [new UserQuestionAnswer(["Option 1"], "A custom destination")]
                    )
                );
            }
        );

        ToolResult result = await Execute(tool, context, Arguments(1, 2));

        Assert.True(received![0].AllowFreeText);
        Assert.Contains("Option 1", result.Output, StringComparison.Ordinal);
        Assert.Contains("A custom destination", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Formats_answer_output_with_question_headers_and_selected_labels()
    {
        var tool = new AskUserTool();
        ToolContext context = CreateContext(
            (_, _) =>
                Task.FromResult(
                    new UserQuestionResult(
                        UserQuestionStatus.Answered,
                        [
                            new UserQuestionAnswer(["Option 2"], null),
                            new UserQuestionAnswer(["Option 1", "Option 2"], "By Friday"),
                        ]
                    )
                )
        );

        ToolResult result = await Execute(tool, context, Arguments(2, 2));

        Assert.False(result.IsError);
        Assert.Contains("Question 1: Option 2", result.Output, StringComparison.Ordinal);
        Assert.Contains("Question 2: Option 1, Option 2", result.Output, StringComparison.Ordinal);
        Assert.Contains("By Friday", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(5, 2)]
    [InlineData(1, 1)]
    [InlineData(1, 5)]
    public async Task Rejects_invalid_question_or_option_counts_without_prompting(
        int questionCount,
        int optionCount
    )
    {
        var tool = new AskUserTool();
        bool prompted = false;
        ToolContext context = CreateContext(
            (_, _) =>
            {
                prompted = true;
                return Task.FromResult(new UserQuestionResult(UserQuestionStatus.Skipped, []));
            }
        );

        ToolResult result = await Execute(tool, context, Arguments(questionCount, optionCount));

        Assert.True(result.IsError);
        Assert.False(prompted);
        Assert.Contains("1–4", result.Output, StringComparison.Ordinal);
        Assert.Contains("2–4", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Description_limits_questions_to_decisions_owned_by_the_user()
    {
        var tool = new AskUserTool();

        Assert.Contains("decision", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("permission", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("discover", tool.Description, StringComparison.OrdinalIgnoreCase);
    }

    private static ToolContext CreateContext(
        Func<IReadOnlyList<UserQuestion>, CancellationToken, Task<UserQuestionResult>> handler
    ) =>
        new("/repo", new RecordingAgentEvents())
        {
            AgentState = new AgentToolContextState(null, null, handler),
        };

    private static Task<UserQuestionResult> AnswerAll(IReadOnlyList<UserQuestion> questions) =>
        Task.FromResult(
            new UserQuestionResult(
                UserQuestionStatus.Answered,
                questions
                    .Select(question => new UserQuestionAnswer([question.Options[0].Label], null))
                    .ToArray()
            )
        );

    private static async Task<ToolResult> Execute(
        AskUserTool tool,
        ToolContext context,
        string arguments
    )
    {
        using JsonDocument document = JsonDocument.Parse(arguments);
        return await tool.ExecuteAsync(
            document.RootElement,
            context,
            TestContext.Current.CancellationToken
        );
    }

    private static string Arguments(int questionCount, int optionCount)
    {
        object[] questions = Enumerable
            .Range(1, questionCount)
            .Select(index =>
                (object)
                    new
                    {
                        header = $"Question {index}",
                        question = $"Prompt {index}",
                        options = Enumerable
                            .Range(1, optionCount)
                            .Select(option => new
                            {
                                label = $"Option {option}",
                                description = $"Description {option}",
                            })
                            .ToArray(),
                        multiSelect = index % 2 == 0,
                    }
            )
            .ToArray();

        return JsonSerializer.Serialize(new { questions });
    }
}
