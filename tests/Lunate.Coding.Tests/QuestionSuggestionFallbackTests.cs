using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class QuestionSuggestionFallbackTests
{
    public static TheoryData<string, string?> UnavailableDecisionProfiles =>
        new()
        {
            { """{"schemaVersion":2,"questionSuggestions":true}""", null },
            {
                """{"schemaVersion":2,"questionSuggestions":true,"usages":{"decision":"missing-profile"}}""",
                "decision profile 'missing-profile' is unavailable"
            },
            {
                """{"schemaVersion":2,"questionSuggestions":true,"profiles":{"reviewer":{"provider":"missing-provider","model":"gpt-4o-mini"}},"usages":{"decision":"reviewer"}}""",
                "decision profile 'reviewer' is unavailable"
            },
        };

    [Theory]
    [MemberData(nameof(UnavailableDecisionProfiles))]
    public async Task Missing_or_invalid_decision_profile_keeps_the_question_available(
        string settings,
        string? expectedWarning
    )
    {
        using var host = new InteractiveSessionHost();
        NoticeSlotLog notices = new();
        host.Session.ObserveNotices(notices.Record);
        File.WriteAllText(host.Temp.File("settings.json"), settings);
        host.Client.Enqueue(
            Scripts.Call(
                "question-call",
                "ask_user",
                Scripts.Args(
                    (
                        "questions",
                        new[]
                        {
                            new
                            {
                                header = "Plan",
                                question = "Which plan should I use?",
                                options = new[]
                                {
                                    new { label = "Simple", description = "Fewer moving parts." },
                                    new { label = "Flexible", description = "More room to grow." },
                                },
                                multiSelect = false,
                            },
                        }
                    )
                )
            ),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("I will continue."), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("Choose a plan.");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        await host.WaitForFrameAsync("Which plan should I use?");

        Assert.DoesNotContain("suggested · decision model", host.Frames, StringComparison.Ordinal);
        Assert.Contains(
            host.Events.OfType<DecisionUnavailable>(),
            decision => decision.QuestionId == "question-1"
        );
        if (expectedWarning is not null)
        {
            Assert.Contains(
                notices.Entries,
                notice => notice.Text.Contains(expectedWarning, StringComparison.Ordinal)
            );
        }

        host.Advance(301);
        host.Console.SendEscape();
        await host.Client.WaitForCallAsync(2);
        ToolCallResult questionResult = Assert.Single(
            host.Events.OfType<ToolCallResult>(),
            result => result.CallId == "question-call"
        );
        Assert.False(questionResult.IsError);
        Assert.Contains(
            "unanswered: user skipped",
            questionResult.Output,
            StringComparison.Ordinal
        );

        host.Console.Complete();
        await run;
    }
}
