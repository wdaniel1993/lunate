using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>Ctrl+G</c> end to end over a scripted child (a shell script on POSIX, a <c>.cmd</c> on
/// Windows), never a real editor: what the draft file contains, what comes back, what a failure
/// does to the draft, cursor and stash, and who owns the terminal while the child runs.
/// </summary>
public sealed class InteractiveSessionEditorTests
{
    private static readonly TimeSpan SpawnWindow = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_multi_line_draft_round_trips_and_the_cursor_lands_at_the_end()
    {
        using var editor = new ScriptedEditor();
        using var host = editor.Host();
        Task run = host.RunAsync();

        host.Console.SendText("line one");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "line one");
        for (var i = 0; i < 4; i++)
        {
            host.Console.Send(new KeyEvent(KeyKind.Left, null, false, false, false));
        }

        await host.WaitUntilAsync(() => host.Session.Input.CursorPosition == 4);
        host.Console.SendCtrl("g");
        await editor.WaitForRunAsync(host, SpawnWindow);
        await host.WaitForFrameAsync("line two", SpawnWindow);

        Assert.Equal("line one\nline two", host.Session.Input.Text);
        Assert.Equal(host.Session.Input.Text.Length, host.Session.Input.CursorPosition);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task An_empty_draft_opens_an_empty_buffer()
    {
        using var editor = new ScriptedEditor();
        using var host = editor.Host();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendCtrl("g");
        await editor.WaitForRunAsync(host, SpawnWindow);
        await host.WaitForFrameAsync("line two", SpawnWindow);

        Assert.Equal(string.Empty, editor.BeforeFileContent());
        Assert.Equal("line one\nline two", host.Session.Input.Text);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task An_empty_buffer_leaves_the_draft_empty()
    {
        using var editor = new ScriptedEditor(content: string.Empty);
        using var host = editor.Host();
        Task run = host.RunAsync();

        host.Console.SendText("erase me");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "erase me");
        host.Console.SendCtrl("g");
        await editor.WaitForRunAsync(host, SpawnWindow);
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0, SpawnWindow);

        Assert.Equal(string.Empty, host.Session.Input.Text);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_missing_editor_binary_leaves_the_draft_alone_and_names_the_variable()
    {
        using var editor = new ScriptedEditor(visual: "no-such-editor-anywhere");
        using var host = editor.Host();
        Task run = host.RunAsync();

        host.Console.SendText("draft text");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "draft text");
        host.Console.Send(new KeyEvent(KeyKind.Left, null, false, false, false));
        await host.WaitUntilAsync(() => host.Session.Input.CursorPosition == 9);

        host.Console.SendCtrl("g");
        await host.WaitForFrameAsync("no such program", SpawnWindow);

        Assert.Contains(
            "VISUAL=\"no-such-editor-anywhere\"",
            host.Frames,
            StringComparison.Ordinal
        );
        Assert.Equal("draft text", host.Session.Input.Text);
        Assert.Equal(9, host.Session.Input.CursorPosition);
        Assert.Empty(editor.TempFiles());
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_non_zero_exit_leaves_the_draft_alone_and_the_file_gone()
    {
        using var editor = new ScriptedEditor(exitCode: 1);
        using var host = editor.Host();
        Task run = host.RunAsync();

        host.Console.SendText("draft text");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "draft text");
        host.Console.SendCtrl("g");
        await editor.WaitForRunAsync(host, SpawnWindow);
        await host.WaitForFrameAsync("editor exited 1", SpawnWindow);

        Assert.Equal("draft text", host.Session.Input.Text);
        Assert.False(File.Exists(editor.RecordedDraftPath()), "the temp file outlived the run");
        Assert.Empty(editor.TempFiles());
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task A_start_that_throws_posts_one_notice_and_leaves_no_file()
    {
        using var editor = new ScriptedEditor(startFails: true);
        using var host = editor.Host();
        Task run = host.RunAsync();

        host.Console.SendText("draft text");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "draft text");
        host.Console.SendCtrl("g");
        await host.WaitForFrameAsync("could not start", SpawnWindow);

        Assert.Equal("draft text", host.Session.Input.Text);
        Assert.Empty(editor.TempFiles());
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_child_owns_the_terminal_and_no_key_reaches_the_session()
    {
        using var editor = new ScriptedEditor(waitForRelease: true);
        using var host = editor.Host();
        Task run = host.RunAsync();

        host.Console.SendCtrl("g");
        await editor.WaitForStartAsync(SpawnWindow);

        // The handover: cooked for the child, and nothing of ours reads while it holds stdin.
        Assert.True(host.Console.IsTerminalSuspended);
        Assert.False(host.Console.InRawMode);

        host.Console.SendText("zzz");
        Assert.Equal(["z", "z", "z"], host.Console.TypedAtChild);

        editor.Release();
        await host.WaitForFrameAsync("line two", SpawnWindow);

        Assert.Equal("line one\nline two", host.Session.Input.Text);
        Assert.DoesNotContain("zzz", host.Session.Input.Text, StringComparison.Ordinal);
        Assert.True(host.Console.InRawMode);
        Assert.False(host.Console.IsTerminalSuspended);
        host.Console.Complete();
        await run;
    }

    [Fact]
    public async Task The_editors_text_wins_and_the_changed_draft_goes_to_the_stash()
    {
        using var editor = new ScriptedEditor(waitForRelease: true);
        using var host = editor.Host();
        Task run = host.RunAsync();
        await host.WaitUntilAsync(() => host.Session.IsComposed);

        host.Console.SendCtrl("g");
        await editor.WaitForStartAsync(SpawnWindow);

        // A programmatic write while the child runs: a queue take-back, an extension, a future
        // background write — the case the rule exists for.
        await host.Session.HandleKeyAsync(
            new KeyEvent(KeyKind.Character, "x", false, false, false),
            999,
            TestContext.Current.CancellationToken
        );
        Assert.Equal("x", host.Session.Input.Text);

        editor.Release();
        await host.WaitForFrameAsync("the editor's text wins", SpawnWindow);

        Assert.Equal("line one\nline two", host.Session.Input.Text);

        // The earlier text is in the slot Ctrl+S already ships: clear the prompt, then restore it.
        host.Console.SendCtrl("u");
        await host.WaitUntilAsync(() => host.Session.Input.Text.Length == 0);
        host.Console.SendCtrl("s");
        await host.WaitUntilAsync(() => host.Session.Input.Text == "x");

        Assert.Equal("x", host.Session.Input.Text);
        host.Console.Complete();
        await run;
    }

    /// <summary>
    /// A scripted editor: records the draft path it was handed (and the file's contents at that
    /// moment), optionally waits for a release file, then writes <c>content</c> and exits.
    /// </summary>
    private sealed class ScriptedEditor : IDisposable
    {
        private readonly bool _windows = OperatingSystem.IsWindows();
        private readonly string _content;

        public ScriptedEditor(
            string? visual = null,
            string content = "line one\nline two\n",
            int exitCode = 0,
            bool waitForRelease = false,
            bool startFails = false
        )
        {
            _content = content;
            Temp = new TempDirectory();
            string script = Temp.File(_windows ? "editor.cmd" : "editor.sh");
            Record = Temp.File("recorded-path");
            Before = Temp.File("draft-before");
            ReleaseFile = Temp.File("release");
            DraftDirectory = Temp.File("drafts");
            Directory.CreateDirectory(DraftDirectory);

            if (startFails)
            {
                // A regular file with no execute bit (and no .exe on Windows): found, cannot start.
                File.WriteAllText(script, string.Empty);
                Visual = script;
            }
            else
            {
                File.WriteAllText(script, Body(exitCode, waitForRelease));
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        script,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    );
                }

                Visual = visual ?? (_windows ? $"cmd.exe /c {script}" : $"/bin/sh {script}");
            }
        }

        public TempDirectory Temp { get; }

        public string Visual { get; }

        public string Record { get; }

        public string Before { get; }

        public string ReleaseFile { get; }

        public string DraftDirectory { get; }

        public InteractiveSessionHost Host() =>
            new(
                environment: name =>
                    string.Equals(name, "VISUAL", StringComparison.Ordinal) ? Visual
                    : string.Equals(name, "EDITOR", StringComparison.Ordinal) ? null
                    // A bare program name is resolved through PATH, so the fake environment has to
                    // carry PATH and PATHEXT the way a user's machine does; without them `cmd.exe`
                    // is reported as a program that does not exist (the POSIX fixture never noticed
                    // because it names /bin/sh by absolute path).
                    : name is "PATH" or "PATHEXT" ? System.Environment.GetEnvironmentVariable(name)
                    : PrintModeTestSupport.Environment()(name),
                editorTempDirectory: DraftDirectory
            );

        public string RecordedDraftPath() =>
            File.Exists(Record) ? File.ReadAllText(Record).Trim() : Temp.File("absent");

        public string BeforeFileContent() =>
            File.Exists(Before) ? File.ReadAllText(Before) : "<the editor never ran>";

        public IReadOnlyList<string> TempFiles() => Directory.GetFiles(DraftDirectory);

        public void Release() => File.WriteAllText(ReleaseFile, string.Empty);

        public Task WaitForStartAsync(TimeSpan timeout) =>
            WaitForAsync(() => File.Exists(Record), timeout, "the editor never started");

        public async Task WaitForRunAsync(InteractiveSessionHost host, TimeSpan timeout)
        {
            await WaitForStartAsync(timeout);
            // The run is over when the draft path is deleted again: the session's finally.
            string path = RecordedDraftPath();
            await host.WaitUntilAsync(() => !File.Exists(path), timeout);
        }

        public void Dispose() => Temp.Dispose();

        private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout, string what)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException($"{what} within {timeout.TotalSeconds:0} seconds.");
                }

                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
        }

        private string Body(int exitCode, bool waitForRelease)
        {
            if (_windows)
            {
                return string.Concat(
                    "@echo off\n",
                    $"copy /Y \"%~1\" \"{Before}\" >nul\n",
                    $"> \"{Record}\" echo %~1\n",
                    waitForRelease
                        ? $":wait\nif not exist \"{ReleaseFile}\" goto wait\n"
                        : string.Empty,
                    ContentLines(),
                    $"exit /b {exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n"
                );
            }

            return string.Concat(
                "#!/bin/sh\n",
                $"cp \"$1\" \"{Before}\"\n",
                $"printf '%s' \"$1\" > \"{Record}\"\n",
                waitForRelease
                    ? $"while [ ! -f \"{ReleaseFile}\" ]; do sleep 0.02; done\n"
                    : string.Empty,
                ContentLines(),
                $"exit {exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n"
            );
        }

        private string ContentLines()
        {
            string[] lines = _content.Split('\n');
            if (_windows)
            {
                string[] written = [.. lines.Where(line => line.Length > 0)];
                return written.Length == 0
                    ? "break > \"%~1\"\n"
                    : $"(echo {string.Join("& echo ", written)}) > \"%~1\"\n";
            }

            var builder = new System.Text.StringBuilder("printf '");
            foreach (string line in lines)
            {
                builder.Append(line.Replace("'", "'\\''", StringComparison.Ordinal));
                if (line.Length > 0)
                {
                    builder.Append("\\n");
                }
            }

            return builder.Append("' > \"$1\"\n").ToString();
        }
    }
}
