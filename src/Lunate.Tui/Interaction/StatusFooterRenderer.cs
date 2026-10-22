using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Lunate.Tui;

/// <summary>
/// Renders the status footer as one dim line with middle-dot separators, culture-invariantly.
/// Narrow widths degrade by priority, not by position: widgets drop first, then the branch, the
/// directory, thinking, favourites and the turn cost; model and usage always survive. The live
/// area reuses <see cref="PlainText"/> so one format serves both.
/// </summary>
public sealed class StatusFooterRenderer
{
    private const string Separator = " \u00b7 ";

    // Lower ranks drop first. Model and usage carry KeepRank, so the loop stops with them alone.
    private const int WidgetRank = 0;
    private const int BranchRank = 1;
    private const int DirectoryRank = 2;
    private const int ThinkingRank = 3;
    private const int FavouriteRank = 4;
    private const int CostRank = 5;
    private const int KeepRank = int.MaxValue;

    public IRenderable Render(StatusFooterModel footer, int width)
    {
        ArgumentNullException.ThrowIfNull(footer);
        var line = new StyledLine();
        line.Add(PlainText(footer, width), SpanStyle.Dim);
        return new Markup(line.ToMarkup() + "\n");
    }

    /// <summary>The footer's plain (unstyled) line, narrowed for the given terminal width.
    /// Extension widget texts append after the core fields in registration order and drop first.</summary>
    internal static string PlainText(
        StatusFooterModel footer,
        int width,
        IReadOnlyList<string>? widgets = null,
        int queuedCount = 0
    )
    {
        // The compact band keeps the model, the queue indicator and the context percent; every
        // other segment — tokens, window, thinking, favourites, cost, directory, branch and
        // widgets — drops regardless of width.
        if (WidthBands.IsCompact(width))
        {
            var compact = footer.Model;
            if (queuedCount > 0)
            {
                compact += Separator + Queued(queuedCount);
            }

            return footer.ContextWindow <= 0 ? compact : compact + Separator + Percent(footer);
        }

        var segments = new List<Segment>
        {
            new(footer.Model, KeepRank),
            new(Usage(footer), KeepRank),
        };
        if (queuedCount > 0)
        {
            segments.Add(new Segment(Queued(queuedCount), KeepRank));
        }

        if (Favourites(footer) is { } favourites)
        {
            segments.Add(new Segment(favourites, FavouriteRank));
        }

        if (!string.IsNullOrEmpty(footer.TurnCost))
        {
            segments.Add(new Segment(footer.TurnCost, CostRank));
        }

        if (!string.IsNullOrEmpty(footer.ThinkLevel))
        {
            segments.Add(new Segment("think:" + footer.ThinkLevel, ThinkingRank));
        }

        if (!string.IsNullOrEmpty(footer.WorkingDirectory))
        {
            segments.Add(new Segment(footer.WorkingDirectory, DirectoryRank));
        }

        if (!string.IsNullOrEmpty(footer.GitBranch))
        {
            segments.Add(new Segment(footer.GitBranch, BranchRank));
        }

        if (widgets is not null)
        {
            foreach (string widget in widgets)
            {
                if (!string.IsNullOrEmpty(widget))
                {
                    segments.Add(new Segment(widget, WidgetRank));
                }
            }
        }

        while (Width(segments) > width)
        {
            int index = NextDropIndex(segments);
            if (index < 0)
            {
                break;
            }

            segments.RemoveAt(index);
        }

        return string.Join(Separator, segments.Select(segment => segment.Text));
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

    /// <summary>The lowest-priority droppable segment, latest first among equals.</summary>
    private static int NextDropIndex(List<Segment> segments)
    {
        int lowest = KeepRank;
        var index = -1;
        for (var candidate = 0; candidate < segments.Count; candidate++)
        {
            int rank = segments[candidate].DropRank;
            if (rank <= lowest && rank != KeepRank)
            {
                lowest = rank;
                index = candidate;
            }
        }

        return index;
    }

    private static int Width(List<Segment> segments) =>
        CellText.Width(string.Join(Separator, segments.Select(segment => segment.Text)));

    private static string Queued(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + " queued";

    private static string? Favourites(StatusFooterModel footer) =>
        footer.FavoriteCount > 0
        && footer.FavoritePosition is int position
        && position >= 1
        && position <= footer.FavoriteCount
            ? "fav:"
                + position.ToString(CultureInfo.InvariantCulture)
                + "/"
                + footer.FavoriteCount.ToString(CultureInfo.InvariantCulture)
            : null;

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

    private readonly record struct Segment(string Text, int DropRank);
}
