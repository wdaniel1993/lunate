using Lunate.Tui;

namespace Lunate.Tui.Tests;

public sealed class FuzzyMatchTests
{
    private static CompletionItem Item(string text) =>
        new(text, null, CompletionKind.Command, null);

    private static string[] Filtered(string query, params string[] texts) =>
        [.. FuzzyMatch.Filter([.. texts.Select(Item)], query).Select(item => item.Text)];

    [Fact]
    public void A_prefix_match_outranks_a_word_start_match()
    {
        Assert.Equal(["model", "xmodel"], Filtered("mod", "xmodel", "model"));
        Assert.Equal(["model", "x/model"], Filtered("mod", "x/model", "model"));
    }

    [Fact]
    public void A_word_start_match_outranks_a_later_position()
    {
        Assert.Equal(["/compact", "xcompact"], Filtered("comp", "xcompact", "/compact"));
    }

    [Fact]
    public void An_earlier_position_outranks_a_later_one()
    {
        Assert.Equal(["abx", "xab"], Filtered("ab", "xab", "abx"));
    }

    [Fact]
    public void Ties_break_by_shorter_text_then_ordinal()
    {
        Assert.Equal(["ab", "abc"], Filtered("ab", "abc", "ab"));
        Assert.Equal(["abc", "abd"], Filtered("ab", "abd", "abc"));
    }

    [Fact]
    public void An_empty_query_keeps_the_natural_order()
    {
        Assert.Equal(
            ["/model", "/new", "/help"],
            Filtered(string.Empty, "/model", "/new", "/help")
        );
    }

    [Fact]
    public void No_subsequence_is_no_match()
    {
        Assert.Empty(Filtered("zzz", "/model", "/new"));
    }

    [Fact]
    public void A_subsequence_matches_across_separators()
    {
        Assert.Equal(["/thinking"], Filtered("thk", "/thinking"));
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        Assert.Equal(["/MODEL"], Filtered("mod", "/MODEL"));
        Assert.Equal(["/model"], Filtered("MOD", "/model"));
    }

    [Fact]
    public void Unicode_characters_match_by_letter()
    {
        Assert.Equal(["/größe"], Filtered("grö", "/größe"));
        Assert.Equal(["/größe"], Filtered("GRÖ", "/größe"));
    }

    [Fact]
    public void An_exact_match_outranks_a_longer_one()
    {
        Assert.Equal(["/new", "/newer"], Filtered("new", "/newer", "/new"));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => FuzzyMatch.Score(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => FuzzyMatch.Score("x", null!));
        Assert.Throws<ArgumentNullException>(() => FuzzyMatch.Filter(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => FuzzyMatch.Filter([], null!));
    }
}
