using System.Globalization;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>
    /// The event pump: streamed text into the live tail and scrollback, streamed reasoning into
    /// the live thinking block and its commit on the end, tool results into blocks, usage into
    /// the footer, control events into notices. Child-run (subagent) events are scoped by run id
    /// and suppressed from the transcript — the live area shows a <c>subagent running</c> status
    /// while a child is live, and the child's usage still feeds the footer.
    /// </summary>
    internal void HandleEvent(AgentEvent agentEvent)
    {
        bool child = agentEvent.ParentRunId is not null;
        switch (agentEvent)
        {
            case RunStarted when child:
                _liveChildren++;
                _live.SetSubagentRunning(true);
                // The child runs inside the spawner's call: the active call is the spawner, and
                // its carried result later renders as one indented group.
                if (_activeCalls.Count > 0)
                {
                    _spawningCalls.Add(_activeCalls[^1]);
                }

                break;
            case RunStarted:
                _live.SetNotice(null);
                break;
            case ReasoningStart when !child:
                _thinking = string.Empty;
                _thinkingStartedAt = _options.Scheduler.Now;
                _live.SetThinking(_thinking);
                break;
            case ReasoningMessageContent reasoning when !child:
                _thinking += reasoning.Text;
                _live.SetThinking(_thinking);
                break;
            case ReasoningEnd when !child:
                CommitThinking();
                _thinking = string.Empty;
                _live.SetThinking(null);
                break;
            case TextMessageContent content when !child:
                AppendTail(content.Text);
                break;
            case TextMessageEnd when !child:
                CommitTail();
                break;
            case ToolCallStart start:
                var startKey = (start.RunId, start.CallId);
                _toolNames[startKey] = start.ToolName;
                _toolArgs[startKey] = string.Empty;
                _toolStartedAt[startKey] = _options.Scheduler.Now;
                if (!child && start.ParentToolCallId is null)
                {
                    _activeCalls.Add(startKey);
                    ShowActiveTool();
                }

                break;
            case ToolCallArgs args:
                var argsKey = (args.RunId, args.CallId);
                _toolArgs[argsKey] = args.Args;
                if (_activeCalls.Count > 0 && _activeCalls[^1] == argsKey)
                {
                    ShowActiveTool();
                }

                break;
            case ToolCallResult result when child:
                _toolNames.Remove((result.RunId, result.CallId));
                _toolArgs.Remove((result.RunId, result.CallId));
                _toolStartedAt.Remove((result.RunId, result.CallId));
                break;
            case ToolCallResult result:
                var resultKey = (result.RunId, result.CallId);
                TimeSpan? duration = _toolStartedAt.Remove(resultKey, out DateTimeOffset startedAt)
                    ? _options.Scheduler.Now - startedAt
                    : null;
                string? failedTool = result.IsError
                    ? _toolNames.GetValueOrDefault(resultKey)
                    : null;
                CommitToolBlock(result, duration);
                if (result.ParentToolCallId is null && _activeCalls.Remove(resultKey))
                {
                    ShowActiveTool();
                }

                if (failedTool is not null)
                {
                    ObserveMcpAfterFailedCall(failedTool);
                }

                break;
            case UsageUpdated usage:
                AddUsage(usage.Usage, root: !child);
                break;
            case ModelRouted routed when !child:
                // Only the session's own extension-registered/virtual model shows routing; a
                // catalog model's footer is untouched even when a nested call routes.
                if (_extensionModelId is not null)
                {
                    _modelRoute = routed.ResolvedModel;
                    UpdateStatus();
                }

                break;
            case RunFinished when child:
            case RunError when child:
                _liveChildren = Math.Max(0, _liveChildren - 1);
                if (_liveChildren == 0)
                {
                    _live.SetSubagentRunning(false);
                }

                break;
            case RunFinished finished:
                MapOutcome(finished.StopReason);
                break;
            case RunError error:
                _outcome = RunOutcome.Failed;
                Notify("run failed: " + error.Message, NoticeLevel.Error);
                break;
            case SteeringInjected:
                EchoSteering();
                break;
            case Retrying retrying:
                Notify(
                    FormattableString.Invariant($"retrying (attempt {retrying.Attempt})"),
                    NoticeLevel.Warning
                );
                break;
            case CompactionApplied:
                Notify("context compacted");
                break;
            case StepLimitReached limit:
                Notify(
                    FormattableString.Invariant($"step limit reached ({limit.MaxSteps} steps)"),
                    NoticeLevel.Warning
                );
                break;
        }
    }

    private void AppendTail(string text)
    {
        _tail += text;
        CommitCompletedParagraphs();
        _live.SetTail(_tail);
    }

    private void CommitCompletedParagraphs()
    {
        int separator;
        while ((separator = _tail.IndexOf("\n\n", StringComparison.Ordinal)) >= 0)
        {
            string paragraph = _tail[..separator];
            _tail = _tail[(separator + 2)..];
            CommitParagraph(paragraph);
        }
    }

    private void CommitTail()
    {
        string tail = _tail;
        _tail = string.Empty;
        CommitParagraph(tail);
        _live.SetTail(string.Empty);
    }

    /// <summary>
    /// The finished reasoning span commits through the one scrollback path: the collapsed
    /// <c>✦ thought for &lt;n&gt;s</c> line, with whole seconds on the injected scheduler's clock,
    /// or the full text when the <c>thinkingFull</c> setting is on. The renderer escapes the text
    /// and paints the thinking colour; the console drops it under NO_COLOR.
    /// </summary>
    private void CommitThinking()
    {
        int elapsedSeconds = (int)
            Math.Floor(Math.Max(0, (_options.Scheduler.Now - _thinkingStartedAt).TotalSeconds));
        var commit = new ThinkingCommitModel(_thinking, elapsedSeconds, _settings.ThinkingFull);
        _transcript.Add(new TranscriptThinkingEntry(commit));
        _turnWroteOutput = true;
        _live.WriteScrollback(console =>
            console.Write(
                _thinkingCommit.Render(commit, _console.Size.Columns, _capabilities.Color)
            )
        );
    }

    private void CommitParagraph(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        _transcript.Add(new TranscriptAssistantEntry(markdown));
        _turnWroteOutput = true;
        _live.WriteScrollback(console => console.Write(_markdown.Render(markdown)));
    }

    private void CommitToolBlock(ToolCallResult result, TimeSpan? duration)
    {
        if (result.ParentToolCallId is not null)
        {
            return;
        }

        var key = (result.RunId, result.CallId);
        string name = _toolNames.Remove(key, out string? toolName) ? toolName : "tool";
        string args = _toolArgs.Remove(key, out string? toolArgs) ? toolArgs : string.Empty;
        var block = new ToolBlockModel(
            name,
            args,
            result.IsError ? ToolBlockStatus.Error : ToolBlockStatus.Ok,
            result.Output,
            DiffFor(result.Details),
            result.Details is BashDetails bash ? bash.ExitCode : null,
            duration,
            ChildResult: _spawningCalls.Remove(key)
        );
        _transcript.Add(new TranscriptToolEntry(block));
        _turnWroteOutput = true;
        _live.WriteScrollback(console =>
            console.Write(_toolBlocks.Render(block, _console.Size.Columns))
        );
        RenderExtensionBlock(name, result);
    }

    private static ToolDiffInfo? DiffFor(object? details) =>
        details switch
        {
            EditDetails edit => new ToolDiffInfo(edit.Path, edit.MatchTier, edit.Diff),
            WriteDetails write => new ToolDiffInfo(
                write.Path,
                write.Created ? "created" : "replaced",
                write.Diff
            ),
            _ => null,
        };

    /// <summary>Queries the renderer registry by tool name and shows a matching contribution as a
    /// styled block above the live area; unmatched or failing renderers render nothing extra.</summary>
    private void RenderExtensionBlock(string toolName, ToolCallResult result)
    {
        if (_toolResultRenderers is not { } renderers)
        {
            return;
        }

        var view = new ToolResultView(toolName, result.CallId, result.Output, result.IsError);
        if (
            renderers.TryRender(toolName, view, out IReadOnlyList<TextSegment> segments)
            && segments.Count > 0
        )
        {
            _interaction.RenderExtension(new ExtensionBlock(toolName, segments));
        }
    }

    /// <summary>
    /// A failed call to an MCP server's tool is an observation point: the server is liveness-probed
    /// in the background (the call already produced its error result); an exited process flips to
    /// failed and fires its one notice. Failures of unrelated tools never probe.
    /// </summary>
    private void ObserveMcpAfterFailedCall(string toolName)
    {
        bool configured = IsToolOfServer(toolName, _mcp?.Statuses);
        bool extension = IsToolOfServer(toolName, _extensions?.McpServers?.Statuses);
        if (configured || extension)
        {
            _ = ObserveMcpAsync(configured, extension, _lifetime.Token);
        }
    }

    private static bool IsToolOfServer(string toolName, IReadOnlyList<McpServerStatus>? statuses) =>
        statuses is not null
        && statuses.Any(status =>
            toolName.StartsWith(status.Name + "__", StringComparison.Ordinal)
        );

    private async Task ObserveMcpAsync(bool configured, bool extension, CancellationToken ct)
    {
        try
        {
            if (configured && _mcp is { } set)
            {
                await set.ObserveStatusesAsync(ct).ConfigureAwait(false);
            }

            if (extension && _extensions?.McpServers is { } servers)
            {
                await servers.ObserveStatusesAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            // Liveness observation is best-effort and must never take the session down.
        }
    }

    /// <summary>
    /// Rolls usage into the footer. Only the root run feeds the activity line's deltas: a child
    /// (subagent) run is part of the turn's cumulative usage but not of the root run's own delta.
    /// </summary>
    private void AddUsage(UsageDetails usage, bool root)
    {
        _inputTokens += usage.InputTokenCount ?? 0;
        _outputTokens += usage.OutputTokenCount ?? 0;
        if (root)
        {
            _live.SetRunUsage(_inputTokens - _runInputBaseline, _outputTokens - _runOutputBaseline);
        }

        UpdateStatus();
    }

    /// <summary>
    /// Shows the top active call in the activity line: its name and its argument summary, both
    /// read from the session's own call dictionaries. No active call clears the line.
    /// </summary>
    private void ShowActiveTool()
    {
        if (_activeCalls.Count == 0)
        {
            _live.SetTool(null);
            return;
        }

        (string RunId, string CallId) key = _activeCalls[^1];
        string name = _toolNames.GetValueOrDefault(key, "tool");
        _live.SetTool(name);
        _live.SetToolSummary(
            ToolArgsSummary.Summarize(
                name,
                _toolArgs.GetValueOrDefault(key),
                unicode: _capabilities.Unicode
            )
        );
    }
}
