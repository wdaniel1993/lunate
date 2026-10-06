using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spike.Shared;

namespace Spike.MafHarness;

internal static class ApprovalChecks
{
    public static async Task RunAllAsync()
    {
        await AllowOnceAsync();
        await AlwaysToolForSessionAsync();
        await AlwaysToolWithArgumentsForSessionAsync();
        await AutoRulePerArgumentsAsync();
    }

    private static async Task AllowOnceAsync()
    {
        Console.WriteLine("--- approval: allow once, per call, args visible ---");
        ScriptedChatClient client = ApprovalScript("dotnet build", "dotnet test");
        AIAgent agent = HarnessFactory.Create(client, approvalRequiredBash: true);
        AgentSession session = await agent.CreateSessionAsync();

        List<AgentResponseUpdate> first = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "build it",
            session
        );
        ToolApprovalRequestContent request = RequireApproval(first, "first bash call");
        FunctionCallContent call = (FunctionCallContent)request.ToolCall;
        Console.WriteLine($"approval_request_args={JsonSerializer.Serialize(call.Arguments)}");

        List<AgentResponseUpdate> second = await HarnessRunner.CollectUpdatesAsync(
            agent,
            new ChatMessage(
                ChatRole.User,
                [request.CreateResponse(approved: true, reason: "spike allow once")]
            ),
            session
        );
        Console.WriteLine(
            $"allow_once_tool_executed={HarnessRunner.HasFunctionResult(second).ToString().ToLowerInvariant()}"
        );

        List<AgentResponseUpdate> third = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "test it",
            session
        );
        Console.WriteLine(
            $"allow_once_next_call_prompts_again={(HarnessRunner.FindApprovalRequest(third) != null).ToString().ToLowerInvariant()}"
        );
    }

    private static async Task AlwaysToolForSessionAsync()
    {
        Console.WriteLine("--- approval: always for this session, per tool ---");
        ScriptedChatClient client = ApprovalScript("dotnet build", "dotnet test");
        AIAgent agent = HarnessFactory.Create(client, approvalRequiredBash: true);
        AgentSession session = await agent.CreateSessionAsync();

        List<AgentResponseUpdate> first = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "build it",
            session
        );
        ToolApprovalRequestContent request = RequireApproval(first, "first bash call");
        _ = await HarnessRunner.CollectUpdatesAsync(
            agent,
            new ChatMessage(
                ChatRole.User,
                [request.CreateAlwaysApproveToolResponse(reason: "spike always tool")]
            ),
            session
        );

        List<AgentResponseUpdate> third = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "test it",
            session
        );
        Console.WriteLine(
            $"always_tool_second_call_prompts={(HarnessRunner.FindApprovalRequest(third) != null).ToString().ToLowerInvariant()}"
        );
        Console.WriteLine(
            $"always_tool_second_call_executed={HarnessRunner.HasFunctionResult(third).ToString().ToLowerInvariant()}"
        );
    }

    private static async Task AlwaysToolWithArgumentsForSessionAsync()
    {
        Console.WriteLine("--- approval: always for this session, per exact arguments ---");
        ScriptedChatClient client = ApprovalScript("dotnet build", "dotnet build", "dotnet test");
        AIAgent agent = HarnessFactory.Create(client, approvalRequiredBash: true);
        AgentSession session = await agent.CreateSessionAsync();

        List<AgentResponseUpdate> first = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "build it",
            session
        );
        ToolApprovalRequestContent request = RequireApproval(first, "first bash call");
        _ = await HarnessRunner.CollectUpdatesAsync(
            agent,
            new ChatMessage(
                ChatRole.User,
                [
                    request.CreateAlwaysApproveToolWithArgumentsResponse(
                        reason: "spike always tool+args"
                    ),
                ]
            ),
            session
        );

        List<AgentResponseUpdate> sameArgs = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "build it again",
            session
        );
        Console.WriteLine(
            $"always_tool_with_args_same_args_prompts={(HarnessRunner.FindApprovalRequest(sameArgs) != null).ToString().ToLowerInvariant()}"
        );
        Console.WriteLine(
            $"always_tool_with_args_same_args_executed={HarnessRunner.HasFunctionResult(sameArgs).ToString().ToLowerInvariant()}"
        );

        List<AgentResponseUpdate> differentArgs = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "test it",
            session
        );
        Console.WriteLine(
            $"always_tool_with_args_different_args_prompts={(HarnessRunner.FindApprovalRequest(differentArgs) != null).ToString().ToLowerInvariant()}"
        );
    }

    private static async Task AutoRulePerArgumentsAsync()
    {
        Console.WriteLine("--- approval: auto-approval rule inspects per-call arguments ---");
        ToolApprovalAgentOptions approvalOptions = new()
        {
            AutoApprovalRules =
            [
                context =>
                    ValueTask.FromResult(
                        JsonSerializer
                            .Serialize(context.FunctionCallContent.Arguments)
                            .Contains("dotnet", StringComparison.Ordinal)
                    ),
            ],
        };

        ScriptedChatClient client = ApprovalScript("dotnet build", "rm -rf bin");
        AIAgent agent = HarnessFactory.Create(
            client,
            approvalRequiredBash: true,
            toolApprovalOptions: approvalOptions
        );
        AgentSession session = await agent.CreateSessionAsync();

        List<AgentResponseUpdate> safeRun = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "build it",
            session
        );
        Console.WriteLine(
            $"auto_rule_safe_command_prompts={(HarnessRunner.FindApprovalRequest(safeRun) != null).ToString().ToLowerInvariant()}"
        );
        Console.WriteLine(
            $"auto_rule_safe_command_executed={HarnessRunner.HasFunctionResult(safeRun).ToString().ToLowerInvariant()}"
        );

        List<AgentResponseUpdate> destructiveRun = await HarnessRunner.CollectUpdatesAsync(
            agent,
            "clean it",
            session
        );
        ToolApprovalRequestContent request = RequireApproval(
            destructiveRun,
            "destructive bash call"
        );
        _ = await HarnessRunner.CollectUpdatesAsync(
            agent,
            new ChatMessage(
                ChatRole.User,
                [
                    request.CreateResponse(
                        approved: false,
                        reason: "spike denies destructive command"
                    ),
                ]
            ),
            session
        );
        Console.WriteLine("auto_rule_destructive_command_prompts=true");
    }

    private static ScriptedChatClient ApprovalScript(params string[] commands)
    {
        List<ScriptedTurn> turns = [];
        int index = 1;
        foreach (string command in commands)
        {
            string callId = $"call_bash_{index}";
            turns.Add(
                new([
                    Updates.Text($"Running command {index}."),
                    Updates.Call(
                        callId,
                        "bash",
                        JsonSerializer.Serialize(
                            new Dictionary<string, string> { ["command"] = command }
                        )
                    ),
                    Updates.Finish(ChatFinishReason.ToolCalls),
                ])
            );
            turns.Add(new([Updates.Text($"Command {index} done."), Updates.Finish()]));
            index++;
        }

        return new ScriptedChatClient(turns);
    }

    private static ToolApprovalRequestContent RequireApproval(
        IReadOnlyList<AgentResponseUpdate> updates,
        string what
    ) =>
        HarnessRunner.FindApprovalRequest(updates)
        ?? throw new InvalidOperationException($"Expected an approval request for {what}.");
}
