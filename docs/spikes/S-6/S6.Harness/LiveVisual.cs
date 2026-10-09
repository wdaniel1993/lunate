using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace S6.Harness;

/// <summary>
/// The XenoAtom.Terminal.UI live area: a retained visual tree whose root also
/// observes key/text input for the approval prompt and the app-level Ctrl+C /
/// Esc rules. The framework coalesces invalidations and repaints the inline
/// region as part of its host loop.
/// </summary>
public sealed class LiveVisual : ContentVisual
{
    private readonly LiveModel _model;
    private readonly Func<DateTimeOffset> _clock;
    private readonly PromptEditor _editor;

    private readonly State<string?> _tail = new(string.Empty);
    private readonly State<string?> _steering = new(string.Empty);
    private readonly State<string?> _spinner = new(string.Empty);
    private readonly State<string?> _footer = new(string.Empty);
    private readonly State<string?> _status = new(string.Empty);
    private readonly State<string?> _approval = new(string.Empty);

    public LiveVisual(LiveModel model, Func<DateTimeOffset> clock)
    {
        _model = model;
        _clock = clock;

        PromptEditor? editor = null;
        editor = new PromptEditor()
            .PromptMarkup("> ")
            .LineMode(PromptEditorLineMode.SingleLine)
            .AutoFocus(true)
            .Accepted(
                (_, e) =>
                {
                    _model.SubmitInput(e.Text ?? string.Empty);
                    editor!.Text = string.Empty;
                    Refresh();
                }
            )
            .Canceled(
                (_, _) =>
                {
                    _model.Escape();
                    Refresh();
                }
            );

        _editor = editor;

        // Lunate's Ctrl+C contract (clear input; twice quits) needs the raw key.
        // TextEditorBase registers a Ctrl+C copy command that consumes the
        // gesture before it can route as KeyDown, so the spike removes it.
        for (var i = editor.Commands.Count - 1; i >= 0; i--)
        {
            if (editor.Commands[i].Id is "TextEditor.Copy")
            {
                editor.Commands.RemoveAt(i);
            }
        }

        Content = new VStack(
            new TextBlock(_tail).Wrap(true).MaxHeight(6),
            new TextBlock(_steering),
            new HStack(new TextBlock(_spinner), new TextBlock(_footer)).Spacing(2),
            new TextBlock(_status),
            new TextBlock(_approval),
            _editor
        );

        AddHandler(Visual.KeyDownEvent, OnKeyDownObserved, handledEventsToo: true);
        AddHandler(Visual.TextInputEvent, OnTextInputObserved, handledEventsToo: true);
    }

    public PromptEditor Editor => _editor;

    /// <summary>Every key routed to the shell (diagnostics for the spike).</summary>
    public List<string> KeyLog { get; } = [];

    /// <summary>Copies model state into the bound controls.</summary>
    public void Refresh()
    {
        _tail.Value = _model.TailDisplay;
        _steering.Value = _model.SteeringDisplay;
        _spinner.Value = _model.SpinnerGlyph ?? string.Empty;
        _footer.Value = _model.Footer;
        _status.Value = _model.Status ?? string.Empty;
        _approval.Value = _model.ApprovalPrompt ?? string.Empty;
    }

    private void OnKeyDownObserved(object? sender, KeyEventArgs e)
    {
        KeyLog.Add($"key={e.Key} char={(int)(e.Char ?? '\0')} mods={e.Modifiers} handled={e.Handled}");

        if (IsCtrlC(e))
        {
            _model.CtrlC(_clock().TimeOfDay);
            _editor.Text = string.Empty;
            Refresh();
            e.Handled = true;
            return;
        }

        if (e.Key == TerminalKey.Escape)
        {
            _model.Escape();
            _editor.Text = string.Empty;
            Refresh();
            e.Handled = true;
        }
    }

    private void OnTextInputObserved(object? sender, TextInputEventArgs e)
    {
        if (_model.PendingApprovalText is null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var consumed = false;
        foreach (var c in e.Text)
        {
            consumed |= _model.TryApprovalKey(c);
        }

        if (consumed)
        {
            // The focused editor received the text first (bubble routing), so
            // clear it: the approval prompt consumes keystrokes, not steering.
            _editor.Text = string.Empty;
            Refresh();
            e.Handled = true;
        }
    }

    private static bool IsCtrlC(KeyEventArgs e) =>
        e.Char == TerminalChar.CtrlC
        || e.Char == 'c' && e.Modifiers.HasFlag(TerminalModifiers.Ctrl);
}
