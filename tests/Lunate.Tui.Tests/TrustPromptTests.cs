using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class TrustPromptTests
{
    [Theory]
    [InlineData("y")]
    [InlineData("Y")]
    public void Bare_y_or_capital_Y_approves(string text) =>
        Assert.Equal(TrustChoice.Approve, Prompt().Decide(Char(text)));

    [Fact]
    public void Bare_n_denies() => Assert.Equal(TrustChoice.Deny, Prompt().Decide(Char("n")));

    [Theory]
    [InlineData(KeyKind.Enter)]
    [InlineData(KeyKind.Escape)]
    public void Enter_and_Escape_deny(KeyKind kind)
    {
        var key = new KeyEvent(kind, null, false, false, false);

        Assert.Equal(TrustChoice.Deny, Prompt().Decide(key));
    }

    [Theory]
    [InlineData("y", true, false, false)]
    [InlineData("Y", true, false, false)]
    [InlineData("n", true, false, false)]
    [InlineData("y", false, true, false)]
    [InlineData("y", false, false, true)]
    [InlineData("Y", false, false, true)]
    public void Modified_keys_never_decide(string text, bool ctrl, bool alt, bool shift) =>
        Assert.Null(Prompt().Decide(new KeyEvent(KeyKind.Character, text, ctrl, shift, alt)));

    [Fact]
    public void Ctrl_Enter_and_Ctrl_Escape_never_decide()
    {
        var prompt = Prompt();

        Assert.Null(prompt.Decide(new KeyEvent(KeyKind.Enter, null, true, false, false)));
        Assert.Null(prompt.Decide(new KeyEvent(KeyKind.Escape, null, true, false, false)));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("yes")]
    [InlineData("")]
    public void Other_character_keys_never_decide(string text) =>
        Assert.Null(Prompt().Decide(Char(text)));

    [Fact]
    public void Space_and_function_keys_never_decide()
    {
        var prompt = Prompt();

        Assert.Null(prompt.Decide(new KeyEvent(KeyKind.Space, null, false, false, false)));
        Assert.Null(prompt.Decide(new KeyEvent(KeyKind.F1, null, false, false, false)));
        Assert.Null(prompt.Decide(new KeyEvent(KeyKind.Tab, null, false, false, false)));
    }

    private static TrustPromptModel Prompt() =>
        new("Load project extension 'hello' 0.1.0 from /work", [], 1, 2);

    private static KeyEvent Char(string text) => new(KeyKind.Character, text, false, false, false);
}

public sealed class TrustPromptRendererTests
{
    [Fact]
    public void Null_model_is_rejected() =>
        Assert.Throws<ArgumentNullException>(() => new TrustPromptRenderer().Render(null!));

    [Fact]
    public void Prompt_names_the_question_and_the_two_choices() =>
        Assert.Equal(
            "Load project extension 'hello' 0.1.0 from /work?  [y]es  [n]o\n",
            Render(
                new TrustPromptModel("Load project extension 'hello' 0.1.0 from /work", [], 0, 0)
            )
        );

    [Fact]
    public void Several_pending_questions_carry_the_n_of_m_label() =>
        Assert.Equal(
            "Start 2 servers from project mcp.json '/w/mcp.json'?  1 of 2  [y]es  [n]o\n",
            Render(
                new TrustPromptModel(
                    "Start 2 servers from project mcp.json '/w/mcp.json'",
                    [],
                    1,
                    2
                )
            )
        );

    [Fact]
    public void A_single_question_has_no_label() =>
        Assert.Equal(
            "Start 1 server from project mcp.json '/w/mcp.json'?  [y]es  [n]o\n",
            Render(
                new TrustPromptModel("Start 1 server from project mcp.json '/w/mcp.json'", [], 1, 1)
            )
        );

    [Fact]
    public void Detail_lines_render_after_the_question() =>
        Assert.Equal(
            "Start 2 servers from project mcp.json '/w/mcp.json'?  [y]es  [n]o\n"
                + "test — dotnet exec server.dll\n"
                + "other — node other.js\n",
            Render(
                new TrustPromptModel(
                    "Start 2 servers from project mcp.json '/w/mcp.json'",
                    ["test — dotnet exec server.dll", "other — node other.js"],
                    0,
                    0
                )
            )
        );

    [Fact]
    public void Escaping_keeps_bracket_text_literal()
    {
        string output = Render(new TrustPromptModel("[red]Load[/] extension", ["[dim]x[/]"], 0, 0));

        Assert.Equal("[red]Load[/] extension?  [y]es  [n]o\n[dim]x[/]\n", output);
    }

    [Fact]
    public void Question_marker_is_yellow() =>
        Assert.Contains(
            "\u001b[38;5;11m?",
            RenderAnsi(new TrustPromptModel("Load extension", [], 0, 0))
        );

    [Fact]
    public void Plain_lines_carry_the_question_and_details_without_markup()
    {
        string[] lines =
        [
            .. TrustPromptRenderer.PlainLines(
                new TrustPromptModel("Load extension", ["test — dotnet exec server.dll"], 2, 2)
            ),
        ];

        Assert.Equal("Load extension?  2 of 2  [y]es  [n]o", lines[0]);
        Assert.Equal("test — dotnet exec server.dll", lines[1]);
    }

    [Fact]
    public void A_title_that_already_ends_with_a_question_mark_keeps_exactly_one()
    {
        string rendered = Render(
            new TrustPromptModel(
                "Trust the single-file extension 'acme-tools' 1.0.0 at '/w/acme.cs'?",
                [],
                0,
                0
            )
        );

        Assert.Equal(
            "Trust the single-file extension 'acme-tools' 1.0.0 at '/w/acme.cs'?  [y]es  [n]o\n",
            rendered
        );
        Assert.Equal(1, rendered.Count(character => character == '?'));
    }

    [Fact]
    public void Plain_lines_never_show_two_question_marks()
    {
        string[] lines =
        [
            .. TrustPromptRenderer.PlainLines(
                new TrustPromptModel("Trust the single-file extension 'acme' 1.0.0?", [], 0, 0)
            ),
        ];

        Assert.Equal("Trust the single-file extension 'acme' 1.0.0?  [y]es  [n]o", lines[0]);
    }

    private const string LongTitle =
        "Load project extension 'hello-world' 0.1.0 from /Users/danielwallner/dev/lunate/demo/extensions/hello-world";

    [Fact]
    public void A_long_title_wraps_at_118_columns_without_losing_text()
    {
        string[] lines =
        [
            .. TrustPromptRenderer.PlainLines(new TrustPromptModel(LongTitle, [], 0, 0), 118),
        ];

        Assert.True(lines.Length > 1);
        Assert.Equal(Question(LongTitle), string.Concat(lines[..^1]));
        Assert.Equal("  [y]es  [n]o", lines[^1]);
        Assert.DoesNotContain("\u2026", string.Concat(lines));
        Assert.All(lines, line => Assert.True(CellText.Width(line) <= 118));
    }

    [Fact]
    public void A_long_title_wraps_at_80_columns_without_clipping_the_choices()
    {
        string[] lines =
        [
            .. TrustPromptRenderer.PlainLines(new TrustPromptModel(LongTitle, [], 0, 0), 80),
        ];

        Assert.True(lines.Length > 1);
        Assert.Contains(Question(LongTitle), string.Concat(lines), StringComparison.Ordinal);
        Assert.Contains("[y]es  [n]o", string.Concat(lines), StringComparison.Ordinal);
        Assert.DoesNotContain("\u2026", string.Concat(lines));
        Assert.All(lines, line => Assert.True(CellText.Width(line) <= 80));
    }

    [Fact]
    public void The_deciding_path_segment_survives_every_width_from_sixty_to_one_hundred_forty()
    {
        const string Path = "/srv/trusted/../untrusted/ext";
        var prompt = new TrustPromptModel(
            "Load project extension 'hello-world' 0.1.0 from " + Path,
            [],
            0,
            0
        );

        for (var width = 60; width <= 140; width++)
        {
            string[] lines = [.. TrustPromptRenderer.PlainLines(prompt, width)];

            Assert.All(
                lines,
                line =>
                    Assert.True(CellText.Width(line) <= width, $"'{line}' exceeds {width} cells")
            );
            string joined = string.Concat(lines);
            Assert.Contains(Path, joined, StringComparison.Ordinal);
            Assert.Contains("untrusted", joined, StringComparison.Ordinal);
            Assert.Contains("[y]es  [n]o", joined, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(120)]
    public void Both_choices_render_at_the_issue_widths(int width)
    {
        string[] lines =
        [
            .. TrustPromptRenderer.PlainLines(new TrustPromptModel(LongTitle, [], 0, 0), width),
        ];

        string joined = string.Concat(lines);
        Assert.Contains(Question(LongTitle), joined, StringComparison.Ordinal);
        Assert.Contains("[y]es", joined, StringComparison.Ordinal);
        Assert.Contains("[n]o", joined, StringComparison.Ordinal);
        Assert.All(
            lines,
            line => Assert.True(CellText.Width(line) <= width, $"'{line}' exceeds {width} cells")
        );
    }

    [Fact]
    public void A_count_label_that_overflows_is_dropped_before_the_title()
    {
        string[] lines =
        [
            .. TrustPromptRenderer.PlainLines(new TrustPromptModel("Load extension", [], 2, 2), 30),
        ];

        Assert.Equal("Load extension?  [y]es  [n]o", Assert.Single(lines));
    }

    [Fact]
    public void A_very_narrow_width_puts_the_choices_on_their_own_line()
    {
        string[] lines =
        [
            .. TrustPromptRenderer.PlainLines(new TrustPromptModel(LongTitle, [], 0, 0), 20),
        ];

        Assert.True(lines.Length > 2);
        Assert.Equal(Question(LongTitle), string.Concat(lines[..^1]));
        Assert.Equal("  [y]es  [n]o", lines[^1]);
        Assert.DoesNotContain("\u2026", string.Concat(lines));
        Assert.All(lines, line => Assert.True(CellText.Width(line) <= 20));
    }

    [Fact]
    public void The_ascii_fallback_wraps_without_an_ellipsis()
    {
        string[] lines =
        [
            .. TrustPromptRenderer.PlainLines(
                new TrustPromptModel(LongTitle, [], 0, 0),
                118,
                unicode: false
            ),
        ];

        string joined = string.Concat(lines);
        Assert.DoesNotContain("...", joined, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2026", joined, StringComparison.Ordinal);
        Assert.Contains(Question(LongTitle), joined, StringComparison.Ordinal);
        Assert.Contains("[y]es  [n]o", joined, StringComparison.Ordinal);
        Assert.All(lines, line => Assert.True(CellText.Width(line) <= 118));
    }

    private static string Question(string title) => TrustPromptRenderer.Question(title);

    private static string Render(TrustPromptModel prompt)
    {
        var console = new TestConsole();
        console.Profile.Width = 120;
        console.Write(new TrustPromptRenderer().Render(prompt));
        return console.Output;
    }

    private static string RenderAnsi(TrustPromptModel prompt)
    {
        var console = new TestConsole();
        console.Profile.Width = 120;
        console.EmitAnsiSequences = true;
        console.Write(new TrustPromptRenderer().Render(prompt));
        return console.Output;
    }
}
