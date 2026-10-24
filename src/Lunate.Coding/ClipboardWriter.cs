using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lunate.Coding;

/// <summary>Where the text went: a platform tool whose exit code proves it, or the terminal.</summary>
internal enum ClipboardRoute
{
    PlatformTool,
    TerminalSequence,
}

/// <summary>The platform family whose clipboard tools apply; the Win32 API is the Windows path.</summary>
internal enum ClipboardFamily
{
    MacOs,
    Linux,
    Wsl,
    Windows,
}

/// <summary>
/// Where the session runs, as the clipboard decision sees it (design decision 2): a remote shell
/// has no clipboard the user can see, tmux needs one setting before OSC 52 works, and the platform
/// family picks the tool.
/// </summary>
internal sealed record ClipboardPlatform(bool Remote, bool Tmux, ClipboardFamily Family)
{
    /// <summary>
    /// Reads the launch environment: <c>SSH_CONNECTION</c> or <c>SSH_TTY</c> marks a remote session,
    /// <c>TMUX</c> a tmux client, and WSL is recognised by its own two variables.
    /// </summary>
    internal static ClipboardPlatform Detect(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return new ClipboardPlatform(
            Remote: Present(environment("SSH_CONNECTION")) || Present(environment("SSH_TTY")),
            Tmux: Present(environment("TMUX")),
            Family: FamilyOf(environment)
        );
    }

    private static ClipboardFamily FamilyOf(Func<string, string?> environment)
    {
        if (OperatingSystem.IsWindows())
        {
            return ClipboardFamily.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return ClipboardFamily.MacOs;
        }

        return Present(environment("WSL_DISTRO_NAME")) || Present(environment("WSL_INTEROP"))
            ? ClipboardFamily.Wsl
            : ClipboardFamily.Linux;
    }

    private static bool Present(string? value) => !string.IsNullOrEmpty(value);
}

/// <summary>
/// One clipboard write as it happened: the route, the tool's name, the character count (the number
/// the notice reports), whether the terminal may truncate the payload and whether tmux is in the
/// way. "Characters" is the text's length, never the encoded payload's.
/// </summary>
internal sealed record ClipboardCopy(
    ClipboardRoute Route,
    string Tool,
    int Characters,
    bool MayBeTruncated,
    bool Tmux
);

/// <summary>The text clipboard seam; tests substitute a fake instead of a real clipboard.</summary>
internal interface ITextClipboard
{
    ValueTask<ClipboardCopy> WriteAsync(string text, CancellationToken cancellationToken);
}

/// <summary>One platform clipboard process, run with the text on standard input.</summary>
internal interface IClipboardTool
{
    /// <summary>True when the tool started and exited 0; a missing tool is false, never an exception.</summary>
    Task<bool> WriteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string text,
        CancellationToken cancellationToken
    );
}

/// <summary>
/// The child-process half of the platform path. Text goes to the tool's standard input as UTF-8 —
/// the form <c>pbcopy</c>, <c>wl-copy</c>, <c>xclip</c>, <c>xsel</c> and <c>clip.exe</c> all read —
/// with a bounded deadline so a wedged helper cannot hold the prompt.
/// </summary>
internal sealed class ProcessClipboardTool : IClipboardTool
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<bool> WriteAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string text,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(text);

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process =
                Process.Start(startInfo) ?? throw new Win32Exception("the tool did not start");
        }
        catch (Exception exception)
            when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(Timeout);
            try
            {
                await process
                    .StandardInput.WriteAsync(text.AsMemory(), timeout.Token)
                    .ConfigureAwait(false);
                await process.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return false;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                return false;
            }

            return process.ExitCode == 0;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        { }
    }
}

/// <summary>
/// The Windows clipboard through the Win32 API (design decision 2): <c>clip.exe</c> is never used,
/// because it re-encodes text in the console code page and mangles anything non-ASCII. The write is
/// one CF_UNICODETEXT block, so the clipboard holds the UTF-16 text the process already has.
/// </summary>
internal static class Win32Clipboard
{
    private const uint GmemMoveable = 0x0042;
    private const uint CfUnicodeText = 13;

    /// <summary>Writes the text to the clipboard; false when the platform has no Win32 clipboard.</summary>
    internal static bool TryWrite(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return OperatingSystem.IsWindows() && Write(text);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool Write(string text)
    {
        // UTF-16 with a terminating null: the format the clipboard expects for CF_UNICODETEXT.
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr block = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
        if (block == IntPtr.Zero)
        {
            return false;
        }

        IntPtr target = GlobalLock(block);
        if (target == IntPtr.Zero)
        {
            _ = GlobalFree(block);
            return false;
        }

        bool copied = false;
        try
        {
            Marshal.Copy(bytes, 0, target, bytes.Length);
            copied = true;
        }
        finally
        {
            _ = GlobalUnlock(block);
        }

        if (!copied || !OpenClipboard(IntPtr.Zero))
        {
            _ = GlobalFree(block);
            return false;
        }

        try
        {
            _ = EmptyClipboard();
            if (SetClipboardData(CfUnicodeText, block) == IntPtr.Zero)
            {
                _ = GlobalFree(block);
                return false;
            }

            // The clipboard owns the block from here; freeing it would corrupt the clipboard.
            return true;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}

/// <summary>
/// Writes text to the clipboard the way the session's location allows (design decisions 2 and 3):
/// over ssh only the OSC 52 sequence — a local tool would write a clipboard the user cannot see —
/// and locally the platform tool first, with OSC 52 as the fallback and for any platform without
/// one. The OSC 52 payload is base64 of the UTF-8 text; above 100 KB the result is flagged as
/// possibly truncated by the terminal, and the text is never cut by Lunate.
/// </summary>
internal sealed class ClipboardWriter : ITextClipboard
{
    /// <summary>The payload size above which terminals commonly drop or clip an OSC 52 sequence.</summary>
    internal const int Osc52PayloadLimitBytes = 100 * 1024;

    private readonly Func<string, string?> _environment;
    private readonly IClipboardTool _tool;
    private readonly Func<string, bool> _windowsClipboard;
    private readonly Action<string> _writeTerminal;
    private readonly ClipboardPlatform? _platform;

    /// <param name="environment">The launch environment; null reads the process environment.</param>
    /// <param name="tool">The child-process seam; null starts real platform tools.</param>
    /// <param name="windowsClipboard">The Win32 write; null uses the real API.</param>
    /// <param name="writeTerminal">Where the OSC 52 sequence goes; null writes to standard output.</param>
    /// <param name="platform">A pinned platform (tests); null detects it from the environment.</param>
    internal ClipboardWriter(
        Func<string, string?>? environment = null,
        IClipboardTool? tool = null,
        Func<string, bool>? windowsClipboard = null,
        Action<string>? writeTerminal = null,
        ClipboardPlatform? platform = null
    )
    {
        _environment = environment ?? Environment.GetEnvironmentVariable;
        _tool = tool ?? new ProcessClipboardTool();
        _windowsClipboard = windowsClipboard ?? Win32Clipboard.TryWrite;
        _writeTerminal = writeTerminal ?? Console.Out.Write;
        _platform = platform;
    }

    public async ValueTask<ClipboardCopy> WriteAsync(
        string text,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        ClipboardPlatform platform = _platform ?? ClipboardPlatform.Detect(_environment);
        bool oversized = Encoding.UTF8.GetByteCount(text) > Osc52PayloadLimitBytes;

        // Remote: the only clipboard the user can see is the one their terminal owns.
        if (
            !platform.Remote
            && await PlatformToolAsync(platform, text, cancellationToken).ConfigureAwait(false)
                is { } tool
        )
        {
            return new ClipboardCopy(
                ClipboardRoute.PlatformTool,
                tool,
                text.Length,
                MayBeTruncated: false,
                Tmux: false
            );
        }

        _writeTerminal(Osc52(text));
        return new ClipboardCopy(
            ClipboardRoute.TerminalSequence,
            "OSC 52",
            text.Length,
            MayBeTruncated: oversized,
            platform.Tmux
        );
    }

    /// <summary>The base64 OSC 52 sequence that sets the clipboard, terminated by BEL.</summary>
    internal static string Osc52(string text) =>
        "\u001b]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "\u0007";

    private async Task<string?> PlatformToolAsync(
        ClipboardPlatform platform,
        string text,
        CancellationToken cancellationToken
    )
    {
        if (platform.Family == ClipboardFamily.Windows)
        {
            // The Win32 API is the Windows tool: clip.exe is never started, even as a fallback.
            return _windowsClipboard(text) ? "Win32 clipboard" : null;
        }

        foreach ((string executable, string[] arguments, string display) in Tools(platform.Family))
        {
            if (
                await _tool
                    .WriteAsync(executable, arguments, text, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                return display;
            }
        }

        return null;
    }

    private static IReadOnlyList<(string Executable, string[] Arguments, string Display)> Tools(
        ClipboardFamily family
    ) =>
        family switch
        {
            ClipboardFamily.MacOs => [("pbcopy", [], "pbcopy")],
            ClipboardFamily.Wsl =>
            [
                ("clip.exe", [], "clip.exe"),
                (
                    "powershell.exe",
                    ["-NoProfile", "-NonInteractive", "-Command", "Set-Clipboard"],
                    "PowerShell Set-Clipboard"
                ),
            ],
            _ =>
            [
                ("wl-copy", [], "wl-copy"),
                ("xclip", ["-selection", "clipboard"], "xclip"),
                ("xsel", ["--clipboard", "--input"], "xsel"),
            ],
        };
}
