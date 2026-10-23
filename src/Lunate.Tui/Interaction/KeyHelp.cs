using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders a <see cref="KeyPattern"/> the way the guide and the help overlay spell it: modifiers
/// first, then the key — <c>Ctrl+R</c>, <c>Alt+Enter</c>, <c>Shift+Tab</c>, <c>Esc</c>, <c>?</c>.
/// The formatter lives beside the catalog so a binding's help text and the router cannot drift.
/// </summary>
internal static class KeyPatternFormatter
{
    public static string Describe(KeyPattern key)
    {
        var text = new StringBuilder();
        if (key.Ctrl == true)
        {
            text.Append("Ctrl+");
        }

        if (key.Alt == true)
        {
            text.Append("Alt+");
        }

        if (key.Shift == true)
        {
            text.Append("Shift+");
        }

        text.Append(Base(key));
        return text.ToString();
    }

    private static string Base(KeyPattern key) =>
        key.Kind switch
        {
            KeyKind.Character => key is { Ctrl: true } or { Alt: true }
                ? (key.Text ?? string.Empty).ToUpperInvariant()
                : key.Text ?? string.Empty,
            KeyKind.Space => "Space",
            KeyKind.Enter => "Enter",
            KeyKind.Tab => "Tab",
            KeyKind.Escape => "Esc",
            KeyKind.Backspace => "Backspace",
            KeyKind.Delete => "Delete",
            KeyKind.Insert => "Insert",
            KeyKind.Home => "Home",
            KeyKind.End => "End",
            KeyKind.PageUp => "PgUp",
            KeyKind.PageDown => "PgDn",
            KeyKind.Up => "Up",
            KeyKind.Down => "Down",
            KeyKind.Left => "Left",
            KeyKind.Right => "Right",
            _ => key.Kind.ToString().ToUpperInvariant(),
        };
}

/// <summary>Which bindings the overlay lists: the ones that apply right now, or every one.</summary>
internal enum KeyHelpScope
{
    Here,
    All,
}

/// <summary>One help row: the key as written, the catalog's description, and its group.</summary>
internal sealed record KeyHelpRow(string Key, string Action, string Group);

/// <summary>
/// The key-help overlay as presented to the user. Data only: the session owns opening, navigation
/// and closing. Rows come from <see cref="KeyBindingCatalog"/> so help cannot describe a binding
/// the router does not have — or miss one it does.
/// </summary>
internal sealed record KeyHelpModel(
    IReadOnlyList<KeyHelpRow> Rows,
    KeyHelpScope Scope,
    string Query,
    int Selected
)
{
    /// <summary>Builds the overlay for the state the prompt is in.</summary>
    public static KeyHelpModel For(KeyContext context, KeyHelpScope scope = KeyHelpScope.Here) =>
        Build(context, scope, query: string.Empty, selected: 0);

    /// <summary>Replaces the filter text; the selection returns to the first surviving row.</summary>
    public KeyHelpModel Filter(string query) => Build(Context, Scope, query, selected: 0);

    /// <summary>Switches between "here" and "all", keeping the filter and resetting the selection.</summary>
    public KeyHelpModel WithScope(KeyHelpScope scope) => Build(Context, scope, Query, selected: 0);

    /// <summary>Moves the selection by <paramref name="delta"/>; clamped, no wrap.</summary>
    public KeyHelpModel Move(int delta)
    {
        if (Rows.Count == 0)
        {
            return this with { Selected = 0 };
        }

        int next = Math.Clamp(Selected + delta, 0, Rows.Count - 1);
        return next == Selected ? this : this with { Selected = next };
    }

    /// <summary>The context the rows were built for; carried so scope and filter keep their base.</summary>
    internal KeyContext Context { get; init; }

    private static KeyHelpModel Build(
        KeyContext context,
        KeyHelpScope scope,
        string query,
        int selected
    )
    {
        List<KeyHelpRow> rows = [];
        foreach (KeyBinding binding in KeyBindingCatalog.All)
        {
            bool here = binding.AppliesTo(context);
            if (scope == KeyHelpScope.Here && !here)
            {
                continue;
            }

            string key = KeyPatternFormatter.Describe(binding.Key);
            if (!Matches(key, binding.Description, query))
            {
                continue;
            }

            rows.Add(new KeyHelpRow(key, binding.Description, Group(binding.Contexts, here)));
        }

        int clamped = rows.Count == 0 ? 0 : Math.Clamp(selected, 0, rows.Count - 1);
        return new KeyHelpModel(rows, scope, query, clamped) { Context = context };
    }

    private static bool Matches(string key, string description, string query) =>
        query.Length == 0
        || key.Contains(query, StringComparison.OrdinalIgnoreCase)
        || description.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string Group(KeyContext contexts, bool here) =>
        contexts switch
        {
            KeyContext.None or KeyContext.Idle => "Editing",
            KeyContext.Running => "Turn running",
            KeyContext.Bash => "Bash mode",
            KeyContext.Approval => "Approvals",
            KeyContext.HasQueue => here ? "Turn running" : "Follow-ups",
            KeyContext.Picker => "Picker",
            KeyContext.Transcript => "Transcript",
            KeyContext.Trust or KeyContext.Secret => "Prompts",
            _ => "Other",
        };
}

/// <summary>
/// Renders a <see cref="KeyHelpModel"/> as a live-area block: the scope title, the filter line when
/// one is set, then the rows grouped by state, windowed so the selection stays visible.
/// </summary>
internal sealed class KeyHelpRenderer
{
    internal const int Window = 14;

    public IRenderable Render(KeyHelpModel model) => new Markup(ComposeMarkup(model));

    /// <summary>The overlay's markup; the tests read it without a console.</summary>
    internal static string ComposeMarkup(KeyHelpModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var text = new StringBuilder();
        string title = model.Scope == KeyHelpScope.All ? "Keys — all" : "Keys — here";
        text.Append("[bold]").Append(Markup.Escape(title)).Append("[/]");
        if (model.Query.Length > 0)
        {
            text.Append("  [dim]filter: ").Append(Markup.Escape(model.Query)).Append("[/]");
        }

        text.Append('\n');
        if (model.Rows.Count == 0)
        {
            text.Append("[dim]no binding matches[/]");
            return text.ToString();
        }

        int first = FirstVisible(model);
        string group = string.Empty;
        for (int index = first; index < Math.Min(first + Window, model.Rows.Count); index++)
        {
            KeyHelpRow row = model.Rows[index];
            if (!string.Equals(row.Group, group, StringComparison.Ordinal))
            {
                group = row.Group;
                if (index > first)
                {
                    text.Append('\n');
                }

                text.Append("[dim]").Append(Markup.Escape(group)).Append("[/]\n");
            }

            string line = $"{row.Key, -14}{row.Action}";
            text.Append(
                    index == model.Selected
                        ? "[bold]> " + Markup.Escape(line) + "[/]"
                        : "  " + Markup.Escape(line)
                )
                .Append('\n');
        }

        if (model.Rows.Count > Window)
        {
            text.Append("[dim]")
                .Append(model.Selected + 1)
                .Append(" of ")
                .Append(model.Rows.Count)
                .Append("   j/k move · / filter · Tab here/all · Esc close[/]");
        }
        else
        {
            text.Append("[dim]/ filter · Tab here/all · Esc close[/]");
        }

        return text.ToString();
    }

    /// <summary>The first row of the window, moved just enough to keep the selection visible.</summary>
    internal static int FirstVisible(KeyHelpModel model)
    {
        if (model.Rows.Count <= Window)
        {
            return 0;
        }

        int first = Math.Max(0, model.Selected - Window + 1);
        return Math.Min(first, model.Rows.Count - Window);
    }
}
