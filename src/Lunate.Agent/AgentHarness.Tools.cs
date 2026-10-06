using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lunate.Ai;
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

        (string argsJson, JsonElement args, string? argumentsError) = ReadArguments(call);
        channel.Emit(new ToolCallArgs(runId, callId, argsJson));
        channel.Emit(new ToolCallEnd(runId, callId));

        ToolResult result;
        try
        {
            result = argumentsError is null
                ? await RunToolAsync(toolName, args, channel, ct)
                : new ToolResult(
                    $"Invalid JSON arguments for '{toolName}': {argumentsError}. Fix the arguments and retry.",
                    IsError: true
                );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            AppendSyntheticResult(runId, call, channel, cancelled: true);
            throw;
        }

        toolActivity?.SetTag(AgentTelemetry.ToolIsErrorAttribute, result.IsError);
        string output = ToolOutput.Truncate(result.Output ?? string.Empty);
        channel.Emit(new ToolCallResult(runId, callId, output, result.IsError, result.Details));
        _history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, output)]));
        _danglingCalls.Remove(call);
    }

    private void AppendSyntheticResult(
        string runId,
        FunctionCallContent call,
        AgentEventChannel channel,
        bool cancelled
    )
    {
        string callId = call.CallId ?? string.Empty;
        string output = SyntheticOutput(call, cancelled);
        channel.Emit(new ToolCallResult(runId, callId, output, IsError: true));
        _history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, output)]));
        _danglingCalls.Remove(call);
    }

    private void RepairDanglingCalls(string runId, bool cancelled)
    {
        foreach (FunctionCallContent call in _danglingCalls)
        {
            string callId = call.CallId ?? string.Empty;
            _history.Add(
                new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(callId, SyntheticOutput(call, cancelled))]
                )
            );
        }

        _danglingCalls.Clear();
    }

    private static string SyntheticOutput(FunctionCallContent call, bool cancelled)
    {
        string toolName = call.Name ?? string.Empty;
        return cancelled
            ? $"Tool call ({toolName}) was cancelled by the user and was not executed."
            : $"Tool call ({toolName}) was not executed: the run failed. Do not retry it.";
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
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new ToolResult(
                $"Approval for tool '{toolName}' failed: {exception.Message}. The call was not run.",
                IsError: true
            );
        }

        try
        {
            return await tool.ExecuteAsync(
                args,
                new ToolContext(_options.WorkingDirectory, new ExtensionOnlyEventSink(channel)),
                ct
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
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

    private static (string Json, JsonElement Args, string? Error) ReadArguments(
        FunctionCallContent call
    )
    {
        if (StreamAccumulator.IsUnassembled(call))
        {
            string? raw = FragmentOf(call.Arguments);
            string error =
                call.Exception?.Message
                ?? "the streamed arguments were not assembled into a complete call";
            return (raw is { Length: > 0 } ? raw : error, default, error);
        }

        try
        {
            string json = SerializeArguments(call.Arguments);
            using JsonDocument document = JsonDocument.Parse(json);
            return (json, document.RootElement.Clone(), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (exception.Message, default, exception.Message);
        }
    }

    private static string? FragmentOf(IDictionary<string, object?>? arguments)
    {
        if (
            arguments is not { } values
            || !values.TryGetValue(StreamAccumulator.ArgumentsFragmentKey, out object? value)
        )
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
    }

    private static string SerializeArguments(IDictionary<string, object?>? arguments) =>
        arguments is null ? "{}" : JsonSerializer.Serialize(arguments, ArgumentsJson);

    private static List<FunctionCallContent> DistinctCalls(List<ChatResponseUpdate> updates)
    {
        Dictionary<string, FunctionCallContent> byCallId = new(StringComparer.Ordinal);
        List<string> order = [];
        foreach (ChatResponseUpdate update in updates)
        {
            foreach (AIContent content in update.Contents)
            {
                if (content is not FunctionCallContent call)
                {
                    continue;
                }

                string callId =
                    call.CallId
                    ?? "ref:"
                        + RuntimeHelpers.GetHashCode(call).ToString(CultureInfo.InvariantCulture);
                if (byCallId.TryAdd(callId, call))
                {
                    order.Add(callId);
                }
            }
        }

        return [.. order.Select(callId => byCallId[callId])];
    }

    private string AvailableTools() =>
        _tools.Tools.Count == 0
            ? "none"
            : string.Join(", ", _tools.Tools.Select(tool => tool.Name));
}
