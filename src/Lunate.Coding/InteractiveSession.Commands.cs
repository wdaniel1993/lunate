using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
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
                Notify(compacted ? "context compacted" : "nothing to compact");
                return;
            case "/context":
                Notify(ContextText());
                return;
            case "/mcp":
                Notify(await McpStatusTextAsync(ct));
                return;
            case "/extensions":
                Notify(ExtensionsText());
                return;
            case "/trust":
                HandleTrustCommand(argument);
                return;
            case "/model":
                if (RefusedWhileBusy())
                {
                    return;
                }

                if (argument.Length > 0)
                {
                    await SwitchModel(argument, ct);
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
                Notify("unknown command: " + name);
                return;
        }
    }

    /// <summary>
    /// The <c>/context</c> notice: the context usage (the footer's own format), the resolved
    /// subagent limits (concurrency, per-child and per-tree token budgets, the fixed depth) and
    /// the current run tree's usage. Every number is formatted culture-invariantly.
    /// </summary>
    private string ContextText()
    {
        var usage = new Lunate.Tui.StatusFooterModel(
            DisplayModel(),
            _inputTokens + _outputTokens,
            _contextWindow,
            _workingDirectory,
            _branch
        );
        SubagentLimits limits = _settings.Subagents;
        SubagentUsage tree = _harness?.SubagentUsage ?? default;
        string context = Lunate.Tui.StatusFooterRenderer.PlainText(usage, int.MaxValue);
        string treeTokens = Lunate.Tui.StatusFooterRenderer.FormatTokens(tree.TokensUsed);
        return FormattableString.Invariant(
            $"context: {context}; subagents: concurrency {Limit(limits.Concurrency)}, {Limit(limits.TokenBudget)} tokens per child, {Limit(limits.TreeTokenBudget)} tokens per tree, depth 1 (fixed); tree usage: {treeTokens} tokens, {tree.LiveChildren} running"
        );
    }

    private static string Limit(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited";

    private static string Limit(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited";

    private bool RefusedWhileBusy()
    {
        if (!_turnActive)
        {
            return false;
        }

        Notify("a turn is running — Esc to cancel first", NoticeLevel.Warning);
        return true;
    }

    /// <summary>
    /// The <c>/mcp</c> notice: one segment per configured and extension-registered server from the
    /// live status (<c>name — started, N tools (source)</c> / <c>not started</c> /
    /// <c>failed: reason</c>); a registration shadowed by a configured server reads
    /// <c>shadowed by {source}</c>. Started servers are liveness-probed first, so a server whose
    /// process exited shows as failed. No servers at all reads as such. The notice never prints
    /// environment values or headers. A live-area notice is a single line, so segments join with
    /// semicolons.
    /// </summary>
    private async Task<string> McpStatusTextAsync(CancellationToken ct)
    {
        List<McpServerStatus> statuses = [];
        if (_mcp is { } configured)
        {
            statuses.AddRange(await configured.ObserveStatusesAsync(ct).ConfigureAwait(false));
        }

        if (_extensions?.McpServers is { } extensionServers)
        {
            statuses.AddRange(
                await extensionServers.ObserveStatusesAsync(ct).ConfigureAwait(false)
            );
        }

        IReadOnlyList<ShadowedMcpServer> shadowed = _extensions?.ShadowedMcpServers ?? [];
        if (statuses.Count == 0 && shadowed.Count == 0)
        {
            return "mcp: no servers configured";
        }

        List<string> segments = [.. statuses.Select(FormatMcpStatus)];
        segments.AddRange(
            shadowed.Select(entry =>
                $"{entry.Name} — shadowed by {(entry.ConfiguredSource is { Length: > 0 } source ? source : "the configured server")}"
            )
        );
        return string.Join("; ", segments);
    }

    private static string FormatMcpStatus(McpServerStatus status)
    {
        string source = status.Source is { Length: > 0 } value ? $" ({value})" : string.Empty;
        string missing = status.MissingVariables is { Count: > 0 } variables
            ? $" [missing {string.Join(", ", variables.Select(variable => "${" + variable + "}"))}]"
            : string.Empty;
        return status.State switch
        {
            McpServerState.Started => FormattableString.Invariant(
                $"{status.Name} — started, {status.ToolCount} tools{source}{missing}"
            ),
            McpServerState.Failed =>
                $"{status.Name} — failed: {McpComposition.ShortReason(status.Reason)}{source}{missing}",
            _ => status.Name + " — not started" + source + missing,
        };
    }

    /// <summary>
    /// The <c>/extensions</c> notice: one segment per discovered extension ordered failed →
    /// trust-denied → loaded, then by id (design D2), joined <c>; </c> under the
    /// <c>extensions: </c> prefix; no extensions reads as such and a <c>--no-extensions</c>
    /// session (no host) reads disabled. Read-only, so it is allowed while a turn runs, and it
    /// never prints settings values, secrets or environment values.
    /// </summary>
    private string ExtensionsText()
    {
        if (_options.Extensions?.NoExtensions is true)
        {
            return ExtensionsView.DisabledText;
        }

        if (_extensions is not { } host)
        {
            return ExtensionsView.NoneText;
        }

        IReadOnlyList<string> segments = ExtensionsView.Segments(
            host.Descriptors,
            host.LoadResults,
            host.RegisteredCounts
        );
        return segments.Count == 0
            ? ExtensionsView.NoneText
            : "extensions: " + string.Join("; ", segments);
    }

    /// <summary>Switches to a catalog model by id; an unknown id produces a notice.</summary>
    private async ValueTask SwitchModel(string id, CancellationToken ct)
    {
        if (_catalog.Find(id) is not { } model)
        {
            Notify("unknown model: " + id, NoticeLevel.Warning);
            return;
        }

        await SwitchToModel(model, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the model change in the session, rebuilds the harness against the same session
    /// (the conversation continues), updates the footer and fires <c>ModelChanged</c> with the
    /// new model id — the switch happens outside a run, so the run id is empty (the
    /// <c>ToolsChanged</c> convention).
    /// </summary>
    private async ValueTask SwitchToModel(ModelInfo model, CancellationToken ct)
    {
        _session.AppendModelChange(model.Id);
        _model = model;
        _modelId = model.Id;
        _contextWindow = model.ContextWindow;
        _extensionModelId = null;
        _modelRoute = null;
        _harness = BuildHarness(model);
        UpdateStatus();
        Notify("model: " + model.Id);
        if ((_options.Hooks ?? _extensions?.HookRunner) is { } hooks)
        {
            await hooks
                .RunModelChangedAsync(new ModelChangedPayload(string.Empty, model.Id), ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens the model picker: catalog order, the current model marked with `* `. When the current
    /// model is an extension-registered or virtual model — not in the catalog — the title's
    /// current-model line shows its last routed resolution (<c>auto → deep</c>).
    /// </summary>
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

        string title = _extensionModelId is not null
            ? "Select model — current " + DisplayModel()
            : "Select model";
        OpenPicker(PickerKind.Model, title, labels, values, current);
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
    private async ValueTask HandlePickerKey(KeyEvent key, CancellationToken ct)
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
                await ConfirmPicker(ct).ConfigureAwait(false);
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

    private async ValueTask ConfirmPicker(CancellationToken ct)
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
            await SwitchModel(value, ct).ConfigureAwait(false);
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
            Notify("could not resume: " + exception.Message, NoticeLevel.Error);
            return;
        }

        _session = resumed;
        ResetAlwaysApproved();
        _harness = BuildHarness(_model);
        Notify("resumed " + resumed.SessionId);
        foreach (
            SessionChildSessionEntry link in resumed.Entries.OfType<SessionChildSessionEntry>()
        )
        {
            Notify(
                FormattableString.Invariant(
                    $"child session: {link.ChildSessionId} (run {link.RunId})"
                )
            );
        }
    }

    /// <summary>Starts a fresh session and rebuilds the harness against it.</summary>
    private void StartNewSession()
    {
        _session = HarnessFactory.CreateSession(_sessionDirectory, _workspace);
        ResetAlwaysApproved();
        _harness = BuildHarness(_model);
        Notify("new session");
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
            Notify("commands: " + string.Join(" ", result.Candidates));
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
            Notify("file index: indexing…");
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
            Notify("paths: no matches");
            return;
        }

        IEnumerable<string> shown = result.Candidates.Take(MaxPathCandidates);
        string notice = "paths: " + string.Join(" ", shown);
        int hidden = result.Candidates.Count - Math.Min(result.Candidates.Count, MaxPathCandidates);
        if (hidden > 0)
        {
            notice += $" … (+{hidden} more)";
        }

        Notify(notice);
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
