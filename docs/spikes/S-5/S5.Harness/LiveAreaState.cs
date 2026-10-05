using System.Text;

namespace S5.Harness;

/// <summary>
/// Shared, model-independent live-area state machine. Every variant drives the
/// same <see cref="Apply"/> transitions; only the plumbing (channels, Rx,
/// ReactiveUI view models) differs. Kept mutable on purpose: it is the subject
/// of every variant, not the thing under comparison.
/// </summary>
public sealed class LiveAreaState
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

    public string Input { get; private set; } = string.Empty;

    public List<string> Steering { get; } = [];

    public string? Status { get; private set; }

    public int FrameNumber { get; private set; }

    public bool Dirty { get; private set; }

    public string TailText => _tail.ToString();

    /// <summary>Applies one input; returns true when the live area needs a redraw.</summary>
    public bool Apply(LiveInput input)
    {
        switch (input)
        {
            case LiveInput.Event e:
                return ApplyEvent(e.Value);
            case LiveInput.Key k:
                return ApplyKey(k.Value, k.Now);
            case LiveInput.Approval a:
                return ApplyApproval(a.Decision);
            case LiveInput.Resize:
                Dirty = true;
                return true;
            case LiveInput.Frame:
                FrameNumber++;
                if (ToolRunning || PendingApprovalText is not null)
                {
                    return true;
                }

                return ConsumeDirty();
            default:
                return false;
        }
    }

    public LiveAreaView Capture(string? spinnerGlyph)
    {
        var footer = new List<string>();
        if (Model.Length > 0)
        {
            footer.Add(Model);
        }

        if (InputTokens + OutputTokens > 0)
        {
            footer.Add($"{InputTokens + OutputTokens} tok");
        }

        footer.Add(FormattableString.Invariant($"{ContextPercent:0.#}% ctx"));

        return new LiveAreaView
        {
            Tail = TailLines(),
            SpinnerGlyph = spinnerGlyph,
            Footer = string.Join(" · ", footer),
            ApprovalPrompt = PendingApprovalText is null
                ? null
                : $"approve {PendingApprovalText}? [y]es [n]o [a]lways",
            QueuedSteering = [.. Steering.TakeLast(MaxSteeringLines)],
            Input = Input,
            Status = Status,
        };
    }

    private bool ApplyEvent(AgentEvent value)
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
                break;
            case AgentEvent.ToolFinished finished:
                ToolRunning = false;
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
                PendingApprovalText = null;
                Status = "run completed";
                break;
            case AgentEvent.RunCancelled cancelled:
                Running = false;
                ToolRunning = false;
                PendingApprovalText = null;
                CancelRequested = true;
                Status = $"run cancelled ({cancelled.Reason})";
                break;
            case AgentEvent.RunFailed failed:
                Running = false;
                ToolRunning = false;
                Status = $"run failed: {failed.Message}";
                break;
        }

        Dirty = true;
        return true;
    }

    private bool ApplyKey(ConsoleKeyInfo key, TimeSpan now)
    {
        if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            if (LastCtrlC is { } last && now - last <= CtrlCWindow)
            {
                QuitRequested = true;
            }
            else
            {
                Input = string.Empty;
                LastCtrlC = now;
                Status = "input cleared (Ctrl+C again within 2 s quits)";
            }

            Dirty = true;
            return true;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            if (Running || ToolRunning)
            {
                Running = false;
                ToolRunning = false;
                PendingApprovalTool = null;
                PendingApprovalText = null;
                CancelRequested = true;
                Input = string.Empty;
                Status = "cancelling (Esc)";
                Dirty = true;
                return true;
            }

            return false;
        }

        if (PendingApprovalText is not null && PendingApprovalTool is not null)
        {
            switch (char.ToLowerInvariant(key.KeyChar))
            {
                case 'y':
                    return ApplyApproval(ApprovalDecision.Yes);
                case 'n':
                    return ApplyApproval(ApprovalDecision.No);
                case 'a':
                    return ApplyApproval(ApprovalDecision.Always);
            }
        }

        if (key.Key == ConsoleKey.Backspace)
        {
            if (Input.Length > 0)
            {
                Input = Input[..^1];
                Dirty = true;
                return true;
            }

            return false;
        }

        if (key.Key == ConsoleKey.Enter)
        {
            if (Input.Length > 0)
            {
                if (Running)
                {
                    Steering.Add(Input);
                    Status = $"queued steering: {Input}";
                }
                else
                {
                    Status = $"input: {Input}";
                }

                Input = string.Empty;
                Dirty = true;
                return true;
            }

            return false;
        }

        if (char.IsControl(key.KeyChar))
        {
            return false;
        }

        if (Input.Length < 200)
        {
            Input += key.KeyChar;
            Dirty = true;
            return true;
        }

        return false;
    }

    private bool ApplyApproval(ApprovalDecision decision)
    {
        if (PendingApprovalTool is null)
        {
            return false;
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
        return true;
    }

    private void ClearApproval()
    {
        PendingApprovalTool = null;
        PendingApprovalText = null;
        Dirty = true;
    }

    private bool ConsumeDirty()
    {
        var dirty = Dirty;
        Dirty = false;
        return dirty;
    }

    private IReadOnlyList<string> TailLines()
    {
        var lines = _tail.ToString().Split('\n');
        return [.. lines.TakeLast(6)];
    }
}
