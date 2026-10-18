namespace Lunate.Agent.Tests;

public sealed class InlineThinkingSplitterTests
{
    private const string Open = "\u003Cthink>";
    private const string Close = "\u003C/think>";

    [Fact]
    public void Plain_text_feeds_through_as_text()
    {
        var splitter = new InlineThinkingSplitter();

        Assert.Equal("T:hello world", Render(splitter.Feed("hello world")));
        Assert.Empty(splitter.Flush());
    }

    [Fact]
    public void A_complete_span_splits_into_text_reasoning_text()
    {
        var splitter = new InlineThinkingSplitter();

        Assert.Equal(
            "T:before|R:middle|T:after",
            Render(splitter.Feed("before" + Open + "middle" + Close + "after"))
        );
        Assert.Empty(splitter.Flush());
    }

    [Fact]
    public void A_tag_alone_in_a_fragment_transitions_without_events()
    {
        var splitter = new InlineThinkingSplitter();

        Assert.Empty(splitter.Feed(Open));
        Assert.Equal("R:reason", Render(splitter.Feed("reason" + Close)));
        Assert.Equal("T:tail", Render(splitter.Feed("tail")));
        Assert.Empty(splitter.Flush());
    }

    [Fact]
    public void Decidable_content_streams_before_the_stream_ends()
    {
        var splitter = new InlineThinkingSplitter();

        Assert.Equal("T:hello ", Render(splitter.Feed("hello " + Open[..4])));
        Assert.Equal("R:reason", Render(splitter.Feed("nk>reason")));
    }

    [Fact]
    public void Every_split_boundary_yields_the_same_segments()
    {
        string stream = "ab" + Open + "cd" + Close + "ef";

        for (int split = 0; split <= stream.Length; split++)
        {
            var splitter = new InlineThinkingSplitter();
            List<InlineThinkingSplitter.Segment> segments = [];
            segments.AddRange(splitter.Feed(stream[..split]));
            segments.AddRange(splitter.Feed(stream[split..]));
            segments.AddRange(splitter.Flush());

            Assert.Equal("T:ab|R:cd|T:ef", Render(Merge(segments)));
        }
    }

    [Fact]
    public void A_partial_open_tag_at_stream_end_is_text()
    {
        var splitter = new InlineThinkingSplitter();

        Assert.Equal("T:tail", Render(splitter.Feed("tail" + Open[..4])));
        Assert.Equal("T:" + Open[..4], Render(splitter.Flush()));
    }

    private static string Render(IEnumerable<InlineThinkingSplitter.Segment> segments) =>
        string.Join(
            "|",
            segments.Select(segment => (segment.IsReasoning ? "R:" : "T:") + segment.Text)
        );

    private static List<InlineThinkingSplitter.Segment> Merge(
        IEnumerable<InlineThinkingSplitter.Segment> segments
    )
    {
        List<InlineThinkingSplitter.Segment> merged = [];
        foreach (InlineThinkingSplitter.Segment segment in segments)
        {
            if (merged.Count > 0 && merged[^1].IsReasoning == segment.IsReasoning)
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + segment.Text };
            }
            else
            {
                merged.Add(segment);
            }
        }

        return merged;
    }

    [Fact]
    public void An_unclosed_open_tag_leaves_the_rest_reasoning_at_stream_end()
    {
        var splitter = new InlineThinkingSplitter();
        List<InlineThinkingSplitter.Segment> segments = [];

        segments.AddRange(splitter.Feed("lead" + Open[..3]));
        segments.AddRange(splitter.Feed(Open[3..] + "unclosed"));
        segments.AddRange(splitter.Flush());

        Assert.Equal("T:lead|R:unclosed", Render(Merge(segments)));
    }

    [Fact]
    public void A_partial_close_tag_at_stream_end_stays_reasoning()
    {
        var splitter = new InlineThinkingSplitter();
        List<InlineThinkingSplitter.Segment> segments = [];

        segments.AddRange(splitter.Feed("a" + Open + "b" + Close[..5]));
        segments.AddRange(splitter.Flush());

        Assert.Equal("T:a|R:b" + Close[..5], Render(Merge(segments)));
    }

    [Fact]
    public void A_closing_tag_without_an_open_tag_is_plain_text()
    {
        var splitter = new InlineThinkingSplitter();

        Assert.Equal("T:x" + Close + "y", Render(splitter.Feed("x" + Close + "y")));
        Assert.Empty(splitter.Flush());
    }

    [Fact]
    public void An_empty_fragment_yields_no_segments()
    {
        var splitter = new InlineThinkingSplitter();

        Assert.Empty(splitter.Feed(""));
        Assert.Empty(splitter.Flush());
    }

    [Fact]
    public void Adjacent_spans_split_cleanly()
    {
        var splitter = new InlineThinkingSplitter();

        Assert.Equal(
            "T:a|R:b|T:c|R:d|T:e",
            Render(splitter.Feed("a" + Open + "b" + Close + "c" + Open + "d" + Close + "e"))
        );
        Assert.Empty(splitter.Flush());
    }
}
