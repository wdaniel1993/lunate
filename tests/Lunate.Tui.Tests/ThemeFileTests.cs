using System.Globalization;
using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The user theme file: the path resolution (<c>--theme &lt;path&gt;</c> over <c>theme.json</c> in
/// the Lunate home), the three colour forms, and the all-or-nothing rule — an unknown token, a
/// mistyped colour or unreadable JSON discards the whole file with one line, and the selected
/// built-in keeps every token.
/// </summary>
public sealed class ThemeFileTests
{
    [Fact]
    public void The_flag_path_wins_and_otherwise_the_home_file_is_read()
    {
        Assert.Equal(
            Path.Combine("/home", "theme.json"),
            ThemeFile.ResolvePath(flagPath: null, home: "/home")
        );
        Assert.Equal(
            "/elsewhere/mine.json",
            ThemeFile.ResolvePath("/elsewhere/mine.json", "/home")
        );
    }

    [Fact]
    public void A_missing_file_leaves_the_selected_theme_alone()
    {
        ThemeOutcome outcome = ThemeFile.Read(ScratchPath("absent.json"));

        Assert.Null(outcome.Error);
        Assert.Empty(outcome.Tokens);
    }

    [Fact]
    public void A_file_that_is_not_a_file_names_the_path()
    {
        string path = ScratchPath(string.Empty);
        Directory.CreateDirectory(path);
        try
        {
            ThemeOutcome outcome = ThemeFile.Read(path);

            Assert.NotNull(outcome.Error);
            Assert.Contains("is not a file", outcome.Error, StringComparison.Ordinal);
            Assert.Empty(outcome.Tokens);
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Fact]
    public void The_three_colour_forms_are_applied()
    {
        ThemeOutcome outcome = Read(
            """
            {"accent":"#ff8800","muted":"bright-blue","error":"colour:208"}
            """
        );

        Assert.Null(outcome.Error);
        Assert.Equal(3, outcome.Tokens.Count);
        Assert.Equal(
            "\u001b[38;2;255;136;0m",
            outcome.Tokens[ThemeToken.Accent].Sgr(ColorSystemSupport.TrueColor)
        );
        Assert.Equal(
            "blue",
            outcome.Tokens[ThemeToken.Muted].MarkupColor(ColorSystemSupport.TrueColor)
        );
        Assert.Equal(
            "\u001b[38;5;208m",
            outcome.Tokens[ThemeToken.Error].Sgr(ColorSystemSupport.EightBit)
        );
    }

    [Fact]
    public void An_unknown_token_fails_the_load_and_lists_the_ten_valid_ones()
    {
        ThemeOutcome outcome = Read("""{"highlight":"red"}""");

        Assert.NotNull(outcome.Error);
        Assert.Contains("unknown token 'highlight'", outcome.Error, StringComparison.Ordinal);
        foreach (ThemeToken token in ThemeTokens.All)
        {
            Assert.Contains(ThemeTokens.Name(token), outcome.Error, StringComparison.Ordinal);
        }

        Assert.Equal(10, ThemeTokens.All.Length);
        Assert.Empty(outcome.Tokens);
    }

    [Fact]
    public void An_unreadable_colour_names_the_token_and_the_accepted_forms()
    {
        ThemeOutcome outcome = Read("""{"accent":"orange-ish"}""");

        Assert.NotNull(outcome.Error);
        Assert.Contains("token 'accent'", outcome.Error, StringComparison.Ordinal);
        Assert.Contains("an ANSI name", outcome.Error, StringComparison.Ordinal);
        Assert.Empty(outcome.Tokens);
    }

    [Fact]
    public void A_token_that_is_not_a_string_fails_the_load()
    {
        ThemeOutcome outcome = Read("""{"accent":12}""");

        Assert.NotNull(outcome.Error);
        Assert.Contains("must be a string colour", outcome.Error, StringComparison.Ordinal);
        Assert.Empty(outcome.Tokens);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"accent\"")]
    public void A_file_that_is_not_an_object_fails_the_load(string json)
    {
        ThemeOutcome outcome = Read(json);

        Assert.NotNull(outcome.Error);
        Assert.Contains("an object of tokens", outcome.Error, StringComparison.Ordinal);
        Assert.Empty(outcome.Tokens);
    }

    [Fact]
    public void Unreadable_json_fails_the_load_without_throwing()
    {
        ThemeOutcome outcome = Read("""{"accent":""");

        Assert.NotNull(outcome.Error);
        Assert.Contains("not readable JSON", outcome.Error, StringComparison.Ordinal);
        Assert.Empty(outcome.Tokens);
    }

    [Fact]
    public void One_bad_token_discards_the_whole_file()
    {
        ThemeOutcome outcome = Read("""{"accent":"red","highlight":"blue"}""");

        Assert.NotNull(outcome.Error);
        Assert.Empty(outcome.Tokens);
    }

    private static ThemeOutcome Read(string json)
    {
        string path = ScratchPath(
            string.Create(CultureInfo.InvariantCulture, $"theme-{Guid.NewGuid():N}.json")
        );
        File.WriteAllText(path, json);
        try
        {
            return ThemeFile.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A path in a fresh directory under the process temp root (<c>$TMPDIR</c> on POSIX).</summary>
    private static string ScratchPath(string fileName)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"lunate-theme-{Guid.NewGuid():N}")
        );
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
