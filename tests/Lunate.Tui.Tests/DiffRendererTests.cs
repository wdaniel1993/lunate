namespace Lunate.Tui.Tests;

public sealed class DiffRendererTests
{
    private const string SampleDiff = """
        --- a/src/x.cs
        +++ b/src/x.cs
        @@ -1,3 +1,3 @@
         int a = 1;
        -int b = 2;
        +int b = 3;
         int c = 4;
        """;

    [Fact]
    public void Well_formed_diff_classifies_every_line()
    {
        DiffLine[] expected =
        [
            new(DiffLineKind.MatchTier, "match: exact"),
            new(DiffLineKind.FileHeader, "--- a/src/x.cs"),
            new(DiffLineKind.FileHeader, "+++ b/src/x.cs"),
            new(DiffLineKind.HunkHeader, "@@ -1,3 +1,3 @@"),
            new(DiffLineKind.Context, " int a = 1;"),
            new(DiffLineKind.Deletion, "-int b = 2;"),
            new(DiffLineKind.Insertion, "+int b = 3;"),
            new(DiffLineKind.Context, " int c = 4;"),
        ];

        Assert.Equal(
            expected,
            DiffRenderer.Parse(new ToolDiffInfo("src/x.cs", "exact", SampleDiff))
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    [InlineData("\n\n")]
    public void Empty_or_whitespace_diffs_parse_to_nothing(string text) =>
        Assert.Empty(DiffRenderer.Parse(new ToolDiffInfo("src/x.cs", "exact", text)));

    [Fact]
    public void A_trailing_newline_is_trimmed() =>
        Assert.Equal(
            DiffRenderer.Parse(new ToolDiffInfo("p", "exact", SampleDiff)).Count,
            DiffRenderer.Parse(new ToolDiffInfo("p", "exact", SampleDiff + "\n")).Count
        );

    [Fact]
    public void Crlf_diffs_parse_identically() =>
        Assert.Equal(
            DiffRenderer.Parse(new ToolDiffInfo("p", "normalized", SampleDiff)),
            DiffRenderer.Parse(
                new ToolDiffInfo("p", "normalized", SampleDiff.Replace("\n", "\r\n"))
            )
        );

    [Fact]
    public void Empty_tier_omits_the_label()
    {
        var parsed = DiffRenderer.Parse(new ToolDiffInfo("p", "", SampleDiff));

        Assert.DoesNotContain(parsed, line => line.Kind == DiffLineKind.MatchTier);
        Assert.Equal(DiffLineKind.FileHeader, parsed[0].Kind);
    }

    [Fact]
    public void Unknown_tiers_keep_their_name() =>
        Assert.Equal(
            new DiffLine(DiffLineKind.MatchTier, "match: indent"),
            DiffRenderer.Parse(new ToolDiffInfo("p", "indent", SampleDiff))[0]
        );

    [Fact]
    public void Malformed_lines_are_tolerated_and_header_like_content_after_a_hunk_stays_content()
    {
        const string malformed = "@@ nonsense\n-a\n+b\nplain\n--- x";

        var parsed = DiffRenderer.Parse(new ToolDiffInfo("p", "other", malformed));

        DiffLine[] expected =
        [
            new(DiffLineKind.MatchTier, "match: other"),
            new(DiffLineKind.HunkHeader, "@@ nonsense"),
            new(DiffLineKind.Deletion, "-a"),
            new(DiffLineKind.Insertion, "+b"),
            new(DiffLineKind.Context, "plain"),
            new(DiffLineKind.Deletion, "--- x"),
        ];
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void File_headers_after_the_first_hunk_are_not_special()
    {
        var parsed = DiffRenderer.Parse(
            new ToolDiffInfo("p", "exact", "@@ -1 +1 @@\n+++ not a header")
        );

        Assert.Equal(new DiffLine(DiffLineKind.Insertion, "+++ not a header"), parsed[^1]);
    }

    [Fact]
    public void Huge_lines_survive()
    {
        string huge = new('x', 1_000_000);
        var parsed = DiffRenderer.Parse(new ToolDiffInfo("p", "exact", "@@ -1 +1 @@\n+" + huge));

        Assert.Equal(DiffLineKind.Insertion, parsed[^1].Kind);
        Assert.Equal("+" + huge, parsed[^1].Text);
    }
}
