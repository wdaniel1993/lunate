using Lunate.Agent;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private enum PickerKind
    {
        Model,
        Session,
    }

    /// <summary>
    /// Dispatches a submitted slash command. Commands are frontend control: they never reach the
    /// model and skip the input pipeline. While a turn runs, the state-changing commands refuse;
    /// <c>/quit</c> always works.
    /// </summary>
    private async Task DispatchCommandAsync(string text, CancellationToken ct)
    {
        (string name, string argument) = Commands.Parse(text);
        switch (name)
        {
            case "/quit":
                Quit();
                return;
            case "/compact":
                if (RefusedWhileBusy())
                {
                    return;
                }

                bool compacted = await _harness!.CompactNowAsync(ct);
                _live.SetNotice(compacted ? "context compacted" : "nothing to compact");
                return;
            case "/model":
                if (RefusedWhileBusy())
                {
                    return;
                }

                if (argument.Length > 0)
                {
                    SwitchModel(argument);
                }
                else
                {
                    OpenModelPicker();
                }

                return;
            case "/new":
                if (RefusedWhileBusy())
                {
                    return;
                }

                StartNewSession();
                return;
            case "/resume":
                if (RefusedWhileBusy())
                {
                    return;
                }

                OpenSessionPicker();
                return;
            default:
                _live.SetNotice("unknown command: " + name);
                return;
        }
    }

    private bool RefusedWhileBusy()
    {
        if (!_turnActive)
        {
            return false;
        }

        _live.SetNotice("a turn is running — Esc to cancel first");
        return true;
    }

    /// <summary>Switches to a catalog model by id; an unknown id produces a notice.</summary>
    private void SwitchModel(string id)
    {
        if (_catalog.Find(id) is not { } model)
        {
            _live.SetNotice("unknown model: " + id);
            return;
        }

        SwitchToModel(model);
    }

    /// <summary>
    /// Records the model change in the session, rebuilds the harness against the same session
    /// (the conversation continues) and updates the footer.
    /// </summary>
    private void SwitchToModel(ModelInfo model)
    {
        _session.AppendModelChange(model.Id);
        _model = model;
        _modelId = model.Id;
        _contextWindow = model.ContextWindow;
        _harness = BuildHarness(model);
        _live.SetFooter(Footer());
        _live.SetNotice("model: " + model.Id);
    }

    /// <summary>Opens the model picker: catalog order, the current model marked with `* `.</summary>
    private void OpenModelPicker()
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        List<string> labels = [];
        List<string> values = [];
        var current = 0;
        foreach (ModelInfo model in _catalog.Models)
        {
            string label = $"{model.Id} ({model.Provider})";
            if (string.Equals(model.Id, _modelId, StringComparison.Ordinal))
            {
                label = "* " + label;
                current = labels.Count;
            }

            labels.Add(label);
            values.Add(model.Id);
        }

        OpenPicker(PickerKind.Model, "Select model", labels, values, current);
    }

    /// <summary>Opens the session picker: the directory listing, newest first, the current one selected.</summary>
    private void OpenSessionPicker()
    {
        List<string> labels = [];
        List<string> values = [];
        var current = 0;
        foreach (SessionSummary summary in Session.List(_sessionDirectory))
        {
            labels.Add(summary.Id);
            values.Add(summary.Path);
            if (string.Equals(summary.Id, _session.SessionId, StringComparison.Ordinal))
            {
                current = labels.Count - 1;
            }
        }

        OpenPicker(PickerKind.Session, "Select session", labels, values, current);
    }

    private void OpenPicker(
        PickerKind kind,
        string title,
        List<string> labels,
        List<string> values,
        int selected
    )
    {
        _pickerKind = kind;
        _pickerValues = values;
        _picker = new SelectListModel(title, labels, selected);
        _live.SetPicker(_picker);
    }

    /// <summary>Picker keys are intercepted first: Up/Down move clamped, Enter confirms, Esc
    /// dismisses; every other key is ignored.</summary>
    private void HandlePickerKey(KeyEvent key)
    {
        SelectListModel picker = _picker!;
        switch (key.Kind)
        {
            case KeyKind.Up when !key.Ctrl && !key.Shift && !key.Alt:
                SetPicker(picker.Move(-1));
                break;
            case KeyKind.Down when !key.Ctrl && !key.Shift && !key.Alt:
                SetPicker(picker.Move(1));
                break;
            case KeyKind.Enter when !key.Ctrl && !key.Shift && !key.Alt:
                ConfirmPicker();
                break;
            case KeyKind.Escape:
                DismissPicker();
                break;
        }
    }

    private void SetPicker(SelectListModel picker)
    {
        _picker = picker;
        _live.SetPicker(picker);
    }

    private void DismissPicker()
    {
        _picker = null;
        _pickerValues = [];
        _live.SetPicker(null);
    }

    private void ConfirmPicker()
    {
        SelectListModel picker = _picker!;
        PickerKind kind = _pickerKind;
        string? value =
            picker.Selected >= 0 && picker.Selected < _pickerValues.Count
                ? _pickerValues[picker.Selected]
                : null;
        DismissPicker();
        if (value is null)
        {
            return;
        }

        if (kind is PickerKind.Model)
        {
            SwitchModel(value);
        }
        else
        {
            ResumeSession(value);
        }
    }

    /// <summary>Resumes a session file; the conversation continues on the current model.</summary>
    private void ResumeSession(string path)
    {
        Session resumed;
        try
        {
            resumed = Session.Load(path);
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _live.SetNotice("could not resume: " + exception.Message);
            return;
        }

        _session = resumed;
        ResetAlwaysApproved();
        _harness = BuildHarness(_model);
        _live.SetNotice("resumed " + resumed.SessionId);
    }

    /// <summary>Starts a fresh session and rebuilds the harness against it.</summary>
    private void StartNewSession()
    {
        _session = HarnessFactory.CreateSession(_sessionDirectory, _workspace);
        ResetAlwaysApproved();
        _harness = BuildHarness(_model);
        _live.SetNotice("new session");
    }

    /// <summary>
    /// Tab completion: only while idle; a whole-input slash word completes against the built-in
    /// commands (single match replaces, several extend to the longest common prefix, no progress
    /// lists them), otherwise an <c>@token</c> ending at the cursor completes against the lazily
    /// built file index (indexing notice while it builds).
    /// </summary>
    private void ApplyCompletion()
    {
        if (_turnActive)
        {
            return;
        }

        if (!TryCompleteSlashWord())
        {
            CompletePathToken();
        }
    }

    /// <summary>Today's rule: the whole input is one `/word` with the cursor at its end.</summary>
    private bool TryCompleteSlashWord()
    {
        string text = _input.Text;
        if (text.Length == 0 || text[0] != '/' || _input.CursorPosition != text.Length)
        {
            return false;
        }

        if (text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        CompletionResult result = Completion.Complete(text, Commands.BuiltIn);
        if (result.Replacement is { } replacement)
        {
            SetInput(replacement);
            return true;
        }

        if (result.Candidates.Count > 0)
        {
            _live.SetNotice("commands: " + string.Join(" ", result.Candidates));
        }

        return true;
    }

    /// <summary>
    /// The <c>@token</c> rule: scan back from the cursor over non-whitespace (the token must end
    /// at the cursor), complete the text after the <c>@</c> against the index and re-prefix the
    /// replacement; a dim notice lists candidates or says there are none.
    /// </summary>
    private void CompletePathToken()
    {
        string text = _input.Text;
        int cursor = _input.CursorPosition;
        if (!TryPathPrefix(text, cursor, out string prefix, out int tokenStart))
        {
            return;
        }

        FileIndex index = FileIndex();
        if (!index.IsReady)
        {
            index.EnsureStarted();
            _live.SetNotice("file index: indexing…");
            return;
        }

        CompletionResult result = Completion.Complete(prefix, index.Match(prefix, 0));
        if (result.Replacement is { } replacement)
        {
            SetInput(text[..tokenStart] + "@" + replacement + text[cursor..]);
            return;
        }

        if (result.Candidates.Count == 0)
        {
            _live.SetNotice("paths: no matches");
            return;
        }

        IEnumerable<string> shown = result.Candidates.Take(MaxPathCandidates);
        string notice = "paths: " + string.Join(" ", shown);
        int hidden = result.Candidates.Count - Math.Min(result.Candidates.Count, MaxPathCandidates);
        if (hidden > 0)
        {
            notice += $" … (+{hidden} more)";
        }

        _live.SetNotice(notice);
    }

    /// <summary>The lazily created index; the seam falls back to the real workspace walk.</summary>
    private FileIndex FileIndex() =>
        _fileIndex ??= new FileIndex(
            _options.WorkspaceFiles ?? new SystemWorkspaceFiles(_workspace.WorktreeRoot)
        );

    /// <summary>The token ending at <paramref name="cursor"/> when it starts with `@`.</summary>
    private static bool TryPathPrefix(
        string text,
        int cursor,
        out string prefix,
        out int tokenStart
    )
    {
        prefix = string.Empty;
        tokenStart = 0;
        if (cursor < 0 || cursor > text.Length)
        {
            return false;
        }

        if (cursor < text.Length && !char.IsWhiteSpace(text[cursor]))
        {
            return false;
        }

        int start = cursor;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        if (start >= cursor || text[start] != '@')
        {
            return false;
        }

        tokenStart = start;
        prefix = text[(start + 1)..cursor];
        return true;
    }

    private const int MaxPathCandidates = 20;
}
