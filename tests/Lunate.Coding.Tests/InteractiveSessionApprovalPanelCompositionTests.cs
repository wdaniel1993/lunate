using System.Globalization;
using System.Text.RegularExpressions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-composition guard for the approval panel: at 40 columns a real edit approval
/// names the path and change counts, shows the diff hunk and every choice without clipping, and
/// <c>a</c> both approves the edit and remembers the tool for the session, so the next edit runs
/// without a prompt.
/// </summary>
public sealed class InteractiveSessionApprovalPanelCompositionTests
{
    private const string Minus = "\u2212";
    private const string Warning = "\u26a0";

    private static readonly Regex Sgr = new("\u001b\\[[0-9;]*m", RegexOptions.Compiled);
    private static readonly Regex Csi = new("\u001b\\[[0-9;?]*[ -/]*[@-~]", RegexOptions.Compiled);

    [Fact]
    public async Task An_edit_approval_shows_the_diff_and_always_lets_the_next_edit_through()
    {
        using var host = new InteractiveSessionHost();
        host.Console.Size = new ConsoleSize(40, 20);
        File.WriteAllText(host.Temp.File("notes.txt"), "old\n");
        File.WriteAllText(host.Temp.File("other.txt"), "alpha\n");

        host.Client.Enqueue(
            Scripts.Call(
                "call-1",
                "edit",
                Scripts.Args(("path", "notes.txt"), ("old_text", "old\n"), ("new_text", "new\n"))
            ),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(
            Scripts.Call(
                "call-2",
                "edit",
                Scripts.Args(("path", "other.txt"), ("old_text", "alpha\n"), ("new_text", "beta\n"))
            ),
            Scripts.ToolCalls()
        );
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());

        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("go");
        host.Console.SendEnter();
        await host.WaitForApprovalAsync();

        string frame = await CaptureFrameAsync(host);

        Assert.Contains($"{Warning} edit  notes.txt +1 {Minus}1", frame, StringComparison.Ordinal);
        Assert.Contains("@@ -1,1 +1,1 @@", frame, StringComparison.Ordinal);
        Assert.Contains("[y]es  > [n]o  [a]lways this session", frame, StringComparison.Ordinal);

        string[] lines = FrameLines(frame);
        for (var index = 0; index < lines.Length; index++)
        {
            Assert.True(
                CellText.Width(lines[index]) <= 40,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"line {index + 1} is {CellText.Width(lines[index])} cells wide: '{lines[index]}'"
                )
            );
        }

        host.Console.SendText("a");

        // Reaching the third model call proves the second edit ran without a prompt; the file
        // contents prove both edits landed, and the final text proves the turn finished.
        await host
            .Client.WaitForCallAsync(3)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await host.WaitForScrollbackAsync(
            text => text.Contains("done", StringComparison.Ordinal) && !host.Session.IsRunning,
            "the answer once the turn finishes"
        );

        Assert.Null(host.Session.PendingApproval);
        Assert.Equal("new\n", File.ReadAllText(host.Temp.File("notes.txt")));
        Assert.Equal("beta\n", File.ReadAllText(host.Temp.File("other.txt")));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    /// <summary>
    /// Advances the live-area clock until the painted frame carries the whole approval panel,
    /// then returns it with ANSI SGR styling removed so the accent-toned key letters do not split
    /// the choices.
    /// </summary>
    private static async Task<string> CaptureFrameAsync(InteractiveSessionHost host)
    {
        string frame = string.Empty;
        await host.WaitUntilAsync(() =>
        {
            host.Advance(33);
            frame = Sgr.Replace(host.LastFrame, string.Empty);
            return frame.Contains("@@ -1,1 +1,1 @@", StringComparison.Ordinal)
                && frame.Contains("[y]es  > [n]o  [a]lways this session", StringComparison.Ordinal);
        });
        return frame;
    }

    /// <summary>
    /// The painted content rows of a frame: FrameWriter prefixes every row with a carriage return
    /// and a clear-line CSI and ends with a cursor move, neither of which is part of the text.
    /// </summary>
    private static string[] FrameLines(string frame)
    {
        string[] rows = frame.Split("\r\u001b[2K", StringSplitOptions.None);
        if (rows.Length == 1)
        {
            return [Csi.Replace(frame, string.Empty).Trim()];
        }

        var lines = new List<string>(Math.Max(0, rows.Length - 2));
        for (var index = 1; index < rows.Length - 1; index++)
        {
            string line = rows[index];
            if (line.EndsWith('\n'))
            {
                line = line[..^1];
            }

            lines.Add(Csi.Replace(line.Replace("\r", string.Empty), string.Empty));
        }

        return [.. lines];
    }
}
