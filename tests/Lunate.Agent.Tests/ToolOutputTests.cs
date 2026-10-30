using System.Globalization;

namespace Lunate.Agent.Tests;

public sealed class ToolOutputTests
{
    [Fact]
    public void DefaultLimit_is_thirty_thousand()
    {
        Assert.Equal(30_000, ToolOutput.DefaultLimit);
    }

    [Fact]
    public void EstimateTokens_uses_utf8_bytes_divided_by_four()
    {
        Assert.Equal(2, ToolOutput.EstimateTokens("😀😀"));
    }

    [Fact]
    public void Output_under_both_spill_limits_is_unchanged()
    {
        const string output = "short output";

        Assert.False(ToolOutput.ExceedsSpillLimits(output, tokenThreshold: 4, characterLimit: 100));
        Assert.Equal(output, ToolOutput.Truncate(output, limit: 100));
    }

    [Fact]
    public void Output_over_the_token_threshold_exceeds_spill_limits()
    {
        Assert.True(
            ToolOutput.ExceedsSpillLimits("12345678", tokenThreshold: 1, characterLimit: 100)
        );
    }

    [Fact]
    public void Output_over_the_character_cap_exceeds_spill_limits()
    {
        Assert.True(
            ToolOutput.ExceedsSpillLimits("123456789", tokenThreshold: 10, characterLimit: 8)
        );
    }

    [Fact]
    public void Spill_pointer_keeps_ten_head_and_tail_lines_with_invariant_counts()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");
            string output = string.Join('\n', Enumerable.Range(1, 25).Select(i => $"line-{i:D2}"));

            string pointer = ToolOutput.RenderPointer(output, "/sessions/tool-output/run-1.txt");

            Assert.Contains("line-01", pointer, StringComparison.Ordinal);
            Assert.Contains("line-10", pointer, StringComparison.Ordinal);
            Assert.DoesNotContain("line-11", pointer, StringComparison.Ordinal);
            Assert.Contains("line-16", pointer, StringComparison.Ordinal);
            Assert.Contains("line-25", pointer, StringComparison.Ordinal);
            Assert.Contains("40 characters omitted", pointer, StringComparison.Ordinal);
            Assert.Contains("/sessions/tool-output/run-1.txt", pointer, StringComparison.Ordinal);
            Assert.Contains("read it with offset and limit", pointer, StringComparison.Ordinal);
            Assert.Contains("25 lines", pointer, StringComparison.Ordinal);
            Assert.DoesNotContain("2.000", pointer, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Spill_pointer_preview_is_at_most_two_thousand_characters()
    {
        string line = new('x', 300);
        string output = string.Join('\n', Enumerable.Repeat(line, 30));

        string pointer = ToolOutput.RenderPointer(output, "/spill.txt");
        int markerStart = pointer.IndexOf("\n\n... [", StringComparison.Ordinal);

        Assert.InRange(markerStart, 0, 2_000);
        Assert.Contains("characters omitted", pointer, StringComparison.Ordinal);
    }

    [Fact]
    public void Spill_pointer_preview_never_splits_a_surrogate_pair()
    {
        string output =
            new string('x', 999) + "😀" + new string('x', 998) + "😀" + new string('x', 1_000);

        string pointer = ToolOutput.RenderPointer(output, "/spill.txt");

        AssertNoLoneSurrogates(pointer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short output")]
    public void Output_within_the_limit_is_returned_unchanged(string output)
    {
        Assert.Same(output, ToolOutput.Truncate(output));
    }

    [Fact]
    public void Output_exactly_at_the_limit_is_returned_unchanged()
    {
        string output = new('x', 100);

        Assert.Same(output, ToolOutput.Truncate(output, limit: 100));
    }

    [Fact]
    public void Output_one_over_the_limit_is_cut_in_the_middle()
    {
        string output = new('x', 101);

        string truncated = ToolOutput.Truncate(output, limit: 100);

        Assert.Equal(
            new string('x', 50) + "\n\n... [1 characters truncated] ...\n\n" + new string('x', 50),
            truncated
        );
    }

    [Fact]
    public void The_marker_states_the_omitted_count()
    {
        string output = new('x', 140);

        string truncated = ToolOutput.Truncate(output, limit: 100);

        Assert.Equal(
            new string('x', 50) + "\n\n... [40 characters truncated] ...\n\n" + new string('x', 50),
            truncated
        );
    }

    [Fact]
    public void The_omitted_count_is_culture_invariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");
            string output = new('x', 1_140);

            string truncated = ToolOutput.Truncate(output, limit: 100);

            Assert.Contains("[1040 characters truncated]", truncated, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_spill_marker_names_the_absolute_path_and_count_culture_invariantly()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-AT");
            string output = new('x', 1_140);

            string truncated = ToolOutput.Truncate(output, 100, "/tmp/spill/run_1-1.txt");

            Assert.StartsWith(new string('x', 50), truncated, StringComparison.Ordinal);
            Assert.EndsWith(new string('x', 50), truncated, StringComparison.Ordinal);
            Assert.Contains(
                "[1040 characters omitted; full output at /tmp/spill/run_1-1.txt]",
                truncated,
                StringComparison.Ordinal
            );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void The_marker_says_when_the_output_was_too_large_to_spill()
    {
        string output = new('x', 140);

        string truncated = ToolOutput.Truncate(output, 100, tooLarge: true);

        Assert.Equal(
            new string('x', 50)
                + "\n\n... [40 characters omitted; output too large to spill] ...\n\n"
                + new string('x', 50),
            truncated
        );
    }

    [Fact]
    public void Cut_points_do_not_split_surrogate_pairs()
    {
        string output = "abcd" + "😀" + new string('x', 40) + "😀" + "wxyz";

        string truncated = ToolOutput.Truncate(output, limit: 10);

        Assert.Equal("abcd" + "\n\n... [44 characters truncated] ...\n\n" + "wxyz", truncated);
        AssertNoLoneSurrogates(truncated);
    }

    [Fact]
    public void Tiny_limits_degrade_gracefully()
    {
        string output = new('x', 50);

        Assert.Equal(
            "\n\n... [50 characters truncated] ...\n\n",
            ToolOutput.Truncate(output, limit: 0)
        );
        Assert.Equal(
            "\n\n... [49 characters truncated] ...\n\nx",
            ToolOutput.Truncate(output, limit: 1)
        );
        Assert.Equal(
            "\n\n... [50 characters truncated] ...\n\n",
            ToolOutput.Truncate(output, limit: -1)
        );
    }

    [Fact]
    public void Empty_output_passes_through_even_with_a_tiny_limit()
    {
        Assert.Same(string.Empty, ToolOutput.Truncate(string.Empty, limit: 0));
    }

    [Fact]
    public void Truncate_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => ToolOutput.Truncate(null!));
    }

    private static void AssertNoLoneSurrogates(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                Assert.True(
                    i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]),
                    $"high surrogate at {i} is not followed by a low surrogate"
                );
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(value[i]), $"lone low surrogate at {i}");
            }
        }
    }
}
