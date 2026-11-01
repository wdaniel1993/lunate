using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// A picker as presented to the user: a title and the labelled items with the selected index.
/// The model is data only; the session owns opening, navigation and confirmation.
/// </summary>
public sealed record SelectListModel(string Title, IReadOnlyList<string> Items, int Selected)
{
    /// <summary>Moves the selection by <paramref name="delta"/>; clamped at both ends, no wrap.</summary>
    public SelectListModel Move(int delta)
    {
        if (Items.Count == 0)
        {
            return this with { Selected = 0 };
        }

        int next = Math.Clamp(Selected + delta, 0, Items.Count - 1);
        return next == Selected ? this : this with { Selected = next };
    }
}

/// <summary>
/// Renders a <see cref="SelectListModel"/> as a live-area block: the title, then one line per
/// visible item, the selected one marked with <c>&gt;</c> and emphasized; at most
/// <see cref="VisibleItems"/> items render, windowed so the selection stays visible.
/// </summary>
public sealed class SelectListRenderer
{
    internal const int VisibleItems = 13;

    public IRenderable Render(SelectListModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        int selected = SelectedInWindow(model);
        var builder = new StringBuilder();
        builder.Append(Markup.Escape(CellText.Sanitize(model.Title))).Append('\n');
        int index = 0;
        foreach (string item in PlainItems(model))
        {
            if (index == selected)
            {
                builder
                    .Append("[bold]> ")
                    .Append(Markup.Escape(CellText.Sanitize(item)))
                    .Append("[/]");
            }
            else
            {
                builder.Append("  ").Append(Markup.Escape(CellText.Sanitize(item)));
            }

            builder.Append('\n');
            index++;
        }

        return new Markup(builder.ToString());
    }

    internal IRenderable Render(SelectListQuestionModel question, int width = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(question);
        var builder = new StringBuilder();
        foreach (string line in QuestionLines(question, width))
        {
            builder.Append(Markup.Escape(line)).Append('\n');
        }

        return new Markup(builder.ToString());
    }

    /// <summary>The question picker's plain lines, including its selection and free-text state.</summary>
    internal static IReadOnlyList<string> QuestionLines(
        SelectListQuestionModel question,
        int width = int.MaxValue
    )
    {
        ArgumentNullException.ThrowIfNull(question);
        int availableWidth = Math.Max(1, width);
        int descriptionIndent = WidthBands.Of(availableWidth) == WidthBand.Compact ? 2 : 4;
        List<string> lines = [];
        AddWrapped(lines, question.Title, string.Empty, availableWidth);
        AddWrapped(lines, question.Description, string.Empty, availableWidth);
        for (int index = 0; index < question.Options.Count; index++)
        {
            SelectListQuestionOption option = question.Options[index];
            string current = question.ActiveOption == index ? "> " : "  ";
            string selected = question.MultiSelect
                ? question.SelectedIndices.Contains(index)
                    ? "[x] "
                    : "[ ] "
                : string.Empty;
            string recommended =
                question.RecommendedLabel == option.Label ? " (recommended)" : string.Empty;
            string suggested = option.DecisionSuggestedProbability is { } probability
                ? $" (suggested · decision model {probability.ToString("0.00", CultureInfo.InvariantCulture)})"
                : string.Empty;
            AddWrapped(
                lines,
                option.Label + recommended + suggested,
                current + selected,
                availableWidth
            );
            if (option.Description.Length > 0)
            {
                AddWrapped(
                    lines,
                    option.Description,
                    new string(' ', descriptionIndent),
                    availableWidth
                );
            }
        }

        string freeTextPrefix = question.FreeTextFocused ? "> Free text: " : "  Free text (Tab): ";
        AddWrapped(lines, question.FreeText ?? string.Empty, freeTextPrefix, availableWidth);
        return lines;
    }

    private static void AddWrapped(List<string> lines, string text, string prefix, int width)
    {
        int prefixWidth = CellText.Width(prefix);
        int available = Math.Max(1, width - prefixWidth);
        IReadOnlyList<string> wrapped = CellText.Wrap(text, available);
        for (int index = 0; index < wrapped.Count; index++)
        {
            string linePrefix = index == 0 ? prefix : new string(' ', prefixWidth);
            lines.Add(linePrefix + wrapped[index].TrimEnd());
        }
    }

    /// <summary>The picker's plain lines as shown in the live area: title, then the marked items.</summary>
    internal static IReadOnlyList<string> PlainLines(SelectListModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        int selected = SelectedInWindow(model);
        List<string> lines = [model.Title];
        int index = 0;
        foreach (string item in PlainItems(model))
        {
            lines.Add(index == selected ? "> " + item : "  " + item);
            index++;
        }

        return lines;
    }

    /// <summary>The windowed item labels, without markers.</summary>
    internal static IReadOnlyList<string> PlainItems(SelectListModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Items.Count <= VisibleItems)
        {
            return model.Items;
        }

        (int start, int count) = Window(model);
        return [.. model.Items.Skip(start).Take(count)];
    }

    private static (int Start, int Count) Window(SelectListModel model)
    {
        int count = Math.Min(VisibleItems, model.Items.Count);
        int selected = SelectedIndex(model);
        int start =
            count == 0 ? 0 : Math.Clamp(selected - VisibleItems / 2, 0, model.Items.Count - count);
        return (start, count);
    }

    private static int SelectedInWindow(SelectListModel model) =>
        model.Items.Count == 0 ? -1 : SelectedIndex(model) - Window(model).Start;

    private static int SelectedIndex(SelectListModel model) =>
        model.Items.Count == 0 ? -1 : Math.Clamp(model.Selected, 0, model.Items.Count - 1);
}
