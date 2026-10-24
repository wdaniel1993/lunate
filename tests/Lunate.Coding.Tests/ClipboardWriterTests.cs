using System.Text;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The clipboard transport decision (design decisions 2 and 3): where the session runs picks the
/// route, the route picks the wording, and neither the text nor the size is ever cut by Lunate.
/// </summary>
public sealed class ClipboardWriterTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Over_ssh_only_the_terminal_sequence_is_written_and_no_tool_starts()
    {
        var tool = new FakeTool();
        var terminal = new List<string>();
        var writer = new ClipboardWriter(
            Environment.Of(("SSH_CONNECTION", "10.0.0.2 51234 10.0.0.1 22")),
            tool,
            writeTerminal: terminal.Add,
            platform: new ClipboardPlatform(Remote: true, Tmux: false, ClipboardFamily.MacOs)
        );

        ClipboardCopy copy = await writer.WriteAsync("the answer", Ct);

        Assert.Equal(ClipboardRoute.TerminalSequence, copy.Route);
        Assert.Equal("OSC 52", copy.Tool);
        Assert.Empty(tool.Requests);
        string sequence = Assert.Single(terminal);
        Assert.Equal("the answer", Decode(sequence));
    }

    [Fact]
    public async Task A_local_session_prefers_the_platform_tool_and_writes_no_sequence()
    {
        var tool = new FakeTool();
        var terminal = new List<string>();
        var writer = new ClipboardWriter(
            Environment.Of(),
            tool,
            writeTerminal: terminal.Add,
            platform: new ClipboardPlatform(Remote: false, Tmux: false, ClipboardFamily.MacOs)
        );

        ClipboardCopy copy = await writer.WriteAsync("the answer", Ct);

        Assert.Equal(ClipboardRoute.PlatformTool, copy.Route);
        Assert.Equal("pbcopy", copy.Tool);
        Assert.Equal("the answer", Assert.Single(tool.Requests).Text);
        Assert.Equal("pbcopy", tool.Requests[0].Executable);
        Assert.Empty(terminal);
    }

    [Fact]
    public async Task A_platform_tool_that_fails_falls_back_to_the_terminal_sequence()
    {
        var tool = new FakeTool(succeeds: false);
        var terminal = new List<string>();
        var writer = new ClipboardWriter(
            Environment.Of(),
            tool,
            writeTerminal: terminal.Add,
            platform: new ClipboardPlatform(Remote: false, Tmux: false, ClipboardFamily.MacOs)
        );

        ClipboardCopy copy = await writer.WriteAsync("the answer", Ct);

        Assert.Equal(ClipboardRoute.TerminalSequence, copy.Route);
        Assert.Equal("the answer", Decode(Assert.Single(terminal)));
    }

    [Fact]
    public async Task Linux_tries_wl_copy_then_xclip_then_xsel()
    {
        var tool = new FakeTool(succeeds: false);
        var writer = new ClipboardWriter(
            Environment.Of(),
            tool,
            writeTerminal: _ => { },
            platform: new ClipboardPlatform(Remote: false, Tmux: false, ClipboardFamily.Linux)
        );

        await writer.WriteAsync("text", Ct);

        Assert.Equal(
            ["wl-copy", "xclip", "xsel"],
            tool.Requests.Select(request => request.Executable)
        );
        Assert.Equal(["-selection", "clipboard"], tool.Requests[1].Arguments);
        Assert.Equal(["--clipboard", "--input"], tool.Requests[2].Arguments);
    }

    [Fact]
    public async Task Tmux_is_reported_so_the_notice_can_name_the_setting()
    {
        var writer = new ClipboardWriter(
            Environment.Of(),
            new FakeTool(succeeds: false),
            writeTerminal: _ => { },
            platform: new ClipboardPlatform(Remote: false, Tmux: true, ClipboardFamily.Linux)
        );

        ClipboardCopy copy = await writer.WriteAsync("text", Ct);

        Assert.True(copy.Tmux);
        Assert.Equal(ClipboardRoute.TerminalSequence, copy.Route);
    }

    [Fact]
    public async Task An_answer_over_100_KB_is_flagged_and_sent_whole()
    {
        string text = new('x', 140 * 1024);
        var terminal = new List<string>();
        var writer = new ClipboardWriter(
            Environment.Of(("SSH_TTY", "/dev/pts/3")),
            new FakeTool(),
            writeTerminal: terminal.Add,
            platform: new ClipboardPlatform(Remote: true, Tmux: false, ClipboardFamily.Linux)
        );

        ClipboardCopy copy = await writer.WriteAsync(text, Ct);

        Assert.True(copy.MayBeTruncated);
        Assert.Equal(text.Length, copy.Characters);
        Assert.Equal(text.Length, Decode(Assert.Single(terminal)).Length);
    }

    [Fact]
    public async Task A_small_answer_is_not_flagged()
    {
        var writer = new ClipboardWriter(
            Environment.Of(("SSH_TTY", "/dev/pts/3")),
            new FakeTool(),
            writeTerminal: _ => { },
            platform: new ClipboardPlatform(Remote: true, Tmux: false, ClipboardFamily.Linux)
        );

        ClipboardCopy copy = await writer.WriteAsync(new string('x', 100 * 1024), Ct);

        Assert.False(copy.MayBeTruncated);
    }

    [Fact]
    public async Task Windows_uses_the_win32_api_and_never_starts_clip_exe()
    {
        var tool = new FakeTool();
        var terminal = new List<string>();
        string? pasted = null;
        var writer = new ClipboardWriter(
            Environment.Of(),
            tool,
            windowsClipboard: text =>
            {
                pasted = text;
                return true;
            },
            writeTerminal: terminal.Add,
            platform: new ClipboardPlatform(Remote: false, Tmux: false, ClipboardFamily.Windows)
        );

        ClipboardCopy copy = await writer.WriteAsync("héllo", Ct);

        Assert.Equal(ClipboardRoute.PlatformTool, copy.Route);
        Assert.Equal("Win32 clipboard", copy.Tool);
        Assert.Equal("héllo", pasted);
        Assert.Empty(tool.Requests);
        Assert.Empty(terminal);
    }

    [Fact]
    public async Task Windows_without_the_api_sends_the_sequence_instead_of_starting_clip_exe()
    {
        var tool = new FakeTool();
        var terminal = new List<string>();
        var writer = new ClipboardWriter(
            Environment.Of(),
            tool,
            windowsClipboard: _ => false,
            writeTerminal: terminal.Add,
            platform: new ClipboardPlatform(Remote: false, Tmux: false, ClipboardFamily.Windows)
        );

        ClipboardCopy copy = await writer.WriteAsync("héllo", Ct);

        Assert.Equal(ClipboardRoute.TerminalSequence, copy.Route);
        Assert.Empty(tool.Requests);
        Assert.Equal("héllo", Decode(Assert.Single(terminal)));
    }

    [Fact]
    public async Task Wsl_uses_clip_exe_first_and_powershell_second()
    {
        var tool = new FakeTool(succeeds: false);
        var writer = new ClipboardWriter(
            Environment.Of(("WSL_DISTRO_NAME", "Ubuntu")),
            tool,
            writeTerminal: _ => { },
            platform: new ClipboardPlatform(Remote: false, Tmux: false, ClipboardFamily.Wsl)
        );

        await writer.WriteAsync("text", Ct);

        Assert.Equal(["clip.exe", "powershell.exe"], tool.Requests.Select(r => r.Executable));
    }

    [Fact]
    public void The_sequence_is_base64_utf8_terminated_by_bel()
    {
        string sequence = ClipboardWriter.Osc52("héllo");

        Assert.StartsWith("\u001b]52;c;", sequence, StringComparison.Ordinal);
        Assert.EndsWith("\u0007", sequence, StringComparison.Ordinal);
        Assert.Equal("héllo", Decode(sequence));
    }

    [Fact]
    public void The_notice_says_copied_only_when_a_tool_proved_the_write()
    {
        Assert.Equal(
            "copied 1,842 characters via pbcopy",
            ClipboardNotice.Text(
                new ClipboardCopy(
                    ClipboardRoute.PlatformTool,
                    "pbcopy",
                    Characters: 1842,
                    MayBeTruncated: false,
                    Tmux: false
                )
            )
        );
        Assert.Equal(
            "sent 1,842 characters to the terminal clipboard (OSC 52)",
            ClipboardNotice.Text(
                new ClipboardCopy(
                    ClipboardRoute.TerminalSequence,
                    "OSC 52",
                    Characters: 1842,
                    MayBeTruncated: false,
                    Tmux: false
                )
            )
        );
    }

    [Fact]
    public void The_notice_names_tmux_and_truncation_once_in_the_same_line()
    {
        Assert.Equal(
            "sent 143,360 characters to the terminal clipboard (OSC 52)"
                + "; tmux requires set-clipboard on for this to work"
                + "; the terminal may truncate a copy this large",
            ClipboardNotice.Text(
                new ClipboardCopy(
                    ClipboardRoute.TerminalSequence,
                    "OSC 52",
                    Characters: 143_360,
                    MayBeTruncated: true,
                    Tmux: true
                )
            )
        );
    }

    private static string Decode(string sequence)
    {
        const string prefix = "\u001b]52;c;";
        Assert.StartsWith(prefix, sequence, StringComparison.Ordinal);
        Assert.EndsWith("\u0007", sequence, StringComparison.Ordinal);
        string payload = sequence[prefix.Length..^1];
        return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    }

    private sealed class FakeTool(bool succeeds = true) : IClipboardTool
    {
        public List<(
            string Executable,
            IReadOnlyList<string> Arguments,
            string Text
        )> Requests { get; } = [];

        public Task<bool> WriteAsync(
            string executable,
            IReadOnlyList<string> arguments,
            string text,
            CancellationToken cancellationToken
        )
        {
            Requests.Add((executable, arguments, text));
            return Task.FromResult(succeeds);
        }
    }

    private static class Environment
    {
        internal static Func<string, string?> Of(params (string Name, string Value)[] values)
        {
            var map = values.ToDictionary(pair => pair.Name, pair => pair.Value);
            return name => map.GetValueOrDefault(name);
        }
    }
}
