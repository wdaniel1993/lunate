using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders a pending trust question as the question line (<c>{title}?  [y]es  [n]o</c>, with an
/// <c>n of m</c> label when several questions are pending) followed by one dim line per detail.
/// All user-derived text is markup-escaped; the question marker is yellow.
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

    /// <summary>The question's plain (unstyled) lines as shown in the live area.</summary>
    internal static IReadOnlyList<string> PlainLines(TrustPromptModel prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        List<string> lines = [HeadlineText(prompt)];
        lines.AddRange(prompt.Details);
        return lines;
    }

    internal static string HeadlineText(TrustPromptModel prompt) =>
        FormattableString.Invariant($"{prompt.Title}?{CountLabel(prompt)}  [y]es  [n]o");

    private static string CountLabel(TrustPromptModel prompt) =>
        prompt.Total > 1
            ? FormattableString.Invariant($"  {prompt.Step} of {prompt.Total}")
            : string.Empty;

    private static StyledLine Headline(TrustPromptModel prompt)
    {
        var line = new StyledLine();
        line.Add(prompt.Title, SpanStyle.Plain);
        line.Add("?", ApprovalPromptRenderer.Question);
        line.Add(CountLabel(prompt), SpanStyle.Dim);
        line.Add("  [y]es  [n]o", SpanStyle.Plain);
        return line;
    }
}
