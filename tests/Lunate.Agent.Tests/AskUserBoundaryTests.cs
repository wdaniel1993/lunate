using Lunate.Ai;
using Lunate.Contracts;
using static Lunate.Agent.Tests.AgentTestSupport;

namespace Lunate.Agent.Tests;

public sealed class AskUserBoundaryTests
{
    [Fact]
    public async Task Decision_gate_does_not_intercept_a_question_tool()
    {
        var decision = new ScriptedChatClient().Enqueue(
            LoopScripts.Text(
                """{"results":{"approval":{"answer":"deny","probabilities":{"approve":0.1,"deny":0.9}}}}"""
            ),
            LoopScripts.Stop()
        );
        var approver = new RecordingApprover(decision: true);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("question_1", "ask_user", QuestionArguments()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(new AskUserTool()),
            new AgentHarnessOptions
            {
                DecisionMode = DecisionMode.Enforce,
                Approver = approver,
                UsageModels = new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
                {
                    ["chat"] = new(
                        client,
                        "chat-profile",
                        "openai",
                        new ModelInfo("chat-model", "openai", null, 64000, SupportsTools: true)
                    ),
                    ["decision"] = new(
                        decision,
                        "decision-profile",
                        "openai",
                        new ModelInfo("decision-model", "openai", null, 64000, SupportsTools: false)
                    ),
                },
            }
        );

        List<AgentEvent> events = await Run(harness);

        Assert.Empty(decision.Requests);
        Assert.Empty(approver.Requests);
        Assert.Empty(events.OfType<DecisionEvaluated>());
    }

    [Fact]
    public async Task Suggested_question_answer_does_not_suppress_approval_of_the_next_bash_call()
    {
        var askUser = new AskUserTool();
        bool suggestionWasShown = false;
        bool bashExecuted = false;
        var bash = new ScriptedTool(
            "bash",
            "Runs a command.",
            """{"type":"object","properties":{"command":{"type":"string"}}}""",
            ToolRisk.Execute
        )
        {
            OnExecute = (_, _) =>
            {
                bashExecuted = true;
                return new ToolResult("ran", IsError: false);
            },
        };
        var approver = new RecordingApprover(decision: false);
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("call_1", "ask_user", QuestionArguments()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("call_2", "bash", LoopScripts.Args(("command", "echo safe"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Done"), LoopScripts.Stop());
        var harness = new AgentHarness(
            client,
            Registry(askUser, bash),
            new AgentHarnessOptions
            {
                Approver = approver,
                QuestionSuggestionsEnabled = true,
                QuestionDecisionAsync = (_, _) => Task.FromResult(QuestionDecision()),
                AskUserAsync = (questions, _) =>
                {
                    suggestionWasShown =
                        questions[0]
                            .Options.Single(option => option.Label == "Yes")
                            .DecisionSuggestedProbability == 0.91d;
                    return Task.FromResult(
                        new UserQuestionResult(
                            UserQuestionStatus.Answered,
                            [new UserQuestionAnswer(["Yes"], null)]
                        )
                    );
                },
            }
        );

        List<AgentEvent> events = await Run(harness);

        ApprovalRequest request = Assert.Single(approver.Requests);
        Assert.Same(bash, request.Tool);
        Assert.True(suggestionWasShown);
        Assert.False(bashExecuted);
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result =>
                result.CallId == "call_1"
                && result.Output.Contains("Choice: Yes", StringComparison.Ordinal)
        );
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result =>
                result.CallId == "call_2"
                && result.Output.StartsWith("Denied:", StringComparison.Ordinal)
        );
    }

    private static DecisionResponse QuestionDecision() =>
        new(
            new Dictionary<string, DecisionAnswer>(StringComparer.Ordinal)
            {
                ["question-1"] = new(
                    "Yes",
                    0.91d,
                    [new("Yes", 0.91d), new("No", 0.09d)],
                    "decision-model",
                    "native"
                ),
            }
        );

    [Fact]
    public async Task Subagent_question_calls_are_rejected_without_parent_bubbling()
    {
        var askUser = new AskUserTool();
        var spawn = new ScriptedTool(
            "spawn",
            "Spawns a subagent.",
            """{"type":"object","properties":{"prompt":{"type":"string"}}}""",
            ToolRisk.Write
        );
        spawn.OnExecuteAsync = async (_, context, ct) =>
        {
            SubagentResult result = await context.Subagents!.RunAsync(
                new SubagentRequest("child"),
                ct
            );
            return new ToolResult(result.Output, result.IsError);
        };
        var client = new ScriptedChatClient()
            .Enqueue(
                LoopScripts.Call("parent_spawn", "spawn", LoopScripts.Args(("prompt", "child"))),
                LoopScripts.ToolCalls()
            )
            .Enqueue(
                LoopScripts.Call("child_question", "ask_user", QuestionArguments()),
                LoopScripts.ToolCalls()
            )
            .Enqueue(LoopScripts.Text("Child done"), LoopScripts.Stop())
            .Enqueue(LoopScripts.Text("Parent done"), LoopScripts.Stop());
        var harness = new AgentHarness(client, Registry(askUser, spawn));

        List<AgentEvent> events = await Run(harness);

        Assert.DoesNotContain(client.Requests[1].Options!.Tools!, tool => tool.Name == "ask_user");
        Assert.Contains(
            events.OfType<ToolCallResult>(),
            result =>
                result.RunId != events.OfType<RunStarted>().First().RunId
                && result.CallId == "child_question"
                && result.IsError
                && result.Output.Contains("Unknown tool 'ask_user'", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            events.OfType<ToolCallResult>(),
            result => result.Output.Contains("unanswered", StringComparison.Ordinal)
        );
    }

    private static Dictionary<string, object?> QuestionArguments() =>
        LoopScripts.Args(
            (
                "questions",
                new object[]
                {
                    LoopScripts.Args(
                        ("header", "Choice"),
                        ("question", "Proceed?"),
                        (
                            "options",
                            new object[]
                            {
                                LoopScripts.Args(("label", "Yes"), ("description", "Proceed")),
                                LoopScripts.Args(("label", "No"), ("description", "Stop")),
                            }
                        ),
                        ("multiSelect", false)
                    ),
                }
            )
        );
}
