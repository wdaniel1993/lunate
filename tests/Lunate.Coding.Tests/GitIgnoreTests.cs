namespace Lunate.Coding.Tests;

/// <summary>
/// The ignore-engine matrix: one test class per pattern behaviour the spec pins. Evaluation is
/// per .gitignore file; cross-file precedence (deeper wins) and directory pruning live in
/// <see cref="FileIndex"/> and are covered by <see cref="FileIndexTests"/>.
/// </summary>
public sealed class GitIgnoreTests
{
    private static bool? Match(string ignore, string path, bool isDirectory = false) =>
        GitIgnore.Parse(ignore).Match(path, isDirectory);

    [Fact]
    public void Blank_lines_and_comments_are_skipped()
    {
        const string ignore = "\n# a comment\n   \nfoo.txt\n";
        Assert.True(Match(ignore, "foo.txt"));
        Assert.Null(Match(ignore, "bar.txt"));
    }

    [Fact]
    public void Literals_match_files_and_directories_at_any_depth()
    {
        Assert.True(Match("foo.txt", "foo.txt"));
        Assert.True(Match("foo.txt", "a/b/foo.txt"));
        Assert.Null(Match("foo.txt", "foo.txt.bak"));
        Assert.Null(Match("foo.txt", "barfoo.txt"));
    }

    [Fact]
    public void Star_matches_within_one_segment()
    {
        Assert.True(Match("*.log", "a.log"));
        Assert.True(Match("*.log", "a/b.log"));
        Assert.Null(Match("*.log", "b.log.bak"));
        Assert.Null(Match("*.log", "dir.log/inside.txt"));
    }

    [Fact]
    public void Question_mark_matches_exactly_one_character()
    {
        Assert.True(Match("f?o", "foo"));
        Assert.Null(Match("f?o", "fo"));
        Assert.Null(Match("f?o", "fooo"));
        Assert.Null(Match("f?o", "f/o"));
    }

    [Fact]
    public void Double_star_slash_matches_any_depth()
    {
        Assert.True(Match("**/temp", "temp"));
        Assert.True(Match("**/temp", "a/temp"));
        Assert.True(Match("**/temp", "a/b/temp"));
        Assert.Null(Match("**/temp", "atemp"));
    }

    [Fact]
    public void Double_star_suffix_matches_everything_below()
    {
        Assert.True(Match("logs/**", "logs/app.log"));
        Assert.True(Match("logs/**", "logs/a/b.log"));
        Assert.Null(Match("logs/**", "logs", isDirectory: true));
        Assert.Null(Match("logs/**", "other/logs/x"));
    }

    [Fact]
    public void Double_star_middle_matches_zero_or_more_directories()
    {
        Assert.True(Match("a/**/b", "a/b"));
        Assert.True(Match("a/**/b", "a/x/b"));
        Assert.True(Match("a/**/b", "a/x/y/b"));
        Assert.Null(Match("a/**/b", "a/b/c"));
    }

    [Fact]
    public void Character_classes_are_supported()
    {
        Assert.True(Match("file[0-9].txt", "file1.txt"));
        Assert.Null(Match("file[0-9].txt", "filea.txt"));
        Assert.True(Match("[!ab].cs", "c.cs"));
        Assert.Null(Match("[!ab].cs", "a.cs"));
    }

    [Fact]
    public void The_last_matching_pattern_in_a_file_wins()
    {
        const string ignore = "*\n!keep.txt\n";
        Assert.False(Match(ignore, "keep.txt"));
        Assert.True(Match(ignore, "drop.txt"));
    }

    [Fact]
    public void Negation_only_returns_an_opinion_when_it_matches()
    {
        Assert.Null(Match("!keep.txt", "other.txt"));
    }

    [Fact]
    public void A_trailing_slash_matches_directories_only()
    {
        Assert.True(Match("bin/", "bin", isDirectory: true));
        Assert.Null(Match("bin/", "bin", isDirectory: false));
        Assert.True(Match("bin/", "a/bin", isDirectory: true));
    }

    [Fact]
    public void A_leading_slash_anchors_to_the_scope_root()
    {
        Assert.True(Match("/root.txt", "root.txt"));
        Assert.Null(Match("/root.txt", "a/root.txt"));
    }

    [Fact]
    public void A_middle_slash_anchors_to_the_scope_directory()
    {
        Assert.True(Match("doc/*.txt", "doc/a.txt"));
        Assert.Null(Match("doc/*.txt", "x/doc/a.txt"));
        Assert.Null(Match("doc/*.txt", "doc/sub/a.txt"));
    }

    [Fact]
    public void A_pattern_without_a_slash_matches_the_basename_at_any_depth()
    {
        Assert.True(Match("README.md", "README.md"));
        Assert.True(Match("README.md", "a/b/README.md"));
    }

    [Fact]
    public void Escaped_leading_hash_is_a_pattern_not_a_comment()
    {
        Assert.True(Match("\\#notes.txt", "#notes.txt"));
    }

    [Fact]
    public void Trailing_spaces_are_trimmed()
    {
        Assert.True(Match("foo.txt   ", "foo.txt"));
    }
}
