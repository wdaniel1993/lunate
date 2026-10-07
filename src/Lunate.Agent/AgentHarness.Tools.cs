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
                ? await RunToolAsync(runId, callId, toolName, args, channel, ct)
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
        AppendHistoryMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, output)])
        );
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
        AppendHistoryMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, output)])
        );
        _danglingCalls.Remove(call);
    }

    private void RepairDanglingCalls(string runId, bool cancelled)
    {
        foreach (FunctionCallContent call in _danglingCalls)
        {
            string callId = call.CallId ?? string.Empty;
            AppendHistoryMessage(
                new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(callId, SyntheticOutput(call, cancelled))]
                )
            );
        }

        _danglingCalls.Clear();
    }

    private static string SyntheticOutput(FunctionCallContent call, bool cancelled) =>
        SyntheticOutput(call.Name ?? string.Empty, cancelled);

    private static string SyntheticOutput(string toolName, bool cancelled) =>
        cancelled
            ? $"Tool call ({toolName}) was cancelled by the user and was not executed."
            : $"Tool call ({toolName}) was not executed: the run failed. Do not retry it.";

    private async Task<ToolResult> RunToolAsync(
        string runId,
        string callId,
        string toolName,
        JsonElement args,
        AgentEventChannel channel,
        CancellationToken ct
    )
    {
        ITool? tool = _tools.Find(toolName);
        if (tool is null)
        {
            return UnknownTool(toolName);
        }

        List<SessionNestedCall>? nestedCalls = _options.Session is null ? null : [];
        ToolResult result = await InvokeToolAsync(
            tool,
            runId,
            callId,
            args,
            channel,
            nestedDepth: 1,
            nestedCalls,
            ct
        );
        if (_options.Session is { } session && nestedCalls is { Count: > 0 })
        {
            session.AppendNestedCalls(callId, nestedCalls);
        }

        return result;
    }

    private async Task<ToolResult> RunNestedToolAsync(
        string runId,
        string parentCallId,
        string toolName,
        JsonElement args,
        AgentEventChannel channel,
        int depth,
        List<SessionNestedCall>? nestedCalls,
        CancellationToken ct
    )
    {
        ITool? tool = _tools.Find(toolName);
        if (tool is null)
        {
            return UnknownTool(toolName);
        }

        if (tool.Exposure is ToolExposure.ModelOnly or ToolExposure.Hidden)
        {
            return new ToolResult(
                $"Tool {toolName} is not callable programmatically (exposure: {tool.Exposure}). Use a direct or programmatic tool.",
                IsError: true
            );
        }

        if (depth > _options.MaxNestedToolDepth)
        {
            return new ToolResult(
                $"Nested tool call depth exceeded ({_options.MaxNestedToolDepth}).",
                IsError: true
            );
        }

        string nestedCallId = string.Create(
            CultureInfo.InvariantCulture,
            $"{parentCallId}/{Interlocked.Increment(ref _nestedCallCounter)}"
        );
        channel.Emit(
            new ToolCallStart(runId, nestedCallId, toolName) { ParentToolCallId = parentCallId }
        );
        channel.Emit(
            new ToolCallArgs(runId, nestedCallId, args.GetRawText())
            {
                ParentToolCallId = parentCallId,
            }
        );
        channel.Emit(new ToolCallEnd(runId, nestedCallId) { ParentToolCallId = parentCallId });

        long started = Stopwatch.GetTimestamp();
        ToolResult result;
        try
        {
            result = await InvokeToolAsync(
                tool,
                runId,
                nestedCallId,
                args,
                channel,
                depth + 1,
                nestedCalls,
                ct
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            channel.Emit(
                new ToolCallResult(
                    runId,
                    nestedCallId,
                    SyntheticOutput(toolName, cancelled: true),
                    IsError: true
                )
                {
                    ParentToolCallId = parentCallId,
                }
            );
            throw;
        }

        channel.Emit(
            new ToolCallResult(
                runId,
                nestedCallId,
                ToolOutput.Truncate(result.Output ?? string.Empty),
                result.IsError,
                result.Details
            )
            {
                ParentToolCallId = parentCallId,
            }
        );
        if (nestedCalls is { } records)
        {
            var record = new SessionNestedCall(
                toolName,
                args.GetRawText(),
                result.IsError ? "error" : "ok",
                (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds
            );
            lock (records)
            {
                records.Add(record);
            }
        }

        return result;
    }

    /// <summary>The approval and execution shared by top-level and nested calls.</summary>
    private async Task<ToolResult> InvokeToolAsync(
        ITool tool,
        string runId,
        string callId,
        JsonElement args,
        AgentEventChannel channel,
        int nestedDepth,
        List<SessionNestedCall>? nestedCalls,
        CancellationToken ct
    )
    {
        try
        {
            if (_options.Approver is { } approver && !await approver.ApproveAsync(tool, args, ct))
            {
                return new ToolResult(
                    $"Denied: '{tool.Name}' was not run. Explain why the call is needed and ask before retrying.",
                    IsError: true
                );
            }
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new ToolResult(
                $"Approval for tool '{tool.Name}' failed: {exception.Message}. The call was not run.",
                IsError: true
            );
        }

        try
        {
            return await tool.ExecuteAsync(
                args,
                BuildToolContext(runId, callId, channel, nestedDepth, nestedCalls, ct),
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
                $"Tool '{tool.Name}' failed: {exception.Message}. Fix the call or try a different approach.",
                IsError: true
            );
        }
    }

    private ToolContext BuildToolContext(
        string runId,
        string callId,
        AgentEventChannel channel,
        int nestedDepth,
        List<SessionNestedCall>? nestedCalls,
        CancellationToken ct
    ) =>
        new(_options.WorkingDirectory, new ExtensionOnlyEventSink(channel))
        {
            RunId = runId,
            CallId = callId,
            ExecuteToolAsync = (name, args, _) =>
                RunNestedToolAsync(
                    runId,
                    callId,
                    name,
                    args,
                    channel,
                    nestedDepth,
                    nestedCalls,
                    ct
                ),
            Progress = message => channel.Emit(new ToolProgressUpdate(runId, callId, message)),
            FileMutations = _options.FileMutations,
        };

    private ToolResult UnknownTool(string toolName) =>
        new(
            $"Unknown tool '{toolName}'. Available tools: {AvailableTools()}. Use one of the available tools.",
            IsError: true
        );

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
