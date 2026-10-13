using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace SubagentsExtension;

/// <summary>
/// The <c>spawn</c> handler: it validates the model's arguments, reads the run's
/// <see cref="ToolContext.Subagents"/> seam and returns the child's result. Refusals and failures
/// arrive as error results already carrying a teaching message; a child that produced no text gets
/// an explicit note instead of an empty result.
/// </summary>
internal sealed class SpawnToolHandler(SubagentsSettings settings) : IExtensionToolHandler
{
    public async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    )
    {
        if (context.Subagents is not { } runner)
        {
            return new ToolResult(
                "spawn: subagents are unavailable because this call is not running inside an agent run. Use the tool from a normal model turn.",
                IsError: true
            );
        }

        if (
            arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("prompt", out JsonElement promptValue)
            || promptValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(promptValue.GetString())
        )
        {
            return new ToolResult(
                "spawn: 'prompt' is required and must be a non-empty string. Pass the subagent's task and retry.",
                IsError: true
            );
        }

        int? maxSteps = ReadMaxSteps(arguments) ?? settings.DefaultMaxSteps;
        IReadOnlyList<string>? tools = ReadTools(arguments, out string? invalidTool);
        if (invalidTool is not null)
        {
            return new ToolResult(
                $"spawn: the tools allow-list must be an array of strings, but contained '{invalidTool}'. Fix the allow-list and retry.",
                IsError: true
            );
        }

        SubagentResult result = await runner
            .RunAsync(new SubagentRequest(promptValue.GetString()!, maxSteps, tools), ct)
            .ConfigureAwait(false);
        string output =
            result.Output.Length > 0
                ? result.Output
                : $"The subagent finished without text (stop reason: {result.StopReason}).";
        return new ToolResult(output, result.IsError);
    }

    private static int? ReadMaxSteps(JsonElement arguments)
    {
        if (
            arguments.TryGetProperty("maxSteps", out JsonElement steps)
            && steps.ValueKind == JsonValueKind.Number
            && steps.TryGetInt32(out int parsed)
            && parsed >= 1
        )
        {
            return parsed;
        }

        return null;
    }

    private static IReadOnlyList<string>? ReadTools(JsonElement arguments, out string? invalidTool)
    {
        invalidTool = null;
        if (
            !arguments.TryGetProperty("tools", out JsonElement tools)
            || tools.ValueKind == JsonValueKind.Null
        )
        {
            return null;
        }

        if (tools.ValueKind != JsonValueKind.Array)
        {
            invalidTool = tools.GetRawText();
            return null;
        }

        List<string> names = [];
        foreach (JsonElement entry in tools.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                invalidTool = entry.GetRawText();
                return null;
            }

            names.Add(entry.GetString()!);
        }

        return names;
    }
}
