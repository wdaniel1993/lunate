using System.Globalization;

namespace Lunate.Agent.Tests;

public sealed class ToolOutputTests
{
    [Fact]
    public void DefaultLimit_is_thirty_thousand()
    {
        Assert.Equal(30_000, ToolOutput.DefaultLimit);
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
