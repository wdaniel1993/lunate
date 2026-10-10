using System.Globalization;
using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>The event pump: streamed text into the live tail and scrollback, tool results into
    /// blocks, usage into the footer, control events into notices.</summary>
    private void HandleEvent(AgentEvent agentEvent)
    {
        switch (agentEvent)
        {
            case RunStarted:
                _live.SetNotice(null);
                break;
            case TextMessageContent content:
                AppendTail(content.Text);
                break;
            case TextMessageEnd:
                CommitTail();
                break;
            case ToolCallStart start:
                _toolNames[start.CallId] = start.ToolName;
                _toolArgs[start.CallId] = string.Empty;
                if (start.ParentToolCallId is null)
                {
                    _activeCalls.Add(start.CallId);
                    _live.SetTool(start.ToolName);
                }

                break;
            case ToolCallArgs args:
                _toolArgs[args.CallId] = args.Args;
                break;
            case ToolCallResult result:
                CommitToolBlock(result);
                if (result.ParentToolCallId is null && _activeCalls.Remove(result.CallId))
                {
                    _live.SetTool(
                        _activeCalls.Count == 0
                            ? null
                            : _toolNames.GetValueOrDefault(_activeCalls[^1])
                    );
                }

                break;
            case UsageUpdated usage:
                AddUsage(usage.Usage);
                break;
            case SteeringInjected:
                EchoSteering();
                break;
            case Retrying retrying:
                _live.SetNotice(
                    FormattableString.Invariant($"retrying (attempt {retrying.Attempt})")
                );
                break;
            case CompactionApplied:
                _live.SetNotice("context compacted");
                break;
            case StepLimitReached limit:
                _live.SetNotice(
                    FormattableString.Invariant($"step limit reached ({limit.MaxSteps} steps)")
                );
                break;
            case RunFinished finished:
                MapOutcome(finished.StopReason);
                break;
            case RunError error:
                _outcome = RunOutcome.Failed;
                _live.SetNotice("run failed: " + error.Message);
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

    private void CommitParagraph(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        _live.WriteScrollback(console => console.Write(_markdown.Render(markdown)));
    }

    private void CommitToolBlock(ToolCallResult result)
    {
        if (result.ParentToolCallId is not null)
        {
            return;
        }

        string name = _toolNames.Remove(result.CallId, out string? toolName) ? toolName : "tool";
        string args = _toolArgs.Remove(result.CallId, out string? toolArgs)
            ? toolArgs
            : string.Empty;
        var block = new ToolBlockModel(
            name,
            args,
            result.IsError ? ToolBlockStatus.Error : ToolBlockStatus.Ok,
            result.Output,
            DiffFor(result.Details)
        );
        _live.WriteScrollback(console => console.Write(_toolBlocks.Render(block)));
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

    private void AddUsage(UsageDetails usage)
    {
        _inputTokens += usage.InputTokenCount ?? 0;
        _outputTokens += usage.OutputTokenCount ?? 0;
        _live.SetFooter(Footer());
    }
}
