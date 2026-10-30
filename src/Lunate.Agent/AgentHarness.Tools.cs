using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

public sealed partial class AgentHarness
{
    /// <summary>Marks the synthetic user message that carries a tool result's images.</summary>
    internal const string ToolImageMarker = "lunate:tool-image";

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
        string output = AdmitToolOutput(runId, callId, result, channel);
        channel.Emit(
            new ToolCallResult(runId, callId, output, result.IsError, result.Details)
            {
                HasImages = result.Images is { Count: > 0 },
            }
        );
        AppendHistoryMessage(
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, output)])
        );
        if (result.Images is { Count: > 0 } images)
        {
            AppendHistoryMessage(BuildToolImageMessage(toolName, images));
        }

        _danglingCalls.Remove(call);
    }

    /// <summary>
    /// Masks every model-facing value before it can be emitted or persisted, then admits either the
    /// concise output or its transcript through the same token-and-character threshold.
    /// </summary>
    private string AdmitToolOutput(
        string runId,
        string callId,
        ToolResult result,
        AgentEventChannel channel
    )
    {
        string rawOutput = result.Output ?? string.Empty;
        string output = RedactForContext(rawOutput);
        string? fullOutput = result.FullOutput is { } rawFullOutput
            ? string.Equals(rawOutput, rawFullOutput, StringComparison.Ordinal)
                ? output
                : RedactForContext(rawFullOutput)
            : null;
        if (string.Equals(output, fullOutput, StringComparison.Ordinal))
        {
            fullOutput = null;
        }

        return fullOutput is { } transcript
            ? AdmitTranscript(runId, callId, output, transcript, channel)
            : AdmitOutput(runId, callId, output, channel);
    }

    private string AdmitOutput(
        string runId,
        string callId,
        string output,
        AgentEventChannel channel
    )
    {
        if (_options.Session is not { } session)
        {
            return ToolOutput.Truncate(output, _options.ToolOutputLimit);
        }

        if (
            !ToolOutput.ExceedsSpillLimits(
                output,
                _options.SpillThresholdTokens,
                _options.ToolOutputLimit
            )
        )
        {
            return output;
        }

        return Spill(runId, callId, output, session, channel, includePreview: true);
    }

    private string AdmitTranscript(
        string runId,
        string callId,
        string answer,
        string transcript,
        AgentEventChannel channel
    )
    {
        if (_options.Session is not { } session)
        {
            return JoinAnswerAndTranscript(
                answer,
                ToolOutput.Truncate(transcript, _options.ToolOutputLimit)
            );
        }

        if (
            !ToolOutput.ExceedsSpillLimits(
                transcript,
                _options.SpillThresholdTokens,
                _options.ToolOutputLimit
            )
        )
        {
            return answer;
        }

        return JoinAnswerAndTranscript(
            answer,
            Spill(runId, callId, transcript, session, channel, includePreview: false)
        );
    }

    private string Spill(
        string runId,
        string callId,
        string output,
        Session session,
        AgentEventChannel channel,
        bool includePreview
    )
    {
        int sequence = _spillSequenceByCallId.GetOrAdd(
            callId,
            _ => Interlocked.Increment(ref _spillSequence)
        );
        ToolOutputSpillResult spill = ToolOutputSpill.TryWrite(session, runId, sequence, output);
        if (spill.Error is { } error)
        {
            channel.Emit(new ToolOutputSpillFailed(runId, error));
        }

        return spill.Path is { } path
            ? ToolOutput.RenderPointer(output, path, includePreview)
            : ToolOutput.Truncate(output, _options.ToolOutputLimit, spill.TooLarge);
    }

    private static string JoinAnswerAndTranscript(string answer, string transcript) =>
        answer.Length == 0 ? transcript : answer + "\n\n" + transcript;

    /// <summary>
    /// The one user-role message that carries a tool result's images. The tool-role message holds
    /// only the placeholder text: the OpenAI adapter drops image content there, while both adapters
    /// accept images on a user turn.
    /// </summary>
    private ChatMessage BuildToolImageMessage(
        string toolName,
        IReadOnlyList<ImageAttachment> images
    )
    {
        List<AIContent> contents = [];
        List<string> placeholders = [];
        foreach (ImageAttachment image in images)
        {
            contents.Add(_images.Store(image.Data, image.MimeType, image.Width, image.Height));
            placeholders.Add(
                string.Create(CultureInfo.InvariantCulture, $"[image {image.Width}x{image.Height}]")
            );
        }

        string text = $"Image from {toolName}: {string.Join(", ", placeholders)}.";
        return new ChatMessage(ChatRole.User, [new TextContent(text), .. contents])
        {
            // Compaction counts user turns: this synthetic turn must not consume a kept slot.
            AdditionalProperties = new AdditionalPropertiesDictionary { [ToolImageMarker] = true },
        };
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

        string output = AdmitToolOutput(runId, nestedCallId, result, channel);
        channel.Emit(
            new ToolCallResult(runId, nestedCallId, output, result.IsError, result.Details)
            {
                ParentToolCallId = parentCallId,
                HasImages = result.Images is { Count: > 0 },
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

        return result with
        {
            CallId = nestedCallId,
        };
    }

    /// <summary>The hook, approval and execution shared by top-level and nested calls.</summary>
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
        (JsonElement approvedArgs, ToolResult? blocked) = await ApplyToolCallingAsync(
            tool,
            runId,
            callId,
            args,
            ct
        );
        if (blocked is { } block)
        {
            return block;
        }

        try
        {
            if (
                _options.Approver is { } approver
                && !await approver.ApproveAsync(
                    new ApprovalRequest(
                        tool,
                        approvedArgs,
                        runId,
                        ParentRunId,
                        SpawnDepth,
                        WorkspaceRoot: _options.WorkingDirectory
                    ),
                    ct
                )
            )
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

        ToolResult result;
        try
        {
            result = await tool.ExecuteAsync(
                approvedArgs,
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

        return await ApplyToolResultReadyAsync(tool, runId, callId, approvedArgs, result, ct);
    }

    private async ValueTask<(JsonElement Args, ToolResult? Blocked)> ApplyToolCallingAsync(
        ITool tool,
        string runId,
        string callId,
        JsonElement args,
        CancellationToken ct
    )
    {
        if (_options.Hooks is not { } hooks)
        {
            return (args, null);
        }

        AgentToolCallingResult result = await hooks.ToolCallingAsync(
            new AgentToolCallingContext(runId, callId, tool.Name, args)
            {
                Annotations = tool.Annotations,
                ParentRunId = ParentRunId,
            },
            ct
        );
        return result switch
        {
            AgentToolCallingResult.Block block => (
                args,
                new ToolResult(
                    $"Denied: '{tool.Name}' was blocked: {block.Reason}. Explain why the call is needed and ask before retrying.",
                    IsError: true
                )
            ),
            AgentToolCallingResult.Proceed { Arguments: { } mutated } => (mutated, null),
            _ => (args, null),
        };
    }

    private async ValueTask<ToolResult> ApplyToolResultReadyAsync(
        ITool tool,
        string runId,
        string callId,
        JsonElement approvedArgs,
        ToolResult result,
        CancellationToken ct
    )
    {
        if (_options.Hooks is not { } hooks)
        {
            return result;
        }

        AgentToolResultReadyResult ready = await hooks.ToolResultReadyAsync(
            new AgentToolResultReadyContext(
                runId,
                callId,
                tool.Name,
                approvedArgs,
                result.Output ?? string.Empty,
                result.IsError
            )
            {
                ParentRunId = ParentRunId,
            },
            ct
        );
        return result with
        {
            Output = ready.Output,
            Details = ready.Data ?? result.Details,
            StructuredContent = ready.StructuredContent ?? result.StructuredContent,
        };
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
            ExecuteToolAsync = async (name, args, callCt) =>
            {
                // The caller's token (for example the code-mode bridge's timeout) composes with the
                // run's: either one cancels the nested call.
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, callCt);
                return await RunNestedToolAsync(
                        runId,
                        callId,
                        name,
                        args,
                        channel,
                        nestedDepth,
                        nestedCalls,
                        linked.Token
                    )
                    .ConfigureAwait(false);
            },
            Progress = message => channel.Emit(new ToolProgressUpdate(runId, callId, message)),
            FileMutations = _options.FileMutations,
            Model = _options.Model,
            FileChanges = new RunFileChangeSink(
                _options.FileChanges,
                _options.WorkingDirectory,
                runId,
                _reads
            ),
            Reads = _reads,
            Subagents = _subagentRunner,
            Session = _options.Session,
            ToolStubs = CallableToolStubs(),
            Models = _options.ModelRegistry is { } registry
                ? new RunModelAccess(registry, this, channel, runId)
                : null,
        };

    /// <summary>
    /// The stubs of exactly the tools callable via <see cref="ToolContext.ExecuteToolAsync"/> for
    /// this run, in registration order. The run's own registry means a narrowed child registry
    /// narrows its stubs too.
    /// </summary>
    private IReadOnlyList<ToolStub> CallableToolStubs() =>
        [
            .. _tools
                .Tools.Where(tool =>
                    tool.Exposure
                        is ToolExposure.Direct
                            or ToolExposure.Programmatic
                            or ToolExposure.Deferred
                )
                .Select(ToolStubs.Describe),
        ];

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
