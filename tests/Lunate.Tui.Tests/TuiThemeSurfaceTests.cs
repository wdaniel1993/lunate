using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The theme's colour plan applied where the user sees it: the transcript viewer's renderer and
/// the status footer of the live area. A colour-capable plan keeps the styled spans (and paints
/// the footer muted); after the plan drops to <c>NoColors</c> — the <c>mono</c> theme, or a
/// terminal that never could colour — the very same frame is plain text.
/// </summary>
public sealed class TuiThemeSurfaceTests
{
    private static readonly ConsoleSize Size = new(40, 10);

    private static readonly ThinkingCommitModel Thought = new(
        Text: "considered the tradeoffs.",
        ElapsedSeconds: 12,
        Full: false
    );

    [Fact]
    public void Transcript_drops_the_thinking_span_when_the_plan_resolves_to_no_colour()
    {
        var viewer = new TranscriptViewerRenderer(
            new TerminalCapabilities(ColorSystemSupport.TrueColor, Unicode: true)
        );
        var model = new TranscriptModel([new TranscriptThinkingEntry(Thought)]);

        string before = viewer.Lines(model, Size, 0)[0].ToMarkup();

        Assert.Contains("[#af87d7]", before, StringComparison.Ordinal);

        viewer.SetColor(ColorSystemSupport.NoColors);

        string after = viewer.Lines(model, Size, 0)[0].ToMarkup();

        Assert.DoesNotContain("#af87d7", after, StringComparison.Ordinal);
        Assert.Contains("considered the tradeoffs.", after, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ColorSystemSupport.TrueColor, "\u001b[38;2;138;138;138m")]
    [InlineData(ColorSystemSupport.Standard, "\u001b[90m")]
    public void The_status_footer_paints_muted_when_the_plan_allows_colour(
        ColorSystemSupport color,
        string muted
    )
    {
        var frame = LiveAreaRenderer.Render(
            new LiveAreaState
            {
                Input = new InputLineState("go", 2),
                Footer = new StatusFooterModel("test-model", 1540, 12320, "/repo", "main"),
                Size = new ConsoleSize(60, 12),
                Color = color,
            }
        );

        int index = frame.Lines.Count - 1;
        Assert.StartsWith(muted, frame.Lines[index], StringComparison.Ordinal);
        Assert.Contains("test-model · 1.5k/12.3k (13%) · /repo", frame.Lines[index]);
    }

    [Fact]
    public void The_status_footer_stays_plain_when_the_plan_resolves_to_no_colour()
    {
        var frame = LiveAreaRenderer.Render(
            new LiveAreaState
            {
                Input = new InputLineState("go", 2),
                Footer = new StatusFooterModel("test-model", 1540, 12320, "/repo", "main"),
                Size = new ConsoleSize(60, 12),
                Color = ColorSystemSupport.NoColors,
            }
        );

        int index = frame.Lines.Count - 1;
        Assert.DoesNotContain("\u001b[", frame.Lines[index], StringComparison.Ordinal);
        Assert.Equal("test-model · 1.5k/12.3k (13%) · /repo · main", frame.Lines[index]);
    }
}
