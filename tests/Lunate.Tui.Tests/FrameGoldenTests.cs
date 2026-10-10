using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

public sealed class FrameGoldenTests
{
    // Frame goldens are plain committed files under tests/Lunate.Tui.Tests/fixtures/frames/:
    // one line per IConsoleIO.Write call with escapes encoded (ESC written as \x1b,
    // CR/LF/TAB as \r/\n/\t, and backslashes doubled). Regenerate deliberately with
    //   LUNATE_TUI_UPDATE_GOLDENS=1 dotnet test --project tests/Lunate.Tui.Tests
    // then review the diff before committing.
    private static string FramesDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "Lunate.Tui.Tests", "fixtures", "frames");

    [Fact]
    public void Scripted_session_frames_match_the_committed_golden()
    {
        string[] actual = RunScriptedSession().Select(Escape).ToArray();
        GoldenFiles.AssertMatchesLines(
            Path.Combine(FramesDirectory, "scripted-session.txt"),
            actual
        );
    }

    private static IReadOnlyList<string> RunScriptedSession()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler, new ConsoleSize(40, 12));
        var area = new LiveArea(console, scheduler);
        area.Start();
        area.SetFooter(new StatusFooterModel("test-model", 1540, 12320, "/repo", "main"));
        area.SetTool("read src/foo.cs");

        Type(area, "hey");
        Advance(scheduler, 33);

        area.PostKey(new KeyEvent(KeyKind.Paste, "a\nb\nc", false, false, false));
        Advance(scheduler, 33);

        area.PostKey(new KeyEvent(KeyKind.Backspace, null, false, false, false));
        area.AppendTail("tail one\ntail two");
        Advance(scheduler, 120);

        console.PushResize(new ConsoleSize(12, 8));
        Advance(scheduler, 33);

        area.SetTool(null);
        Advance(scheduler, 33);

        area.Dispose();
        Advance(scheduler, 1);
        return console.Writes;
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("\u001b", "\\x1b")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");

    private static void Type(LiveArea area, string text)
    {
        foreach (char c in text)
        {
            area.PostKey(new KeyEvent(KeyKind.Character, c.ToString(), false, false, false));
        }
    }

    private static void Advance(TestScheduler scheduler, int milliseconds) =>
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(milliseconds).Ticks);
}
