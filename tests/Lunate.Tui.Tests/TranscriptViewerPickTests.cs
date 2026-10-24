using System.Text;
using Spectre.Console;

namespace Lunate.Tui.Tests;

/// <summary>
/// The viewer's pick mode — <c>/tree</c>'s surface: the same rendering as the transcript viewer, the
/// selected entry marked, <c>j</c>/<c>k</c> between pickable rows, <c>/</c> filtering and Enter
/// returning the picked entry without touching anything else.
/// </summary>
public sealed class TranscriptViewerPickTests
{
    private static readonly ConsoleSize Size = new(40, 10);

    [Fact]
    public void The_first_pickable_entry_starts_marked_and_the_footer_carries_the_position()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: true);

        viewer.Pick(Tree(), [0, 2]);

        Assert.True(viewer.IsPicking);
        string frame = Frame(console);
        Assert.Contains("\u25b8 \u00bb one", frame, StringComparison.Ordinal);
        Assert.Contains("1 of 2", frame, StringComparison.Ordinal);
        Assert.Contains("j/k move", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void J_and_k_move_the_marker_between_pickable_rows()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: true);
        viewer.Pick(Tree(), [0, 2]);

        Assert.Equal(PickOutcome.None, viewer.HandlePickKey(Key("j")));
        Assert.Contains("\u25b8 \u00bb three", Frame(console), StringComparison.Ordinal);
        Assert.Contains("2 of 2", Frame(console), StringComparison.Ordinal);

        Assert.Equal(PickOutcome.None, viewer.HandlePickKey(Key("k")));
        Assert.Contains("\u25b8 \u00bb one", Frame(console), StringComparison.Ordinal);
    }

    [Fact]
    public void The_selection_clamps_at_both_ends()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: true);
        viewer.Pick(Tree(), [0, 2]);

        for (var index = 0; index < 5; index++)
        {
            viewer.HandlePickKey(Key("j"));
        }

        Assert.Contains("2 of 2", Frame(console), StringComparison.Ordinal);
        Assert.Contains("\u25b8 \u00bb three", Frame(console), StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_picks_the_selected_entry_and_leaves_the_alternate_screen()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: true);
        viewer.Pick(Tree(), [0, 2]);
        viewer.HandlePickKey(Key("j"));

        Assert.Equal(PickOutcome.Picked, viewer.HandlePickKey(Enter()));

        Assert.Equal(2, viewer.PickedEntryIndex);
        Assert.False(console.InAlternateScreen);
        Assert.False(viewer.IsOpen);
        Assert.Equal("\u001b[?1049l", console.Writes[^1]);
    }

    [Fact]
    public void A_filter_narrows_the_rows_and_resets_the_selection()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: true);
        viewer.Pick(Tree(), [0, 2]);
        viewer.HandlePickKey(Key("j"));

        viewer.HandlePickKey(Key("/"));
        foreach (char character in "thr")
        {
            viewer.HandlePickKey(Key(character.ToString()));
        }

        string frame = Frame(console);
        Assert.Contains("\u25b8 \u00bb three", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("one", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("two", frame, StringComparison.Ordinal);
        Assert.Contains("1 of 1", frame, StringComparison.Ordinal);
        Assert.Contains("enter confirm", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void Enter_while_filtering_picks_the_only_match()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: true);
        viewer.Pick(Tree(), [0, 2]);
        viewer.HandlePickKey(Key("/"));
        foreach (char character in "thr")
        {
            viewer.HandlePickKey(Key(character.ToString()));
        }

        // The assistant paragraph is context, not a pickable row: "two" never matches a pick.
        Assert.Equal(PickOutcome.Picked, viewer.HandlePickKey(Enter()));

        Assert.Equal(2, viewer.PickedEntryIndex);
    }

    [Fact]
    public void Escape_clears_the_filter_before_it_closes_the_picker()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: true);
        viewer.Pick(Tree(), [0, 2]);
        viewer.HandlePickKey(Key("/"));
        viewer.HandlePickKey(Key("t"));

        Assert.Equal(PickOutcome.None, viewer.HandlePickKey(Escape()));
        Assert.True(viewer.IsOpen);
        Assert.Contains("\u00bb one", Frame(console), StringComparison.Ordinal);

        Assert.Equal(PickOutcome.Closed, viewer.HandlePickKey(Escape()));
        Assert.False(console.InAlternateScreen);
        Assert.Null(viewer.PickedEntryIndex);
    }

    [Fact]
    public void Q_closes_without_picking_and_the_ascii_fallback_has_no_alt_bindings()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: false);
        viewer.Pick(Tree(), [0, 2]);

        Assert.Contains("> > one", Frame(console), StringComparison.Ordinal);

        Assert.Equal(PickOutcome.Closed, viewer.HandlePickKey(Key("q")));
        Assert.Null(viewer.PickedEntryIndex);
    }

    [Fact]
    public void A_pickable_list_with_no_rows_still_opens_and_enter_does_nothing()
    {
        using var console = new FakeConsoleIO(Size);
        TranscriptViewer viewer = Viewer(console, unicode: true);

        viewer.Pick(Tree(), []);

        Assert.True(console.InAlternateScreen);
        Assert.Equal(PickOutcome.None, viewer.HandlePickKey(Enter()));
        Assert.Contains("0 of 0", Frame(console), StringComparison.Ordinal);
    }

    private static TranscriptViewer Viewer(FakeConsoleIO console, bool unicode) =>
        new(console, new TerminalCapabilities(ColorSystemSupport.NoColors, unicode));

    /// <summary>One user message, one assistant paragraph, one user message: two pickable rows.</summary>
    private static TranscriptModel Tree() =>
        new([
            new TranscriptUserEntry("one"),
            new TranscriptAssistantEntry("two"),
            new TranscriptUserEntry("three"),
        ]);

    private static KeyEvent Key(string text) =>
        new(KeyKind.Character, text, Ctrl: false, Shift: false, Alt: false);

    private static KeyEvent Enter() =>
        new(KeyKind.Enter, null, Ctrl: false, Shift: false, Alt: false);

    private static KeyEvent Escape() =>
        new(KeyKind.Escape, null, Ctrl: false, Shift: false, Alt: false);

    /// <summary>The last painted frame as plain text.</summary>
    private static string Frame(FakeConsoleIO console)
    {
        string frame = console.Writes.Count == 0 ? string.Empty : console.Writes[^1];
        var plain = new StringBuilder();
        for (var index = 0; index < frame.Length; index++)
        {
            if (frame[index] != '\u001b')
            {
                plain.Append(frame[index]);
                continue;
            }

            // A CSI sequence is ESC '[' then parameter bytes until a final byte (0x40-0x7E).
            int at = index + 1;
            if (at < frame.Length && frame[at] == '[')
            {
                at++;
                while (at < frame.Length && (frame[at] < 0x40 || frame[at] > 0x7E))
                {
                    at++;
                }
            }

            index = at;
        }

        return plain.ToString();
    }
}
