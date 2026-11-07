using System.Globalization;
using System.Text;
using Lunate.Ai;
using Lunate.Tui;

namespace Lunate.Coding;

/// <summary>
/// The session half of the <c>LUNATE_INPUT_TRACE</c> trace: what arrived here and where the router
/// sent it. It records key kinds, modifiers, routing decisions and timestamps only; printable text
/// and paste content become <c>chars=N</c>, so a credential typed into a prompt leaves nothing in
/// the file. The rule — default under the state root, an explicit value admissible only inside it,
/// owner-only on POSIX, LF and UTF-8 — holds a second copy over in <c>Lunate.Tui</c>, which is the
/// terminal half of the same trace; the boundary tests pin both.
/// </summary>
internal static class InputTrace
{
    /// <summary>UTF-8 without a BOM; the Write text already ends in its LF.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Traces one key with the routing decision the router made for it and the session state that
    /// decision was made under.
    /// </summary>
    public static void Key(
        KeyEvent key,
        RoutedKey routed,
        bool turnActive,
        bool runCts,
        bool bashRunning
    )
    {
        string? path = ResolvePath();
        if (path is null)
        {
            return;
        }

        Write(
            path,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{DateTimeOffset.UtcNow:O} key kind={key.Kind} ctrl={key.Ctrl} shift={key.Shift} alt={key.Alt} chars={(key.Text is null ? "-" : key.Text.Length.ToString(CultureInfo.InvariantCulture))} routed={routed} turn={turnActive} runCts={runCts} bash={bashRunning}"
            )
        );
    }

    /// <summary>
    /// Writes one line. A failed trace never disturbs input: the diagnostic's failure is nothing.
    /// </summary>
    internal static void Write(string path, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + '\n', Utf8);
            if (!OperatingSystem.IsWindows())
            {
                // Every write, not once per process: a file written through another path must not
                // keep its create-mode permissions.
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        { }
    }

    /// <summary>
    /// Resolves the trace path the trace rule names, once per process where the state root is
    /// read: the session resolves it and configures both trace halves with it.
    /// </summary>
    internal static string? ResolvePath()
    {
        string? setting = Environment.GetEnvironmentVariable(Lunate.Tui.InputTrace.Variable);
        return string.IsNullOrWhiteSpace(setting)
            ? null
            : Lunate.Tui.InputTrace.Resolve(setting, LunateHome.Store);
    }
}
