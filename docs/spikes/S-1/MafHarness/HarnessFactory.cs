using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spike.Shared;

namespace Spike.MafHarness;

internal static class HarnessFactory
{
    public const string SystemPrompt = """
You are Lunate, a coding agent working in the user's repository at /repo on macOS, shell bash.
Tools: read, write, edit, bash.
- Read a file before you edit it. Prefer edit over write for existing files.
- Keep old_text in edit short but unique; include start_line if the text repeats.
- After changing code, build or run the relevant tests and report the result.
- Never touch files outside the repository. Ask before destructive commands.
- Be brief. Show what you changed and why, not every step.
""";

    public static AIAgent Create(
        IChatClient client,
        bool approvalRequiredBash = false,
        bool defaultHarnessInstructions = false,
        ToolApprovalAgentOptions? toolApprovalOptions = null,
        IReadOnlyList<SpikeTool>? toolsOverride = null
    )
    {
        List<AITool> tools = [];
        foreach (SpikeTool tool in toolsOverride ?? SpikeTool.All)
        {
            AIFunction function = new SpikeAIFunction(tool);
            tools.Add(
                approvalRequiredBash && tool.Name == "bash"
                    ? new ApprovalRequiredAIFunction(function)
                    : function
            );
        }

        HarnessAgentOptions options = new()
        {
            Name = "spike-harness",
            DisableCompaction = true,
            DisableTodoProvider = true,
            DisableAgentModeProvider = true,
            DisableFileMemory = true,
            DisableWebSearch = true,
            DisableAgentSkillsProvider = true,
            DisableOpenTelemetry = true,
            ToolApprovalAgentOptions = toolApprovalOptions,
            ChatOptions = new ChatOptions { Instructions = SystemPrompt, Tools = tools },
        };

        if (!defaultHarnessInstructions)
        {
            options.HarnessInstructions = string.Empty;
        }

        return client.AsHarnessAgent(options);
    }
}

internal static class HarnessRunner
{
    public static async Task<List<AgentResponseUpdate>> CollectUpdatesAsync(
        AIAgent agent,
        string input,
        AgentSession? session,
        List<string>? diagnostics = null,
        CancellationToken cancellationToken = default
    ) =>
        await CollectCoreAsync(
            agent.RunStreamingAsync(input, session, cancellationToken: cancellationToken),
            diagnostics
        );

    public static async Task<List<AgentResponseUpdate>> CollectUpdatesAsync(
        AIAgent agent,
        ChatMessage message,
        AgentSession? session,
        List<string>? diagnostics = null,
        CancellationToken cancellationToken = default
    ) =>
        await CollectCoreAsync(
            agent.RunStreamingAsync(message, session, cancellationToken: cancellationToken),
            diagnostics
        );

    public static IReadOnlyList<MappedEvent> MapUpdates(IReadOnlyList<AgentResponseUpdate> updates)
    {
        HarnessEventMapper mapper = new();
        List<MappedEvent> events = [];
        foreach (AgentResponseUpdate update in updates)
        {
            events.AddRange(mapper.Map(update));
        }

        events.AddRange(mapper.Complete());
        return events;
    }

    public static ToolApprovalRequestContent? FindApprovalRequest(
        IReadOnlyList<AgentResponseUpdate> updates
    )
    {
        foreach (AgentResponseUpdate update in updates)
        {
            foreach (AIContent content in update.Contents)
            {
                if (content is ToolApprovalRequestContent request)
                {
                    return request;
                }
            }
        }

        return null;
    }

    public static bool HasFunctionResult(IReadOnlyList<AgentResponseUpdate> updates) =>
        updates.SelectMany(u => u.Contents).OfType<FunctionResultContent>().Any();

    public static void PrintEvents(IReadOnlyList<MappedEvent> events)
    {
        foreach (MappedEvent item in events)
        {
            Console.WriteLine(item.Line);
        }
    }

    private static async Task<List<AgentResponseUpdate>> CollectCoreAsync(
        IAsyncEnumerable<AgentResponseUpdate> updates,
        List<string>? diagnostics
    )
    {
        List<AgentResponseUpdate> collected = [];
        try
        {
            await foreach (AgentResponseUpdate update in updates)
            {
                collected.Add(update);
            }
        }
        catch (OperationCanceledException)
        {
            diagnostics?.Add("run_cancelled_exception=OperationCanceledException");
        }
        catch (Exception ex)
        {
            diagnostics?.Add($"run_exception={ex.GetType().Name}: {ex.Message}");
        }

        return collected;
    }
}
