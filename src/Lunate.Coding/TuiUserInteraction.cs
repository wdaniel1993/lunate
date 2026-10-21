using System.Text;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;
using Spectre.Console;
using Style = Lunate.Extensibility.Abstractions.TextStyle;

namespace Lunate.Coding;

/// <summary>
/// The interactive mode's interaction: notices become live-area notices, status becomes the footer
/// with the widget segments appended, and extension blocks are styled lines written above the live
/// area through the scrollback path. The segment palette maps to the TUI's existing styles and the
/// text is identical under the NO_COLOR and ASCII fallbacks — only colour attributes are dropped.
/// </summary>
internal sealed class TuiUserInteraction(LiveArea live) : IUserInteraction
{
    public void Notify(UserNotice notice) => live.SetNotice(notice.Message);

    public void UpdateStatus(UserStatus status) =>
        live.SetFooter(
            new StatusFooterModel(
                status.Model,
                status.TokensUsed,
                status.ContextWindow,
                status.WorkingDirectory,
                status.GitBranch
            )
            {
                ThinkLevel = status.ThinkLevel,
                FavoritePosition = status.FavoritePosition,
                FavoriteCount = status.FavoriteCount,
                TurnCost = status.TurnCost,
            },
            [.. status.Widgets.Select(segment => segment.Text)]
        );

    public void RenderExtension(ExtensionBlock block) =>
        live.WriteScrollback(console => console.Write(RenderBlock(block.Segments)));

    private static Markup RenderBlock(IReadOnlyList<TextSegment> segments)
    {
        var lines = new List<StyledLine> { new() };
        foreach (TextSegment segment in segments)
        {
            string[] parts = segment.Text.Split('\n');
            lines[^1].Add(parts[0], Palette(segment.Style));
            for (var index = 1; index < parts.Length; index++)
            {
                lines.Add(new StyledLine());
                lines[^1].Add(parts[index], Palette(segment.Style));
            }
        }

        var builder = new StringBuilder();
        foreach (StyledLine line in lines)
        {
            builder.Append(line.ToMarkup()).Append('\n');
        }

        return new Markup(builder.ToString());
    }

    private static SpanStyle Palette(Style style) =>
        style switch
        {
            Style.Dim => SpanStyle.Dim,
            Style.Accent => SpanStyle.Key,
            Style.Warning => ApprovalPromptRenderer.Question,
            Style.Error => ToolBlockRenderer.StatusError,
            _ => SpanStyle.Plain,
        };
}
