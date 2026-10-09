namespace Lunate.Tui.Tests;

public sealed class SyntaxHighlightTests
{
    [Theory]
    [InlineData("var x = 1;")]
    [InlineData("var x = \"a\\\"b\"; // done")]
    [InlineData("/* unterminated")]
    [InlineData("@\"verbatim \"\" quote\"")]
    [InlineData("$\"interp {x}\" + @$\"v\"")]
    [InlineData("don't panic")]
    [InlineData("")]
    public void CSharp_reconstructs_the_line_exactly(string line)
    {
        bool inBlockComment = false;
        Assert.Equal(
            line,
            string.Concat(
                SyntaxHighlight.CSharp(line, ref inBlockComment).Select(span => span.Text)
            )
        );
    }

    [Theory]
    [InlineData("{\"a\": [1, -2.5e3, true]}")]
    [InlineData("\"unterminated")]
    [InlineData("-")]
    [InlineData("")]
    public void Json_reconstructs_the_line_exactly(string line) =>
        Assert.Equal(line, string.Concat(SyntaxHighlight.Json(line).Select(span => span.Text)));

    [Theory]
    [InlineData("if [ -f \"$HOME/x\" ]; then")]
    [InlineData("echo '$HOME' # not a var")]
    [InlineData("echo \"unterminated")]
    [InlineData("$ ${")]
    [InlineData("#!/bin/sh")]
    [InlineData("")]
    public void Shell_reconstructs_the_line_exactly(string line) =>
        Assert.Equal(line, string.Concat(SyntaxHighlight.Shell(line).Select(span => span.Text)));

    [Fact]
    public void CSharp_keywords_strings_and_comments_are_classified()
    {
        bool inBlockComment = false;
        var spans = SyntaxHighlight.CSharp("var name = \"hi\"; // note", ref inBlockComment);

        Assert.Contains(spans, span => span is { Kind: HighlightKind.Keyword, Text: "var" });
        Assert.Contains(spans, span => span is { Kind: HighlightKind.String, Text: "\"hi\"" });
        Assert.Contains(spans, span => span is { Kind: HighlightKind.Comment, Text: "// note" });
        Assert.False(inBlockComment);
    }

    [Fact]
    public void CSharp_block_comments_span_lines()
    {
        bool inBlockComment = false;
        var first = SyntaxHighlight.CSharp("/* open", ref inBlockComment);
        Assert.True(inBlockComment);
        Assert.Contains(first, span => span is { Kind: HighlightKind.Comment, Text: "/* open" });

        var second = SyntaxHighlight.CSharp("still */ code", ref inBlockComment);
        Assert.False(inBlockComment);
        Assert.Equal("still */", second[0].Text);
        Assert.Equal(HighlightKind.Comment, second[0].Kind);
        Assert.Contains(second, span => span is { Kind: HighlightKind.Plain, Text: " code" });
    }

    [Fact]
    public void Json_keys_strings_and_numbers_are_classified()
    {
        var spans = SyntaxHighlight.Json("{\"key\": 12.5e3}");
        Assert.Contains(spans, span => span is { Kind: HighlightKind.Key, Text: "\"key\"" });
        Assert.Contains(spans, span => span is { Kind: HighlightKind.Number, Text: "12.5e3" });

        var value = SyntaxHighlight.Json("\"value\"");
        Assert.Contains(value, span => span.Kind == HighlightKind.String);
        Assert.DoesNotContain(value, span => span.Kind == HighlightKind.Key);
    }

    [Fact]
    public void Shell_keywords_strings_and_variables_are_classified()
    {
        var spans = SyntaxHighlight.Shell("if [ -f \"$HOME/x\" ]; then echo $HOME; fi");

        Assert.Contains(spans, span => span is { Kind: HighlightKind.Keyword, Text: "if" });
        Assert.Contains(spans, span => span is { Kind: HighlightKind.Keyword, Text: "then" });
        Assert.Contains(spans, span => span is { Kind: HighlightKind.Keyword, Text: "fi" });
        Assert.Contains(spans, span => span is { Kind: HighlightKind.String, Text: "\"$HOME/x\"" });
        Assert.Contains(spans, span => span is { Kind: HighlightKind.Variable, Text: "$HOME" });
    }

    [Fact]
    public void Shell_hash_inside_a_word_is_not_a_comment()
    {
        var spans = SyntaxHighlight.Shell("echo a#b");

        Assert.DoesNotContain(spans, span => span.Kind == HighlightKind.Comment);
    }

    [Fact]
    public void Language_keys_normalize_known_names_only()
    {
        Assert.Equal("csharp", SyntaxHighlight.LanguageKey("CSharp"));
        Assert.Equal("csharp", SyntaxHighlight.LanguageKey(" cs "));
        Assert.Equal("json", SyntaxHighlight.LanguageKey("JSON"));
        Assert.Equal("shell", SyntaxHighlight.LanguageKey("bash"));
        Assert.Null(SyntaxHighlight.LanguageKey("nix"));
        Assert.Null(SyntaxHighlight.LanguageKey(null));
    }
}
