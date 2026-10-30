using Lunate.Agent;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

public sealed class InteractiveQuestionToolTests
{
    [Fact]
    public async Task Ask_user_is_offered_and_answered_through_the_question_picker()
    {
        using var host = new InteractiveSessionHost();
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
        host.Client.Enqueue(Scripts.Text("I'll use your choice."), Scripts.Stop());
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("Choose a plan.");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);

        Assert.Contains(host.Client.RequestOptions[0]!.Tools!, tool => tool.Name == "ask_user");
        await host.WaitForFrameAsync("Which plan should I use?");
        Assert.Null(host.Session.PendingApproval);
        host.Advance(301);
        host.Console.SendText("2");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);

        FunctionResultContent answer = Assert.Single(
            host.Client.Requests[1]
                .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
        );
        Assert.Contains("Plan: Flexible", answer.Result?.ToString(), StringComparison.Ordinal);

        host.Console.Complete();
        await run;
    }
}
