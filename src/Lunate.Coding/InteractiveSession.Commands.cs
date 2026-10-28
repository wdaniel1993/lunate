using System.Globalization;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;
using Spectre.Console;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private enum PickerKind
    {
        Model,
        Session,
        AuthProvider,
        AuthVerify,
    }

    /// <summary>The cap on <c>/compact</c> focus instructions; a longer argument is refused.</summary>
    private const int CompactInstructionsLimit = 1000;

    /// <summary>
    /// Dispatches a submitted slash command through the composed registry. Commands are frontend
    /// control: they never reach the model and skip the input pipeline. A source may act through
    /// its own host callback (built-ins and skills) or return a message (extensions); an error
    /// result is rendered as an error notice. Internal because it is the one dispatch path: the
    /// key pump and the tests that drive a command while a turn is active both come through here.
    /// </summary>
    internal async Task DispatchCommandAsync(string text, CancellationToken ct)
    {
        (string name, string argument) = Commands.Parse(text);
        string bare = name.StartsWith('/') ? name[1..] : name;
        CommandResult result = await _commands
            .InvokeAsync(new CommandInvocation(bare, argument), ct)
            .ConfigureAwait(false);
        if (result.Message is { } message)
        {
            if (result.IsError)
            {
                Notify(message, NoticeLevel.Error);
            }
            else
            {
                Notify(message);
            }
        }
    }

    /// <summary>
    /// The <c>/help</c> listing: the registry's built-in entries grouped (session, model,
    /// extensions, files, quit, design D3) followed by one section per non-built-in kind present
    /// (<c>skills</c>, <c>extensions</c>, <c>templates</c>), printed as finished lines into
    /// scrollback (a live-area notice is one clipped line and cannot hold a listing). The lines are
    /// plain text, so they are markup-escaped on the way in. Read-only, so it is allowed while a
    /// turn runs.
    /// </summary>
    private void ShowHelp() =>
        WriteScrollbackLines([.. HelpView.Lines(_commands.Descriptors).Select(Markup.Escape)]);

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
        List<string> tail = [];
        if (CacheContextSegment() is { } cache)
        {
            tail.Add(cache);
        }

        tail.Add(SkillsContextSegment());
        return FormattableString.Invariant(
            $"context: {context}; subagents: concurrency {Limit(limits.Concurrency)}, {Limit(limits.TokenBudget)} tokens per child, {Limit(limits.TreeTokenBudget)} tokens per tree, depth 1 (fixed); tree usage: {treeTokens} tokens, {tree.LiveChildren} running; {string.Join("; ", tail)}"
        );
    }

    /// <summary>
    /// The <c>/context</c> prompt-cache segment: only the counts a provider reported, formatted like
    /// the footer; null when it reported neither.
    /// </summary>
    private string? CacheContextSegment()
    {
        string? read = _cacheReadTokens is { } cacheRead
            ? "read " + Lunate.Tui.StatusFooterRenderer.FormatTokens(cacheRead)
            : null;
        string? write = _cacheWriteTokens is { } cacheWrite
            ? "write " + Lunate.Tui.StatusFooterRenderer.FormatTokens(cacheWrite)
            : null;
        return (read, write) switch
        {
            (not null, not null) => $"cache: {read}, {write}",
            (not null, null) => "cache: " + read,
            (null, not null) => "cache: " + write,
            _ => null,
        };
    }

    private static string Limit(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited";

    private static string Limit(long? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "unlimited";

    /// <summary>
    /// <c>/name &lt;title&gt;</c> writes the title sidecar beside the session file (design D4); the
    /// bare form reports the stored title or <c>(none)</c>. A blank or over-long title is a dim
    /// notice and changes nothing, and a fileless session has no file to name.
    /// </summary>
    private void SetSessionName(string argument)
    {
        if (_session is not { } session)
        {
            Notify("no session file to name");
            return;
        }

        if (argument.Length == 0)
        {
            Notify("session name: " + (SessionTitle.Read(session.Path) ?? "(none)"));
            return;
        }

        if (SessionTitle.Normalize(argument) is not { } title)
        {
            Notify(
                FormattableString.Invariant(
                    $"session name must be 1-{SessionTitle.MaxLength} characters"
                )
            );
            return;
        }

        try
        {
            SessionTitle.Write(session.Path, title);
            Notify("session name: " + title);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Notify("could not name the session: " + exception.Message, NoticeLevel.Error);
        }
    }

    /// <summary>
    /// The <c>/session</c> block: the file path and id, the recorded message count, the input and
    /// output tokens summed from every recorded usage (so they survive a resume), and the cache
    /// counts when the session recorded any. When every recorded model is known and fully priced
    /// (design D6), a total and one line per model with its four lanes follow; an unpriced, unknown
    /// or zero-total session stays token-only, never a zero cost. A schema 1/2 session with a cache
    /// rate is token-only because its input total hides the unknown cache split. Read-only and no
    /// model call. The block goes into scrollback, one escaped line at a time.
    /// </summary>
    private void ShowSessionReport()
    {
        string path = "(none)";
        string id = "(none)";
        var messages = 0;
        var input = 0;
        var output = 0;
        long cacheRead = 0;
        long cacheWrite = 0;
        CostReport? cost = null;
        if (_session is { } session)
        {
            path = session.Path;
            id = session.SessionId;
            foreach (SessionMessageEntry entry in session.Entries.OfType<SessionMessageEntry>())
            {
                messages++;
                if (entry.Usage is { } usage)
                {
                    input += usage.Input;
                    output += usage.Output;
                    cacheRead += usage.CacheRead ?? 0;
                    cacheWrite += usage.CacheWrite ?? 0;
                }
            }

            cost = UsageCost.SummarizeSession(_catalog, session, _model.Id);
        }

        List<string> lines =
        [
            "session: " + path,
            "id: " + id,
            FormattableString.Invariant($"messages: {messages}"),
            FormattableString.Invariant($"tokens: {input} in / {output} out"),
        ];
        if (cacheRead + cacheWrite > 0)
        {
            lines.Add(FormattableString.Invariant($"cache: {cacheRead} read / {cacheWrite} write"));
        }

        if (cost is { Total: > 0m })
        {
            lines.Add(FormattableString.Invariant($"cost: {CostCalculator.Format(cost.Total)}"));
            foreach (ModelCostLine model in cost.Models)
            {
                lines.Add(
                    FormattableString.Invariant(
                        $"  {model.ModelId}: {model.Lanes.Input} in / {model.Lanes.Output} out / {model.Lanes.CacheRead} cache read / {model.Lanes.CacheWrite} cache write = {CostCalculator.Format(model.Cost)}"
                    )
                );
            }
        }

        WriteScrollbackLines([.. lines.Select(Markup.Escape)]);
    }

    /// <summary>
    /// The bare <c>/thinking</c> notice: the session's current level plus the active model's
    /// mapping (design D7/D3). Read-only, so it is allowed while a turn runs.
    /// </summary>
    private string ThinkingText() =>
        "thinking: "
        + ThinkingLiteral(_thinkingLevel)
        + " — "
        + ReasoningParameters.Describe(_model, _thinkingLevel);

    /// <summary>
    /// The <c>/thinking &lt;level&gt;</c> set form: sets the level, rebuilds the harness so the
    /// next request carries the mapping, updates the footer and notices the level. An unknown
    /// literal notices the allowed values and changes nothing.
    /// </summary>
    private void SetThinkingLevel(string argument)
    {
        if (!SettingsStore.TryParseThinking(argument, out ThinkingLevel level))
        {
            Notify("thinking must be 'off', 'low', 'medium' or 'high'", NoticeLevel.Warning);
            return;
        }

        _thinkingLevel = level;
        _harness = BuildHarness(_model);
        UpdateStatus();
        Notify("thinking: " + ThinkingLiteral(level));
    }

    /// <summary>The level's user-facing lowercase literal; <c>off</c> is the plain form.</summary>
    private static string ThinkingLiteral(ThinkingLevel level) =>
        level switch
        {
            ThinkingLevel.Low => "low",
            ThinkingLevel.Medium => "medium",
            ThinkingLevel.High => "high",
            _ => "off",
        };

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
    /// session (no host) reads disabled. It re-reads the single-file candidates first — a file the
    /// model wrote mid-session is state, not stale discovery — and it writes nothing, prompts
    /// nothing and prints no settings values, secrets or environment values.
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

        List<string> segments =
        [
            .. ExtensionsView.Segments(
                host.Descriptors,
                host.LoadResults,
                id => host.RegisteredCounts(id, _commands.Shadowed),
                hookTimings: host.HookTimings,
                remoteStart: host.RemoteStartDuration,
                singleFile: host.RefreshSingleFileExtensions(),
                ignoredCopies: host.IgnoredHostCopyOf
            ),
        ];
        foreach ((string id, RemoteExtensionStatus status) in host.FailedRemoteStatuses())
        {
            segments.Add(
                $"{id} — remote {status.State.ToString().ToLowerInvariant()}: {status.Reason}"
            );
        }

        return segments.Count == 0
            ? ExtensionsView.NoneText
            : "extensions: " + string.Join("; ", segments);
    }

    /// <summary>
    /// The <c>/extensions restart &lt;id&gt;</c> action: restarts one loaded remote extension (the
    /// manual restart after an exhausted automatic budget). A non-remote id and a failed restart
    /// produce notices; the session continues either way.
    /// </summary>
    private async ValueTask RestartExtension(string id, CancellationToken ct)
    {
        if (id.Length == 0)
        {
            Notify("usage: /extensions restart <id>");
            return;
        }

        if (_extensions is not { } host)
        {
            Notify(ExtensionsText());
            return;
        }

        try
        {
            bool restarted = await host.RestartExtensionAsync(id, ct).ConfigureAwait(false);
            Notify(
                restarted
                    ? $"extension '{id}' restarted"
                    : $"'{id}' is not a loaded remote extension",
                restarted ? NoticeLevel.Info : NoticeLevel.Warning
            );
        }
        catch (Exception exception)
        {
            Notify($"extension '{id}' was not restarted: {exception.Message}", NoticeLevel.Warning);
        }
    }

    /// <summary>
    /// The <c>/extensions reload &lt;id&gt;</c> action: builds the file again and swaps it in. A trust
    /// or build refusal is a warning notice and the old build keeps answering; the success notice
    /// names the build that is now live.
    /// <para>
    /// The work runs on its own task, not on the caller's: the caller is the key pump, and the
    /// reload's first step is a trust question — awaiting it here would leave nobody to read the
    /// answer, which is a session that asks and cannot be answered. One reload at a time, so two
    /// Enter presses cannot race for the same cache entry.
    /// </para>
    /// </summary>
    private ValueTask ReloadExtension(string id, CancellationToken ct)
    {
        if (id.Length == 0)
        {
            Notify("usage: /extensions reload <id>");
            return ValueTask.CompletedTask;
        }

        if (_extensions is not { } host)
        {
            Notify(ExtensionsText());
            return ValueTask.CompletedTask;
        }

        // A reload swaps the extension's build under whatever is running: a turn whose tools and
        // command sources were resolved against the old build must finish first, exactly like
        // /model, /new and every other command that changes the running composition.
        if (RefusedWhileBusy())
        {
            return ValueTask.CompletedTask;
        }

        if (Interlocked.CompareExchange(ref _reloadGate, 1, 0) != 0)
        {
            Notify(
                FormattableString.Invariant(
                    $"a reload is already running — '{id}' was not started"
                ),
                NoticeLevel.Warning
            );
            return ValueTask.CompletedTask;
        }

        // Off the pump — the first step is a trust question the pump must stay free to answer — so
        // the session declares itself busy for the same stretch: Enter then queues a follow-up
        // instead of opening a turn whose tool list the swap would mutate underneath it.
        _turnActive = true;
        _ = ReloadAsync(id, host, ct);
        return ValueTask.CompletedTask;
    }

    private async Task ReloadAsync(string id, ExtensionSession host, CancellationToken ct)
    {
        string? notice = null;
        var level = NoticeLevel.Info;

        try
        {
            SingleFileReloadOutcome outcome = await host.ReloadSingleFileAsync(id, ct)
                .ConfigureAwait(false);
            if (!outcome.Reloaded)
            {
                notice = FormattableString.Invariant(
                    $"extension '{id}' was not reloaded: {outcome.Reason ?? "no reason recorded"}"
                );
                level = NoticeLevel.Warning;
            }
            else
            {
                // The session's tool list is what the next request declares and what a tool call
                // resolves against; the reload registered the new build's tools, so the session's
                // registry is re-pointed at them here — the missing half that made a reloaded or
                // freshly written extension invisible to the model until the next session.
                SyncReloadedTools(id);
                // Same for the command registry: the old build's sources are gone the moment its
                // context unloads, and the new build's are registered, so recomposing is what makes
                // a new or changed slash command dispatchable (and /help and autocomplete agree).
                await ComposeCommandsAsync(ct).ConfigureAwait(false);
                notice = FormattableString.Invariant(
                    $"extension '{id}' reloaded — build {outcome.LoadedBuildKey}"
                );
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The session is quitting: the reload's own report is not news any more.
        }
        catch (Exception exception)
        {
            notice = FormattableString.Invariant(
                $"extension '{id}' was not reloaded: {exception.Message}"
            );
            level = NoticeLevel.Warning;
        }
        finally
        {
            // Idle before the notice: the notice is the session saying the swap is done, so it must
            // not arrive while the session still looks busy and turn the next Enter into a queued
            // follow-up nobody drains.
            _turnActive = false;
            Volatile.Write(ref _reloadGate, 0);
        }

        if (notice is not null)
        {
            Notify(notice, level);
        }
    }

    /// <summary>
    /// Re-points the session's tool registry at the build a reload just loaded: the previous
    /// build's adapters are dropped (their context is unloaded, so keeping them would call into
    /// code that is gone) and the new build's are added, then the harness is rebuilt so the system
    /// prompt names the same set the declarations carry. Names a core or MCP tool already owns stay
    /// with their owner — the composition rule, applied again after the swap.
    /// </summary>
    private void SyncReloadedTools(string id)
    {
        foreach (
            ITool stale in _tools
                .Tools.OfType<ExtensionToolAdapter>()
                .Where(tool => string.Equals(tool.ExtensionId, id, StringComparison.Ordinal))
                .ToList()
        )
        {
            _tools.Remove(stale.Name);
            _inheritedTools.Remove(stale);
        }

        if (_extensions is { } host)
        {
            foreach (
                ITool tool in host.Tools.Where(tool =>
                    tool is ExtensionToolAdapter adapter
                    && string.Equals(adapter.ExtensionId, id, StringComparison.Ordinal)
                )
            )
            {
                if (_tools.Find(tool.Name) is not null)
                {
                    Notify(
                        FormattableString.Invariant(
                            $"extension tool '{tool.Name}' not added: the name is already taken"
                        ),
                        NoticeLevel.Warning
                    );
                    continue;
                }

                _tools.Add(tool);
                _inheritedTools.Add(tool);
            }
        }

        _harness = BuildHarness(_model);
    }

    /// <summary>Switches to a catalog model by id; an unknown id produces a notice.</summary>
    private async ValueTask SwitchModel(string id, CancellationToken ct)
    {
        ModelInfo? model;
        try
        {
            model = _catalog.Find(id);
        }
        catch (InvalidOperationException exception)
        {
            // An ambiguous bare id names its candidates; the switch reports, never guesses.
            Notify(exception.Message, NoticeLevel.Warning);
            return;
        }

        if (model is null)
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
        _session?.AppendModelChange(model.Id);
        _model = model;
        _modelId = model.Id;
        _contextWindow = model.ContextWindow;
        _extensionModelId = null;
        _modelRoute = null;
        _imageResolver = CreateImageResolver(model);
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
    /// Opens the model picker: the settings favourites first in their list order (only ids the
    /// catalog has, deduplicated), then every remaining catalog model in catalog order. The
    /// active model is selected when it is present, otherwise the first row. A catalog with
    /// hundreds of entries is why the picker filters as the person types. When the current model
    /// is an extension-registered or virtual model — not in the catalog — the title's
    /// current-model line shows its last routed resolution (<c>auto → deep</c>).
    /// </summary>
    private void OpenModelPicker()
    {
        if (RefusedWhileBusy())
        {
            return;
        }

        Dictionary<string, ModelInfo> byId = _catalog.Models.ToDictionary(
            model => model.Id,
            StringComparer.Ordinal
        );
        List<string> labels = [];
        List<string> values = [];
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (string favorite in _settings.Favorites)
        {
            if (
                added.Contains(favorite)
                || !byId.TryGetValue(favorite, out ModelInfo? favoriteModel)
            )
            {
                continue;
            }

            added.Add(favorite);
            labels.Add(Label(favoriteModel));
            values.Add(favoriteModel.Id);
        }

        foreach (ModelInfo model in _catalog.Models)
        {
            if (!added.Add(model.Id))
            {
                continue;
            }

            labels.Add(Label(model));
            values.Add(model.Id);
        }

        string title = _extensionModelId is not null
            ? "Select model — current " + DisplayModel()
            : "Select model";
        int current = values.IndexOf(_modelId);
        OpenPicker(PickerKind.Model, title, labels, values, current < 0 ? 0 : current);
    }

    private static string Label(ModelInfo model) => $"{model.Id} ({model.Provider})";

    /// <summary>Opens the session picker: the directory listing, newest first, the current one selected.</summary>
    private void OpenSessionPicker()
    {
        List<string> labels = [];
        List<string> values = [];
        var current = 0;
        foreach (SessionSummary summary in Session.List(_sessionDirectory))
        {
            labels.Add(summary.Title is { } title ? $"{title} — {summary.Id}" : summary.Id);
            values.Add(summary.Path);
            if (
                _session is { } currentSession
                && string.Equals(summary.Id, currentSession.SessionId, StringComparison.Ordinal)
            )
            {
                current = labels.Count - 1;
            }
        }

        OpenPicker(PickerKind.Session, "Select session", labels, values, current);
    }

    private void OpenPicker(
        PickerKind kind,
        string titlePrefix,
        List<string> labels,
        List<string> values,
        int selected
    )
    {
        _pickerKind = kind;
        _pickerTitlePrefix = titlePrefix;
        _pickerLabels = labels;
        _pickerValues = values;
        _pickerFilter = string.Empty;
        ShowPicker(keepValue: null, fallback: selected);
    }

    /// <summary>
    /// Rebuilds the visible rows from the full list and the filter, keeping
    /// <paramref name="keepValue"/> selected when it still matches and falling back to
    /// <paramref name="fallback"/> otherwise; the session picker never filters.
    /// </summary>
    private void ShowPicker(string? keepValue, int fallback)
    {
        List<string> labels = [];
        List<string> values = [];
        for (var index = 0; index < _pickerLabels.Count; index++)
        {
            if (!PickerMatches(_pickerLabels[index]))
            {
                continue;
            }

            labels.Add(PickerLabel(index));
            values.Add(_pickerValues[index]);
        }

        _pickerVisibleValues = values;
        int selected = keepValue is null ? fallback : values.IndexOf(keepValue);
        if (selected < 0)
        {
            selected = fallback;
        }

        selected = Math.Clamp(selected, 0, Math.Max(labels.Count - 1, 0));
        SetPicker(new SelectListModel(PickerTitle(selected, labels.Count), labels, selected));
    }

    private bool PickerMatches(string label) =>
        _pickerKind is not PickerKind.Model
        || _pickerFilter.Length == 0
        || label.Contains(_pickerFilter, StringComparison.OrdinalIgnoreCase);

    /// <summary>The row label; the active model keeps its <c>* </c> marker.</summary>
    private string PickerLabel(int index) =>
        _pickerKind is PickerKind.Model
        && string.Equals(_pickerValues[index], _modelId, StringComparison.Ordinal)
            ? "* " + _pickerLabels[index]
            : _pickerLabels[index];

    /// <summary>
    /// The title. The session picker keeps its plain title; the model picker appends the
    /// invariant position and visible count and, while filtering, the filter text.
    /// </summary>
    private string PickerTitle(int selected, int count)
    {
        if (_pickerKind is not PickerKind.Model)
        {
            return _pickerTitlePrefix;
        }

        string position =
            count == 0 ? "0 of 0" : FormattableString.Invariant($"{selected + 1} of {count}");
        string title = _pickerTitlePrefix + " — " + position;
        return _pickerFilter.Length == 0 ? title : title + " — filter: " + _pickerFilter;
    }

    /// <summary>
    /// Picker keys are intercepted first: Up/Down move clamped, Enter confirms a selected value,
    /// Esc dismisses. In the model picker an unmodified printable Character or Space appends to
    /// the filter, Backspace removes the last character and Esc clears a non-empty filter first.
    /// Every other key (Ctrl+L, Ctrl+P, ...) is inert while the picker is open.
    /// </summary>
    private async ValueTask HandlePickerKey(KeyEvent key, CancellationToken ct)
    {
        bool plain = !key.Ctrl && !key.Alt;
        switch (key.Kind)
        {
            case KeyKind.Up when plain:
                MovePicker(-1);
                break;
            case KeyKind.Down when plain:
                MovePicker(1);
                break;
            case KeyKind.Enter when plain:
                await ConfirmPicker(ct).ConfigureAwait(false);
                break;
            case KeyKind.Escape:
                if (_pickerKind is PickerKind.Model && _pickerFilter.Length > 0)
                {
                    _pickerFilter = string.Empty;
                    ShowPicker(SelectedPickerValue(), fallback: 0);
                    break;
                }

                DismissPicker();
                break;
            case KeyKind.Character when plain && key.Text is { Length: > 0 } text:
                if (_pickerKind is PickerKind.Model)
                {
                    AppendPickerFilter(text);
                }
                else if (text == "j")
                {
                    MovePicker(1);
                }
                else if (text == "k")
                {
                    MovePicker(-1);
                }

                break;
            case KeyKind.Space when plain:
                AppendPickerFilter(key.Text ?? " ");
                break;
            case KeyKind.Backspace when plain:
                BackspacePickerFilter();
                break;
        }
    }

    private void AppendPickerFilter(string text)
    {
        if (_pickerKind is not PickerKind.Model)
        {
            return;
        }

        _pickerFilter += text;
        ShowPicker(SelectedPickerValue(), fallback: 0);
    }

    private void BackspacePickerFilter()
    {
        if (_pickerKind is not PickerKind.Model || _pickerFilter.Length == 0)
        {
            return;
        }

        _pickerFilter = _pickerFilter[..^1];
        ShowPicker(SelectedPickerValue(), fallback: 0);
    }

    private void MovePicker(int delta)
    {
        SelectListModel moved = _picker!.Move(delta);
        SetPicker(moved with { Title = PickerTitle(moved.Selected, moved.Items.Count) });
    }

    private string? SelectedPickerValue() =>
        _picker is { Selected: >= 0 } picker && picker.Selected < _pickerVisibleValues.Count
            ? _pickerVisibleValues[picker.Selected]
            : null;

    private void SetPicker(SelectListModel picker)
    {
        _picker = picker;
        _live.SetPicker(picker);
    }

    private void DismissPicker()
    {
        _picker = null;
        _pickerTitlePrefix = string.Empty;
        _pickerLabels = [];
        _pickerValues = [];
        _pickerVisibleValues = [];
        _pickerFilter = string.Empty;
        _live.SetPicker(null);
    }

    private async ValueTask ConfirmPicker(CancellationToken ct)
    {
        SelectListModel picker = _picker!;
        PickerKind kind = _pickerKind;
        string? value =
            picker.Selected >= 0 && picker.Selected < _pickerVisibleValues.Count
                ? _pickerVisibleValues[picker.Selected]
                : null;
        if (value is null)
        {
            // An empty match has nothing to confirm: Enter is a no-op and the picker stays open.
            return;
        }

        DismissPicker();
        switch (kind)
        {
            case PickerKind.Model:
                await SwitchModel(value, ct).ConfigureAwait(false);
                break;
            case PickerKind.Session:
                ResumeSession(value);
                break;
            case PickerKind.AuthProvider:
                if (value == AuthCustomValue)
                {
                    BeginCustomLogin(ct);
                }
                else if (ProviderPresets.Find(value) is { } preset)
                {
                    await BeginPresetLogin(preset, ct).ConfigureAwait(false);
                }

                break;
            case PickerKind.AuthVerify:
                if (value == "yes")
                {
                    StartVerification(ct);
                }

                break;
        }
    }

    /// <summary>Resumes a session file; the conversation continues on the current model.</summary>
    private void ResumeSession(string path)
    {
        Session resumed;
        try
        {
            resumed = WithRedaction(Session.Load(path));
        }
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Notify("could not resume: " + exception.Message, NoticeLevel.Error);
            return;
        }

        _session = resumed;
        _fileless = false;
        _deferFreshSession = false;
        ResetAlwaysApproved();
        _turnActive = true;
        _run = ResumeCompositionAsync(resumed);
    }

    /// <summary>
    /// The resume half that composes outside the key pump (a changed-skills trust question must
    /// stay answerable); the conversation continues on the current model.
    /// </summary>
    private async Task ResumeCompositionAsync(Session resumed)
    {
        try
        {
            await ComposeSkillsAsync(_lifetime.Token).ConfigureAwait(false);
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
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Notify("could not resume: " + exception.Message, NoticeLevel.Error);
        }
        finally
        {
            _turnActive = false;
            _run = null;
        }
    }

    /// <summary>
    /// Schedules a fresh session and re-discovers skills on its own task: the reload composes off
    /// the key pump so a trust question stays answerable.
    /// </summary>
    private void StartNewSession()
    {
        _session = _fileless ? null : CreateFreshSession();
        _deferFreshSession = false;
        ResetAlwaysApproved();
        _turnActive = true;
        _run = RestartCompositionAsync();
    }

    private async Task RestartCompositionAsync()
    {
        try
        {
            await ComposeSkillsAsync(_lifetime.Token).ConfigureAwait(false);
            _harness = BuildHarness(_model);
            // /new is a session start: the header prints again, with the counts already known.
            PrintWelcomeHeader(HeaderCounts());
            Notify("new session");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            Notify("could not start the new session: " + exception.Message, NoticeLevel.Error);
        }
        finally
        {
            _turnActive = false;
            _run = null;
        }
    }
}
