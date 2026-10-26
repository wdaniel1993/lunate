using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Lunate.Coding;

/// <summary>
/// The program that edits the draft: its name, the arguments the configuration carried, and where
/// the name came from (the variable, for a notice that names what failed).
/// </summary>
internal sealed record EditorCommand(
    string Program,
    IReadOnlyList<string> Arguments,
    string? Source,
    string Value
)
{
    /// <summary>The command as a notice names it: <c>VISUAL="vim -u NONE"</c>, or just the program.</summary>
    public string Label =>
        Source is { Length: > 0 } source
            ? string.Create(CultureInfo.InvariantCulture, $"{source}=\"{Value}\"")
            : Program;
}

/// <summary>
/// <c>Ctrl+G</c>'s editor contract, free of the terminal and the session: resolve the program, find
/// it, write and read the draft file, and normalize what comes back. Resolution is one pass, first
/// non-blank wins, split on whitespace with no shell and no quote interpretation — one rule on every
/// platform beats a half-shell that surprises on Windows.
/// </summary>
internal static class DraftEditor
{
    internal const string VisualVariable = "VISUAL";
    internal const string EditorVariable = "EDITOR";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The platform default when neither variable is set or both are blank.</summary>
    public static string DefaultProgram(bool windows) => windows ? "notepad" : "vi";

    /// <summary>
    /// <c>$VISUAL</c>, else <c>$EDITOR</c>, else the platform default. A variable that is set but
    /// blank counts as unset; a value that cannot be launched is never replaced by the next
    /// candidate (that would run an editor the user did not configure).
    /// </summary>
    public static EditorCommand Resolve(Func<string, string?> environment, bool windows)
    {
        ArgumentNullException.ThrowIfNull(environment);

        foreach (string variable in new[] { VisualVariable, EditorVariable })
        {
            string? raw = environment(variable);
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string value = raw.Trim();
            string[] parts = value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (parts.Length == 0)
            {
                continue;
            }

            return new EditorCommand(parts[0], [.. parts.Skip(1)], variable, value);
        }

        string program = DefaultProgram(windows);
        return new EditorCommand(program, [], null, program);
    }

    /// <summary>
    /// The program's path, or null when nothing by that name exists: a name with a directory part
    /// is used as given, a bare name is looked up on <c>PATH</c> (with <c>PATHEXT</c> on Windows, so
    /// <c>notepad</c> and <c>code.cmd</c> both resolve). Finding it first is what lets a missing
    /// editor post the notice the guide promises instead of an exception's prose.
    /// </summary>
    public static string? Locate(string program, Func<string, string?> environment, bool windows)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(environment);

        if (program.Length == 0)
        {
            return null;
        }

        if (program.Contains(Path.DirectorySeparatorChar) || program.Contains('/'))
        {
            return File.Exists(program) ? program : null;
        }

        string? path = environment("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            string candidate = Path.Combine(directory, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (!windows)
            {
                continue;
            }

            string? extensions = environment("PATHEXT");
            foreach (
                string extension in (extensions ?? ".COM;.EXE;.BAT;.CMD").Split(
                    ';',
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                string withExtension = candidate + extension.ToLowerInvariant();
                if (File.Exists(withExtension))
                {
                    return withExtension;
                }

                withExtension = candidate + extension;
                if (File.Exists(withExtension))
                {
                    return withExtension;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Writes the draft for the editor: UTF-8 without a BOM, owner-only on POSIX (the same
    /// reasoning as <c>auth.json</c>), created fresh so a leftover file is never edited.
    /// </summary>
    public static void WriteDraft(string path, string text)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        stream.Write(Utf8NoBom.GetBytes(text));
    }

    /// <summary>
    /// What the editor left: CRLF normalized to LF, and exactly one trailing newline removed.
    /// Editors append a final newline as a matter of course; removing every one would eat a
    /// deliberate blank line, and removing none would add an empty last line on every edit.
    /// </summary>
    public static string ReadBack(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return normalized.EndsWith('\n') ? normalized[..^1] : normalized;
    }

    /// <summary>
    /// Starts the editor on the draft with the session's own stdin, stdout and stderr: the child
    /// draws on the terminal the session just handed over, and nothing is captured or echoed.
    /// </summary>
    public static Process Start(EditorCommand command, string draftPath, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(draftPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = command.Program,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };
        foreach (string argument in command.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(draftPath);
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"could not start {command.Program}: no process"
                )
            );
    }

    /// <summary>Deletes the draft file; a missing file is not a failure (it was never written).</summary>
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The editor's own file is gone or out of reach; nothing here can act on it.
        }
    }

    /// <summary>The notice for a program name that does not exist anywhere on the path.</summary>
    public static string MissingProgramNotice(EditorCommand command) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{command.Label}: no such program \u2014 draft unchanged"
        );

    /// <summary>The notice for a child that started and refused the edit.</summary>
    public static string ExitNotice(int exitCode) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"editor exited {exitCode} \u2014 draft unchanged"
        );

    /// <summary>The notice for a start that threw after the program was found.</summary>
    public static string StartFailureNotice(EditorCommand command, Win32Exception failure) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"could not start {command.Program}: {failure.Message}"
        );

    /// <summary>The notice for a read-back that failed; the draft stays as it was.</summary>
    public static string ReadFailureNotice(Exception failure) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"could not read the edited draft: {failure.Message}"
        );
}
