using System.Globalization;
using System.Text;

namespace S6.Harness;

public enum ApprovalDecision
{
    Yes,
    No,
    Always,
}

/// <summary>
/// Model-independent live-area state machine. Ported from the S-5
/// <c>LiveAreaState</c> so approval yes/no/always, steering, Esc cancel and the
/// Ctrl+C clear/double-quit rules behave identically; the render plumbing is
/// XenoAtom.Terminal.UI's.
/// </summary>
public sealed class LiveModel
{
    public static readonly TimeSpan CtrlCWindow = TimeSpan.FromSeconds(2);

    public const int MaxSteeringLines = 3;

    private readonly StringBuilder _tail = new();

    public string Model { get; private set; } = string.Empty;

    public long InputTokens { get; private set; }

    public long OutputTokens { get; private set; }

    public double ContextPercent { get; private set; }

    public bool Running { get; private set; }

    public bool ToolRunning { get; private set; }

    public string? PendingApprovalTool { get; private set; }

    public string? PendingApprovalText { get; private set; }

    public HashSet<string> AlwaysApproved { get; } = new(StringComparer.Ordinal);

    public bool CancelRequested { get; private set; }

    public bool QuitRequested { get; private set; }

    public TimeSpan? LastCtrlC { get; private set; }

    public List<string> Steering { get; } = [];

    public string? Status { get; private set; }

    public int FrameNumber { get; private set; }

    public string TailText => _tail.ToString();

    public string TailDisplay => string.Join('\n', TailLines());

    public string SteeringDisplay =>
        string.Join('\n', Steering.TakeLast(MaxSteeringLines).Select(s => $"queued: {s}"));

    public string Footer
    {
        get
        {
            var parts = new List<string>();
            if (Model.Length > 0)
            {
                parts.Add(Model);
            }

            if (InputTokens + OutputTokens > 0)
            {
                parts.Add($"{InputTokens + OutputTokens} tok");
            }

            parts.Add(FormattableString.Invariant($"{ContextPercent:0.#}% ctx"));
            return string.Join(" · ", parts);
        }
    }

    public string? ApprovalPrompt =>
        PendingApprovalText is null
            ? null
            : $"approve {PendingApprovalText}? [y]es [n]o [a]lways";

    public string? SpinnerGlyph { get; private set; }

    public void ApplyEvent(AgentEvent value)
    {
        switch (value)
        {
            case AgentEvent.RunStarted started:
                Model = started.Model;
                Running = true;
                Status = null;
                break;
            case AgentEvent.TextDelta delta:
                _tail.Append(delta.Text);
                break;
            case AgentEvent.ApprovalRequested approval:
                if (AlwaysApproved.Contains(approval.Tool))
                {
                    PendingApprovalTool = null;
                    PendingApprovalText = null;
                    Status = $"auto-approved {approval.Tool} (always)";
                }
                else
                {
                    PendingApprovalTool = approval.Tool;
                    PendingApprovalText = $"{approval.Tool}: {approval.Arguments}";
                }

                break;
            case AgentEvent.ToolStarted tool:
                ToolRunning = true;
                PendingApprovalTool = null;
                PendingApprovalText = null;
                Status = $"running {tool.Tool}";
                FrameNumber = 0;
                break;
            case AgentEvent.ToolFinished finished:
                ToolRunning = false;
                SpinnerGlyph = null;
                Status = finished.Ok
                    ? $"{finished.Tool} ok: {finished.Summary}"
                    : $"{finished.Tool} failed: {finished.Summary}";
                break;
            case AgentEvent.Usage usage:
                InputTokens = usage.InputTokens;
                OutputTokens = usage.OutputTokens;
                ContextPercent = usage.ContextPercent;
                break;
            case AgentEvent.RunCompleted:
                Running = false;
                ToolRunning = false;
                SpinnerGlyph = null;
                PendingApprovalText = null;
                Status = "run completed";
                break;
            case AgentEvent.RunCancelled cancelled:
                Running = false;
                ToolRunning = false;
                SpinnerGlyph = null;
                PendingApprovalText = null;
                CancelRequested = true;
                Status = $"run cancelled ({cancelled.Reason})";
                break;
            case AgentEvent.RunFailed failed:
                Running = false;
                ToolRunning = false;
                SpinnerGlyph = null;
                Status = $"run failed: {failed.Message}";
                break;
        }
    }

    public void CtrlC(TimeSpan now)
    {
        if (LastCtrlC is { } last && now - last <= CtrlCWindow)
        {
            QuitRequested = true;
        }
        else
        {
            LastCtrlC = now;
            Status = "input cleared (Ctrl+C again within 2 s quits)";
        }
    }

    public void Escape()
    {
        if (Running || ToolRunning || PendingApprovalText is not null)
        {
            Running = false;
            ToolRunning = false;
            PendingApprovalTool = null;
            PendingApprovalText = null;
            SpinnerGlyph = null;
            CancelRequested = true;
            Status = "cancelling (Esc)";
        }
    }

    public void ApplyApproval(ApprovalDecision decision)
    {
        if (PendingApprovalTool is null)
        {
            return;
        }

        var tool = PendingApprovalTool;
        switch (decision)
        {
            case ApprovalDecision.Yes:
                Status = $"approved {tool}";
                break;
            case ApprovalDecision.No:
                Status = $"denied {tool}";
                break;
            case ApprovalDecision.Always:
                AlwaysApproved.Add(tool);
                Status = $"always approve {tool} this session";
                break;
        }

        ClearApproval();
    }

    /// <summary>True when the key is consumed by the approval prompt.</summary>
    public bool TryApprovalKey(char c)
    {
        if (PendingApprovalText is null)
        {
            return false;
        }

        switch (char.ToLowerInvariant(c))
        {
            case 'y':
                ApplyApproval(ApprovalDecision.Yes);
                return true;
            case 'n':
                ApplyApproval(ApprovalDecision.No);
                return true;
            case 'a':
                ApplyApproval(ApprovalDecision.Always);
                return true;
            default:
                return false;
        }
    }

    public void SubmitInput(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (Running)
        {
            Steering.Add(text);
            Status = $"queued steering: {text}";
        }
        else
        {
            Status = $"input: {text}";
        }
    }

    public void TickSpinner(TimeSpan now, out bool animated)
    {
        animated = ToolRunning || PendingApprovalText is not null;
        if (animated)
        {
            FrameNumber++;
        }

        SpinnerGlyph = ToolRunning
            ? Spinner.Frames[FrameNumber % Spinner.Frames.Length].ToString()
            : null;
    }

    private void ClearApproval()
    {
        PendingApprovalTool = null;
        PendingApprovalText = null;
    }

    private IReadOnlyList<string> TailLines() =>
        [.. _tail.ToString().Split('\n').TakeLast(6)];
}
