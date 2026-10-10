using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders the status footer as one dim line with middle-dot separators, culture-invariantly.
/// Narrow widths degrade deterministically: the branch drops first, then the directory; model and
/// usage always survive.
/// </summary>
public sealed class StatusFooterRenderer
{
    private const string Separator = " \u00b7 ";
    private const int MinimumSegments = 2;

    public IRenderable Render(StatusFooterModel footer, int width)
    {
        ArgumentNullException.ThrowIfNull(footer);

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

        var line = new StyledLine();
        line.Add(string.Join(Separator, segments), SpanStyle.Dim);
        return new Markup(line.ToMarkup() + "\n");
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
