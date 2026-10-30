using System.Globalization;
using Lunate.Agent;
using Lunate.Tui;
using Spectre.Console;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    /// <summary>Keys <c>/settings &lt;key&gt; &lt;value&gt;</c> may change in a running session.</summary>
    private static readonly string[] EditableKeys =
    [
        "approval",
        "thinkingFull",
        "toolDetails",
        "toolResultLimit",
    ];

    /// <summary>
    /// The <c>/settings</c> block: every resolved setting aligned with its settings-file key, the
    /// launch-time ones included, the file path at the head, and the keys this session accepts as
    /// footer. Read-only and free of model calls; goes into scrollback like <c>/session</c>.
    /// </summary>
    private void ShowSettings()
    {
        var lines = new List<string> { "settings: " + _options.SettingsPath };
        lines.Add(Row("model", _settings.Model ?? "(none)"));
        lines.Add(Row("approval", ApprovalLiteral(_settings.Approval)));
        lines.Add(Row("thinking", ThinkingLiteral(_thinkingLevel)));
        lines.Add(Row("thinkingFull", BooleanLiteral(_settings.ThinkingFull)));
        lines.Add(Row("toolDetails", ToolDetailsLiteral(_settings.ToolDetails)));
        lines.Add(Row("toolResultLimit", LimitText(_settings.ToolOutputLimit)));
        lines.Add(Row("startupHeader", BooleanLiteral(_settings.StartupHeader)));
        lines.Add(Row("updateCheck", BooleanLiteral(_settings.UpdateCheck)));
        lines.Add(Row("favorites", ListValue(_settings.Favorites)));
        lines.Add(Row("tools.extra", ListValue(_settings.Tools.Extra)));
        lines.Add(Row("providers", ListCount("endpoint", _settings.Providers)));
        lines.Add(Row("roslyn", RoslynText()));
        lines.Add(Row("subagents", SubagentsText(_settings.Subagents)));
        lines.Add(
            "editable here: "
                + string.Join(", ", EditableKeys)
                + " — the rest have their own command or are launch-time settings"
        );
        WriteScrollbackLines([.. lines.Select(Markup.Escape)]);
    }

    /// <summary>The <c>/settings &lt;key&gt;</c> read: the key's current value on one line.</summary>
    private void ShowOneSetting(string key)
    {
        string value = key switch
        {
            "model" => _settings.Model ?? "(none)",
            "approval" => ApprovalLiteral(_settings.Approval),
            "thinking" => ThinkingLiteral(_thinkingLevel),
            "thinkingFull" => BooleanLiteral(_settings.ThinkingFull),
            "toolDetails" => ToolDetailsLiteral(_settings.ToolDetails),
            "toolResultLimit" => LimitText(_settings.ToolOutputLimit),
            "startupHeader" => BooleanLiteral(_settings.StartupHeader),
            "updateCheck" => BooleanLiteral(_settings.UpdateCheck),
            "favorites" => ListValue(_settings.Favorites),
            "tools.extra" => ListValue(_settings.Tools.Extra),
            "providers" => ListCount("endpoint", _settings.Providers),
            "roslyn" => RoslynText(),
            "subagents" => SubagentsText(_settings.Subagents),
            _ => "(unknown key)",
        };
        Notify(key + ": " + value);
    }

    /// <summary>
    /// The <c>/settings &lt;key&gt; &lt;value&gt;</c> edit: the four keys a running session owns
    /// take effect immediately and last for this session — the settings file stays as it is; a key
    /// with its own command is refused with that command; a launch-time key is refused with the
    /// file. A bad value refuses and names the accepted literals.
    /// </summary>
    private void SetSetting(string argument)
    {
        string key = argument.Split(' ')[0];
        if (!KnownKey(key))
        {
            Notify($"settings: no such key '{key}'", NoticeLevel.Warning);
            return;
        }

        string value = argument[Math.Min(key.Length, argument.Length)..].Trim();
        if (value.Length == 0)
        {
            ShowOneSetting(key);
            return;
        }

        if (RefusedWhileBusy())
        {
            return;
        }

        switch (key)
        {
            case "approval":
                if (SettingsStore.TryParseApproval(value, out ApprovalPolicy policy))
                {
                    _settings = _settings with { Approval = policy };
                    _approver = new SessionApprover(this, policy);
                    Notify($"approval: {ApprovalLiteral(policy)} (this session)");
                    return;
                }

                Notify("approval must be 'ask' or 'auto-edit'", NoticeLevel.Warning);
                return;
            case "thinkingFull":
                if (value is "on" or "off")
                {
                    bool full = value == "on";
                    _settings = _settings with { ThinkingFull = full };
                    Notify($"thinkingFull: {BooleanLiteral(full)} (this session)");
                    return;
                }

                Notify("thinkingFull must be 'on' or 'off'", NoticeLevel.Warning);
                return;
            case "toolDetails":
                if (SettingsStore.TryParseToolDetails(value, out ToolDetails details))
                {
                    _settings = _settings with { ToolDetails = details };
                    Notify($"toolDetails: {ToolDetailsLiteral(details)} (this session)");
                    return;
                }

                Notify("toolDetails must be 'collapsed' or 'expanded'", NoticeLevel.Warning);
                return;
            case "toolResultLimit":
                if (
                    int.TryParse(
                        value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int limit
                    )
                    && limit > 0
                )
                {
                    _settings = _settings with { ToolOutputLimit = limit };
                    _harness = BuildHarness(_model);
                    Notify($"toolResultLimit: {LimitText(limit)} (this session)");
                    return;
                }

                Notify("toolResultLimit must be a positive integer", NoticeLevel.Warning);
                return;
            case "model":
                Notify("the model has its own command — /model", NoticeLevel.Warning);
                return;
            case "thinking":
                Notify(
                    "the thinking level has its own command — /thinking <level>",
                    NoticeLevel.Warning
                );
                return;
            case "favorites":
                Notify(
                    "favorites has its own commands — /favorite, /unfavorite",
                    NoticeLevel.Warning
                );
                return;
            default:
                Notify(
                    $"{key} is a launch-time setting — set it in {_options.SettingsPath}, "
                        + "its LUNATE_* variable or a launch flag, then start a session",
                    NoticeLevel.Warning
                );
                return;
        }
    }

    /// <summary>Whether <paramref name="key"/> is a settings-file key this table admits.</summary>
    private static bool KnownKey(string key) =>
        key
            is "model"
                or "approval"
                or "thinking"
                or "thinkingFull"
                or "toolDetails"
                or "toolResultLimit"
                or "startupHeader"
                or "updateCheck"
                or "favorites"
                or "tools.extra"
                or "providers"
                or "roslyn"
                or "subagents";

    private static string Row(string key, string value) => key.PadRight(16) + value;

    private static string ApprovalLiteral(ApprovalPolicy policy) =>
        policy == ApprovalPolicy.AutoEdit ? "auto-edit" : "ask";

    private static string ToolDetailsLiteral(ToolDetails details) =>
        details == ToolDetails.Expanded ? "expanded" : "collapsed";

    private static string BooleanLiteral(bool value) => value ? "on" : "off";

    private static string LimitText(int limit) =>
        string.Create(CultureInfo.InvariantCulture, $"{limit:N0} characters");

    private static string ListValue(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values);

    private static string ListCount(string noun, IReadOnlyList<CustomProvider> values) =>
        values.Count == 0
            ? "none"
            : string.Create(CultureInfo.InvariantCulture, $"{values.Count:N0} {noun}(s)");

    private string RoslynText() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"max {_settings.Roslyn.MaxLoadedWorkspaces:N0} loaded Roslyn workspaces"
        );

    private static string SubagentsText(SubagentLimits limits) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{CountText(limits.Concurrency)} concurrent, "
                + $"{CellText(limits.TokenBudget)} tokens a call, "
                + $"{CellText(limits.TreeTokenBudget)} tokens a tree"
        );

    private static string CountText(int? value) =>
        value is { } count ? count.ToString("N0", CultureInfo.InvariantCulture) : "unlimited";

    private static string CellText(long? value) =>
        value is { } budget ? budget.ToString("N0", CultureInfo.InvariantCulture) : "unlimited";
}
