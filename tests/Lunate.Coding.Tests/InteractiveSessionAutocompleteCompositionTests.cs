using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Lunate.Agent;
using Lunate.Tui;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-composition guard for autocomplete: the real host paints the input box, typing
/// <c>/</c> opens the command rows, the arrow moves the selection, Tab accepts the second row and
/// Enter submits it; the <c>@</c> flow opens the lazy index's file rows and acceptance keeps the
/// <c>@</c>. The model turn is a fixed script parked by the provider-stream hook, so the observed
/// frames do not depend on run timing; frames are compared in the canonical form (lines plus
/// cursor) that ignores repaint carry-over from the previous frame's height.
/// </summary>
public sealed class InteractiveSessionAutocompleteCompositionTests
{
    private static readonly KeyEvent Down = new(KeyKind.Down, null, false, false, false);

    [Fact]
    public async Task The_host_opens_the_command_list_accepts_the_second_row_and_submits_it()
    {
        using var host = new InteractiveSessionHost();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("/");
        await host.WaitUntilAsync(() => host.Session.Autocomplete is { Items.Count: 8 });
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "  /new");

        string open = await CaptureFrameAsync(host);
        Assert.Contains("> /mcp", open, StringComparison.Ordinal);
        Assert.Contains("  /new  start a fresh session", open, StringComparison.Ordinal);

        host.Console.Send(Down);
        await host.WaitUntilAsync(() => host.Session.Autocomplete?.SelectedIndex == 1);
        await host.WaitUntilAsync(() => host.Session.Autocomplete?.Selected?.Text == "/new");
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "> /new");
        string selected = await CaptureFrameAsync(host);
        Assert.Contains("> /new  start a fresh session", selected, StringComparison.Ordinal);

        host.Console.Send(new KeyEvent(KeyKind.Tab, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Input.Text == "/new ");
        Assert.Null(host.Session.Autocomplete);

        host.Console.SendEnter();
        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "new session");
        Assert.False(host.Session.IsRunning);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public async Task The_host_opens_the_file_list_and_accepting_keeps_the_at_sign()
    {
        var gate = new FinalTextGate();
        using var host = new InteractiveSessionHost(
            workspaceFiles: Tree(),
            configureHarness: options => options with { Hooks = gate }
        );
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendText("@");
        AutocompleteState state = await WaitForFileListAsync(host);
        Assert.Equal(CompletionMode.File, state.Mode);
        Assert.Equal(0, state.TokenStart);
        Assert.Contains(state.Items, item => item.Text == "src/main.cs" && item.Muted == "src/");

        await InteractiveSessionCommandTests.WaitForFrameAsync(host, "  src/main.cs");
        string open = await CaptureFrameAsync(host);
        Assert.Contains("  docs/", open, StringComparison.Ordinal);
        Assert.Contains("> docs/", open, StringComparison.Ordinal);
        Assert.Contains("  docs/guide.md", open, StringComparison.Ordinal);

        host.Console.Send(new KeyEvent(KeyKind.Tab, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length > 1);
        Assert.Equal("@" + state.Selected!.Text, host.Session.Input.Text);
        Assert.StartsWith("@", host.Session.Input.Text, StringComparison.Ordinal);
        Assert.Null(host.Session.Autocomplete);

        host.Client.Enqueue(Scripts.Text("indexed"), Scripts.Stop());
        host.Console.SendEnter();
        await gate.Reached.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        Assert.True(host.Session.IsRunning);
        _ = await CaptureFrameAsync(host);
        gate.Release();

        await host.WaitUntilAsync(() =>
            !host.Session.IsRunning
            && host.ScrollbackText.Contains("indexed", StringComparison.Ordinal)
        );
        Assert.Empty(EventSequenceValidator.Validate(host.Events));

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    private static FakeWorkspaceFiles Tree() =>
        new FakeWorkspaceFiles()
            .AddDirectory("docs")
            .AddDirectory("src")
            .AddDirectory("src/core")
            .AddFile("docs/guide.md", "# guide")
            .AddFile("src/main.cs", "class Program { }")
            .AddFile("src/core/File.cs", "class File { }");

    /// <summary>Nudges the cursor (a no-op at the end) until the lazily built index publishes.</summary>
    private static async Task<AutocompleteState> WaitForFileListAsync(InteractiveSessionHost host)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            host.Console.Send(new KeyEvent(KeyKind.Right, null, false, false, false));
            await Task.Delay(10, TestContext.Current.CancellationToken);
            if (host.Session.Autocomplete is { } state)
            {
                return state;
            }
        }

        throw new TimeoutException("the file list never opened");
    }

    /// <summary>
    /// Waits for the live frame to stop changing and returns it in a history-free canonical
    /// form: the written lines and the cursor, without the carry-over move and trailing clears
    /// that depend on the previous frame's height and therefore on run timing.
    /// </summary>
    private static async Task<string> CaptureFrameAsync(InteractiveSessionHost host)
    {
        string previous = string.Empty;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            host.Advance(33);
            await Task.Delay(1, TestContext.Current.CancellationToken);
            string current = Escape(CanonicalFrame(host.LastFrame));
            if (attempt > 0 && string.Equals(current, previous, StringComparison.Ordinal))
            {
                return current;
            }

            previous = current;
        }

        return previous;
    }

    private static string CanonicalFrame(string write)
    {
        string[] rows = write.Split("\r\u001b[2K");
        if (rows.Length == 1)
        {
            return write;
        }

        string last = rows[^1];
        var column = 0;
        Match right = Regex.Match(last, "\\r(?:\\x1b\\[(\\d+)C)?$");
        if (right.Success)
        {
            if (right.Groups[1].Success)
            {
                column = int.Parse(right.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            last = last[..right.Index];
        }

        var up = 0;
        Match upper = Regex.Match(last, "\\x1b\\[(\\d+)A$");
        if (upper.Success)
        {
            up = int.Parse(upper.Groups[1].Value, CultureInfo.InvariantCulture);
            last = last[..upper.Index];
        }

        var lines = new List<string>(rows.Length - 1);
        for (var index = 1; index < rows.Length; index++)
        {
            string text = index == rows.Length - 1 ? last : rows[index];
            lines.Add(text.EndsWith('\n') ? text[..^1] : text);
        }

        int cursorRow = lines.Count - up;
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var builder = new StringBuilder();
        foreach (string line in lines)
        {
            builder.Append("\r\u001b[2K").Append(line).Append('\n');
        }

        int trail = lines.Count - cursorRow;
        if (trail > 0)
        {
            builder.Append("\u001b[").Append(trail).Append('A');
        }

        builder.Append('\r');
        if (column > 0)
        {
            builder.Append("\u001b[").Append(column).Append('C');
        }

        return builder.ToString();
    }

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\")
            .Replace("\u001b", "\\x1b")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

    /// <summary>Parks the provider stream on its final text until the test releases it.</summary>
    private sealed class FinalTextGate : IAgentHookPoints
    {
        private readonly TaskCompletionSource _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public TaskCompletionSource Reached { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ProviderStreamEventAsync(
            string runId,
            string? parentRunId,
            ChatResponseUpdate update,
            CancellationToken cancellationToken
        )
        {
            if (update.Contents.OfType<TextContent>().Any(content => content.Text == "indexed"))
            {
                Reached.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }
        }

        public void Release() => _release.TrySetResult();
    }
}
