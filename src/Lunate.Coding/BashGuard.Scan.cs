using System.Globalization;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>The pre-run scan for <see cref="BashGuard"/>: what a command's redirection would
/// write, and the refusals and hints the tool reports without or after a spawn.</summary>
internal sealed partial class BashGuard
{
    /// <summary>
    /// The pre-run refusal for a command that would write a denied target: refused without a spawn,
    /// naming the guard, because the mechanism would refuse it a moment later in OS terms.
    /// </summary>
    internal string? Refusal(string command, GuardCommand launch)
    {
        if (!launch.Wrapped)
        {
            return null;
        }

        return WriteTargetIn(command) switch
        {
            string denied => string.Create(
                CultureInfo.InvariantCulture,
                $"refused by the bash guard ({launch.Mechanism}): the command would write under "
                    + $"{denied}, and the guard denies writes under {_target.StateRoot} and "
                    + $"{_target.Prefix}. Write inside the workspace instead."
            ),
            null => null,
        };
    }

    /// <summary>
    /// The guard's credit for a failure the mechanism most likely caused: the command touched a
    /// denied target and its stderr shows a filesystem denial. Best-effort, so it says "may".
    /// </summary>
    internal string? Hint(string command, string stderr, GuardCommand launch)
    {
        if (!launch.Wrapped)
        {
            return null;
        }

        bool denied =
            stderr.Contains("Operation not permitted", StringComparison.Ordinal)
            || stderr.Contains("Permission denied", StringComparison.Ordinal)
            || stderr.Contains("Read-only file system", StringComparison.Ordinal);
        bool touched =
            command.Contains(_target.StateRoot, StringComparison.OrdinalIgnoreCase)
            || command.Contains(_target.Prefix, StringComparison.OrdinalIgnoreCase)
            || command.Contains(_target.AuthPath, StringComparison.OrdinalIgnoreCase);
        return denied && touched
            ? $"maybe refused by the bash guard ({launch.Mechanism}): the command touched a denied "
                + "target and failed with a filesystem denial (writes under the state root and the "
                + "install prefix are denied)."
            : null;
    }

    // The pre-run scan -------------------------------------------------------

    /// <summary>
    /// The path a command's redirection would write into, when it is a denied target. Conservative
    /// by design: a skipped or self-spelling token (<c>$HOME</c>, bare <c>|</c> notation) is left to
    /// the mechanism itself, and obscured quoting is covered by the same backstop.
    /// </summary>
    private string? WriteTargetIn(string command)
    {
        var singleQuote = false;
        var doubleQuote = false;
        for (var index = 0; index < command.Length; index++)
        {
            char current = command[index];
            bool inQuote = singleQuote || doubleQuote;
            if (inQuote && current == '\\' && index + 1 < command.Length)
            {
                index++;
                continue;
            }

            if (!doubleQuote && current == '\'')
            {
                singleQuote = !singleQuote;
                continue;
            }

            if (!singleQuote && current == '"')
            {
                doubleQuote = !doubleQuote;
                continue;
            }

            if (inQuote || current != '>')
            {
                continue;
            }

            while (
                index + 1 < command.Length
                && (
                    command[index + 1] == '>'
                    || command[index + 1] == '|'
                    || command[index + 1] == '&'
                )
            )
            {
                index++;
            }

            while (
                index + 1 < command.Length
                && (command[index + 1] == ' ' || command[index + 1] == '\t')
            )
            {
                index++;
            }

            string? token = ReadWord(command, index + 1);
            if (DenyTarget(token) is { } deniedPath)
            {
                return deniedPath;
            }
        }

        return null;
    }

    private static string? ReadWord(string command, int start)
    {
        if (start >= command.Length)
        {
            return null;
        }

        int end = start;
        while (
            end < command.Length
            && command[end] is not (' ' or '\t' or ';' or '|' or '&' or '<' or '\n')
        )
        {
            end++;
        }

        if (end == start)
        {
            return null;
        }

        string token = command[start..end].Trim('\'', '"');
        return token.Length == 0 ? null : token;
    }

    private string? DenyTarget(string? token)
    {
        if (token is null)
        {
            return null;
        }

        string candidate = Expand(token);
        if (!candidate.Contains('/', StringComparison.Ordinal))
        {
            // A relative token points into the shell's working directory (the workspace), which
            // the guard never denies.
            return null;
        }

        if (!Path.IsPathRooted(candidate) && _workspace is { } workspace)
        {
            // Anchor a relative token to the workspace, not to the process's current directory:
            // the shell runs in the workspace, so its own spelling of a workspace-relative write
            // must never be refused, wherever Lunate itself was started from.
            candidate = Path.Combine(workspace, candidate);
        }

        try
        {
            if (LunateHome.IsInside(candidate, _target.StateRoot))
            {
                return _target.StateRoot;
            }

            return LunateHome.IsInside(candidate, _target.Prefix) ? _target.Prefix : null;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private string Expand(string token)
    {
        string expanded = token.Contains("$LUNATE_HOME", StringComparison.Ordinal)
            ? token.Replace("$LUNATE_HOME", _target.StateRoot)
            : token;
        if (expanded.StartsWith("~/", StringComparison.Ordinal))
        {
            expanded = Path.Combine(_target.ProfileBase, expanded[2..]);
        }
        else if (expanded.StartsWith("$HOME/", StringComparison.Ordinal))
        {
            expanded = Path.Combine(_target.ProfileBase, expanded[6..]);
        }

        return expanded;
    }
}
