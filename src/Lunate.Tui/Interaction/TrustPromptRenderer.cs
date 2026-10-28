using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders a pending trust question as the question line (<c>{title}?  [y]es  [n]o</c>, with an
/// <c>n of m</c> label when several questions are pending) followed by one dim line per detail.
/// All user-derived text is markup-escaped; the question marker is yellow.
/// <para>
/// The question mark is added here only when the title does not already end with one: the
/// single-file question is phrased with it (<c>SingleFileTrustQuestion.Title</c>), the tool-approval
/// and packaged-extension questions are not, and a title must never show two.
/// </para>
/// <para>
/// At narrow widths the title and every detail line wrap instead of being clipped, so the part
/// that decides the answer — a path segment, a package name — is always visible. The choices are
/// appended to the last title line when they fit, else they get a line of their own; the
/// <c>n of m</c> label is dropped first when the single-line headline does not fit.
/// </para>
/// </summary>
public sealed class TrustPromptRenderer
{
    public IRenderable Render(TrustPromptModel prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        var builder = new StringBuilder();
        StyledLine headline = Headline(prompt);
        builder.Append(headline.ToMarkup()).Append('\n');
        foreach (string detail in prompt.Details)
        {
            var line = new StyledLine();
            line.Add(detail, SpanStyle.Dim);
            builder.Append(line.ToMarkup()).Append('\n');
        }

        return new Markup(builder.ToString());
    }

    private const string Choices = "  [y]es  [n]o";

    /// <summary>
    /// The question's plain (unstyled) lines as shown in the live area, fitted to
    /// <paramref name="width"/> by wrapping the title and the details. The default width keeps the
    /// whole headline on one line and every detail as written.
    /// </summary>
    internal static IReadOnlyList<string> PlainLines(
        TrustPromptModel prompt,
        int width = int.MaxValue,
        bool unicode = true
    )
    {
        ArgumentNullException.ThrowIfNull(prompt);

        string title = Question(prompt.Title);
        string count = CountLabel(prompt);
        List<string> lines;
        if (
            width == int.MaxValue
            || CellText.Width(title) + CellText.Width(count) + CellText.Width(Choices) <= width
        )
        {
            lines = [HeadlineText(prompt)];
        }
        else
        {
            // The label is dropped before the title; the title itself never loses a character.
            lines = [.. CellText.Wrap(title, width)];
            if (
                CellText.Width(Choices) <= width
                && CellText.Width(lines[^1]) + CellText.Width(Choices) <= width
            )
            {
                lines[^1] += Choices;
            }
            else
            {
                lines.Add(CellText.Clip(Choices, width));
            }
        }

        foreach (string detail in prompt.Details)
        {
            lines.AddRange(width == int.MaxValue ? [detail] : CellText.Wrap(detail, width));
        }

        return lines;
    }

    internal static string HeadlineText(TrustPromptModel prompt) =>
        FormattableString.Invariant($"{Question(prompt.Title)}{CountLabel(prompt)}{Choices}");

    private static string CountLabel(TrustPromptModel prompt) =>
        prompt.Total > 1
            ? FormattableString.Invariant($"  {prompt.Step} of {prompt.Total}")
            : string.Empty;

    private static StyledLine Headline(TrustPromptModel prompt)
    {
        // The marker is styled on its own, so a title that already ends with one gives its question
        // mark to that span instead of producing a second.
        string title = prompt.Title.TrimEnd();
        bool owns = title.EndsWith('?');
        var line = new StyledLine();
        line.Add(owns ? title[..^1] : title, SpanStyle.Plain);
        line.Add("?", ApprovalPromptRenderer.Question);
        line.Add(CountLabel(prompt), SpanStyle.Dim);
        line.Add("  [y]es  [n]o", SpanStyle.Plain);
        return line;
    }

    /// <summary>The title with exactly one question mark at its end, whoever phrased it.</summary>
    internal static string Question(string title)
    {
        string trimmed = title.TrimEnd();
        return trimmed.EndsWith('?') ? trimmed : trimmed + "?";
    }
}
