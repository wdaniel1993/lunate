using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders the status footer as one dim line with middle-dot separators, culture-invariantly.
/// Narrow widths degrade deterministically: the branch drops first, then the directory; model and
/// usage always survive. The live area reuses <see cref="PlainText"/> so one format serves both.
/// </summary>
public sealed class StatusFooterRenderer
{
    private const string Separator = " \u00b7 ";
    private const int MinimumSegments = 2;

    public IRenderable Render(StatusFooterModel footer, int width)
    {
        ArgumentNullException.ThrowIfNull(footer);
        var line = new StyledLine();
        line.Add(PlainText(footer, width), SpanStyle.Dim);
        return new Markup(line.ToMarkup() + "\n");
    }

    /// <summary>The footer's plain (unstyled) line, narrowed for the given terminal width.</summary>
    internal static string PlainText(StatusFooterModel footer, int width)
    {
        var segments = new List<string> { footer.Model, Usage(footer) };
        if (!string.IsNullOrEmpty(footer.WorkingDirectory))
        {
            segments.Add(footer.WorkingDirectory);
        }

        if (!string.IsNullOrEmpty(footer.GitBranch))
        {
            segments.Add(footer.GitBranch);
        }

        while (
            segments.Count > MinimumSegments
            && CellText.Width(string.Join(Separator, segments)) > width
        )
        {
            segments.RemoveAt(segments.Count - 1);
        }

        return string.Join(Separator, segments);
    }

    internal static string FormatTokens(long tokens)
    {
        if (tokens < 1000)
        {
            return tokens.ToString(CultureInfo.InvariantCulture);
        }

        decimal thousands = tokens / 1000m;
        if (decimal.Round(thousands, 1, MidpointRounding.AwayFromZero) >= 1000m)
        {
            return (tokens / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M";
        }

        return thousands.ToString("0.0", CultureInfo.InvariantCulture) + "k";
    }

    private static string Usage(StatusFooterModel footer)
    {
        string tokens = FormatTokens(footer.TokensUsed) + "/" + FormatTokens(footer.ContextWindow);
        return footer.ContextWindow <= 0
            ? tokens
            : string.Concat(tokens, " (", Percent(footer), ")");
    }

    private static string Percent(StatusFooterModel footer)
    {
        decimal percent = Math.Round(
            (decimal)footer.TokensUsed * 100 / footer.ContextWindow,
            MidpointRounding.AwayFromZero
        );
        return percent.ToString("0", CultureInfo.InvariantCulture) + "%";
    }
}
