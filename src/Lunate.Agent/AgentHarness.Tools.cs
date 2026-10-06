using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    private static readonly JsonSerializerOptions ArgumentsJson = new(
        AIJsonUtilities.DefaultOptions
    )
    {
        WriteIndented = false,
    };

    private async Task ExecuteCallAsync(
        string runId,
        FunctionCallContent call,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        string callId = call.CallId ?? string.Empty;
        string toolName = call.Name ?? string.Empty;
        using Activity? toolActivity = AgentTelemetry.Source.StartActivity(
            $"{AgentTelemetry.ExecuteToolOperation} {toolName}",
            ActivityKind.Internal
        );
        toolActivity?.SetTag(
            AgentTelemetry.OperationNameAttribute,
            AgentTelemetry.ExecuteToolOperation
        );
        toolActivity?.SetTag(AgentTelemetry.ToolNameAttribute, toolName);
        toolActivity?.SetTag(AgentTelemetry.ToolCallIdAttribute, callId);

        channel.Emit(new ToolCallStart(runId, callId, toolName));

        string argsJson = "{}";
        JsonElement args = default;
        string? argumentsError = call.Exception?.Message;
        if (argumentsError is null)
        {
            try
            {
                argsJson = SerializeArguments(call.Arguments);
                using JsonDocument document = JsonDocument.Parse(argsJson);
                args = document.RootElement.Clone();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                argumentsError = exception.Message;
            }
        }

        channel.Emit(new ToolCallArgs(runId, callId, argsJson));
        channel.Emit(new ToolCallEnd(runId, callId));

        ToolResult result = argumentsError is null
            ? await RunToolAsync(toolName, args, channel, ct)
            : new ToolResult(
                $"Invalid JSON arguments for '{toolName}': {argumentsError}. Fix the arguments and retry.",
                IsError: true
            );

        toolActivity?.SetTag(AgentTelemetry.ToolIsErrorAttribute, result.IsError);
        string output = ToolOutput.Truncate(result.Output);
        channel.Emit(new ToolCallResult(runId, callId, output, result.IsError, result.Details));
        _history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, output)]));
    }

    private async Task<ToolResult> RunToolAsync(
        string toolName,
        JsonElement args,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        ITool? tool = _tools.Find(toolName);
        if (tool is null)
        {
            return new ToolResult(
                $"Unknown tool '{toolName}'. Available tools: {AvailableTools()}. Use one of the available tools.",
                IsError: true
            );
        }

        try
        {
            if (_options.Approver is { } approver && !await approver.ApproveAsync(tool, args, ct))
            {
                return new ToolResult(
                    $"Denied: '{toolName}' was not run. Explain why the call is needed and ask before retrying.",
                    IsError: true
                );
            }

            return await tool.ExecuteAsync(
                args,
                new ToolContext(_options.WorkingDirectory, channel),
                ct
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new ToolResult(
                $"Tool '{toolName}' failed: {exception.Message}. Fix the call or try a different approach.",
                IsError: true
            );
        }
    }

    private static string SerializeArguments(IDictionary<string, object?>? arguments) =>
        arguments is null ? "{}" : JsonSerializer.Serialize(arguments, ArgumentsJson);

    private string AvailableTools() =>
        _tools.Tools.Count == 0
            ? "none"
            : string.Join(", ", _tools.Tools.Select(tool => tool.Name));
}
