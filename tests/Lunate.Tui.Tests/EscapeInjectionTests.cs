namespace Lunate.Tui.Tests;

/// <summary>
/// The escape-sequence injection card (#447): a hostile tool result, a crafted trust-prompt detail
/// or pasted input must render inertly — no control byte the renderer did not put there reaches
/// the terminal. The scrubber keeps the printable remainder of a hostile sequence ("]52;c;…"),
/// so the assertions expect the visible leftovers too. Payload comparisons are ordinal: a
/// culture-sensitive search would itself mis-handle the control bytes.
/// </summary>
public sealed class EscapeInjectionTests
{
    private static readonly string[] Payloads = ["\u001b]52", "\u001b]8", "\u009b", "\u0004"];

    /// <summary>None of the hostile payloads survives into the rendered line.</summary>
    private static void CarriesNoPayload(string line) =>
        Assert.All(
            Payloads,
            payload => Assert.DoesNotContain(payload, line, StringComparison.Ordinal)
        );

    [Fact]
    public void A_hostile_streaming_tail_renders_inertly()
    {
        var state = new LiveAreaState
        {
            TailText = "\u001b]52;c;Q2xpY2JvYXJk\u0007line one\nplain line two",
            Size = new ConsoleSize(80, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Equal(
            [
                "]52;c;Q2xpY2JvYXJkline one",
                "plain line two",
                new string('─', 80),
                "> Ask anything, / for commands",
                new string('─', 80),
            ],
            frame.Lines
        );
        Assert.All(frame.Lines, CarriesNoPayload);
    }

    [Fact]
    public void A_hostile_thinking_stream_renders_inertly()
    {
        var state = new LiveAreaState
        {
            Thinking = new ThinkingState
            {
                Text = "\u009b2J\u009b31m\nplanned the diff",
                ElapsedSeconds = 3,
            },
            Size = new ConsoleSize(80, 12),
        };

        var frame = LiveAreaRenderer.Render(state);

        Assert.Contains(frame.Lines, line => line.Contains("planned the diff"));
        Assert.All(frame.Lines, CarriesNoPayload);
    }

    [Fact]
    public void A_hostile_pasted_input_renders_inertly()
    {
        var state = InputLine.Apply(
            InputLine.Empty,
            new KeyEvent(
                KeyKind.Paste,
                "\u001b]52;c;Q2xpY2JvYXJk\u0007pasted text\nsecond",
                false,
                false,
                false
            )
        );

        var layout = InputLine.Layout(state, 60, 8);

        // The ESC dropped; the printable remainder of the hostile paste survives.
        Assert.Equal(["> ]52;c;Q2xpY2JvYXJkpasted text", "  second"], layout.Lines);
        Assert.All(layout.Lines, CarriesNoPayload);
    }

    [Fact]
    public void A_hostile_input_line_renders_inertly_without_losing_newlines()
    {
        var state = InputLine.Apply(
            InputLine.Empty,
            new KeyEvent(KeyKind.Paste, "one\u009b2J\ntwo", false, false, false)
        );

        var layout = InputLine.Layout(state, 60, 8);

        // The C1 byte dropped; its printable remainder survives next to the text.
        Assert.Equal(["> one2J", "  two"], layout.Lines);
        Assert.All(layout.Lines, CarriesNoPayload);
    }

    [Fact]
    public void A_hostile_trust_prompt_detail_renders_inertly()
    {
        var lines = TrustPromptRenderer.PlainLines(
            new TrustPromptModel(
                "Trust packaged extension?",
                ["mcp.json: \u001b]52;c;AAA\u0007\ttrusted"],
                1,
                1
            ),
            120
        );

        // The tab expanded, the OSC 52 bytes dropped, the remainder stays readable.
        Assert.Equal(
            ["Trust packaged extension?  [y]es  [n]o", "mcp.json: ]52;c;AAA trusted"],
            lines
        );
        Assert.All(lines, CarriesNoPayload);
    }
}
