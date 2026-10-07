namespace Lunate.Coding.Tests;

public sealed class LineDiffTests
{
    [Fact]
    public void Identical_texts_have_no_diff()
    {
        Assert.Equal("", LineDiff.Unified("a\nb\n", "a\nb\n", "File.txt"));
    }

    [Fact]
    public void Normalized_line_endings_have_no_diff()
    {
        Assert.Equal("", LineDiff.Unified("a\r\nb\r\n", "a\nb\n", "File.txt"));
    }

    [Fact]
    public void A_creation_shows_all_added_lines()
    {
        Assert.Equal(
            "--- a/New.txt\n+++ b/New.txt\n@@ -0,0 +1,2 @@\n+a\n+b",
            LineDiff.Unified("", "a\nb\n", "New.txt")
        );
    }

    [Fact]
    public void A_deletion_in_the_middle_keeps_three_context_lines()
    {
        Assert.Equal(
            "--- a/File.txt\n+++ b/File.txt\n@@ -1,6 +1,5 @@\n a\n b\n-c\n d\n e\n f",
            LineDiff.Unified("a\nb\nc\nd\ne\nf\ng\nh\n", "a\nb\nd\ne\nf\ng\nh\n", "File.txt")
        );
    }

    [Fact]
    public void An_append_ends_the_hunk_at_the_last_line()
    {
        Assert.Equal(
            "--- a/File.txt\n+++ b/File.txt\n@@ -1,2 +1,3 @@\n a\n b\n+c",
            LineDiff.Unified("a\nb\n", "a\nb\nc\n", "File.txt")
        );
    }

    [Fact]
    public void Distant_changes_form_separate_hunks()
    {
        var oldText = string.Join('\n', Enumerable.Range(1, 20).Select(n => $"line{n:D2}")) + "\n";
        var newText =
            string.Join(
                '\n',
                Enumerable
                    .Range(1, 20)
                    .Select(n =>
                        n switch
                        {
                            2 => "X",
                            15 => "Y",
                            _ => $"line{n:D2}",
                        }
                    )
            ) + "\n";

        Assert.Equal(
            "--- a/File.txt\n"
                + "+++ b/File.txt\n"
                + "@@ -1,5 +1,5 @@\n"
                + " line01\n"
                + "-line02\n"
                + "+X\n"
                + " line03\n"
                + " line04\n"
                + " line05\n"
                + "@@ -12,7 +12,7 @@\n"
                + " line12\n"
                + " line13\n"
                + " line14\n"
                + "-line15\n"
                + "+Y\n"
                + " line16\n"
                + " line17\n"
                + " line18",
            LineDiff.Unified(oldText, newText, "File.txt")
        );
    }

    [Fact]
    public void Changes_closer_than_two_context_windows_merge_into_one_hunk()
    {
        var oldText = string.Join('\n', Enumerable.Range(1, 10).Select(n => $"l{n}")) + "\n";
        var newText =
            string.Join(
                '\n',
                Enumerable
                    .Range(1, 10)
                    .Select(n =>
                        n switch
                        {
                            2 => "X",
                            9 => "Y",
                            _ => $"l{n}",
                        }
                    )
            ) + "\n";

        Assert.Equal(
            "--- a/File.txt\n"
                + "+++ b/File.txt\n"
                + "@@ -1,10 +1,10 @@\n"
                + " l1\n"
                + "-l2\n"
                + "+X\n"
                + " l3\n"
                + " l4\n"
                + " l5\n"
                + " l6\n"
                + " l7\n"
                + " l8\n"
                + "-l9\n"
                + "+Y\n"
                + " l10",
            LineDiff.Unified(oldText, newText, "File.txt")
        );
    }

    [Fact]
    public void Very_large_inputs_fall_back_to_a_single_replace_hunk()
    {
        var oldLines = Enumerable.Range(0, 20001).Select(n => $"l{n}").ToArray();
        var newLines = (string[])oldLines.Clone();
        newLines[10000] = "changed";
        var oldText = string.Join('\n', oldLines) + "\n";
        var newText = string.Join('\n', newLines) + "\n";

        Assert.Equal(
            "--- a/Big.txt\n"
                + "+++ b/Big.txt\n"
                + "@@ -9998,7 +9998,7 @@\n"
                + " l9997\n"
                + " l9998\n"
                + " l9999\n"
                + "-l10000\n"
                + "+changed\n"
                + " l10001\n"
                + " l10002\n"
                + " l10003",
            LineDiff.Unified(oldText, newText, "Big.txt")
        );
    }
}
