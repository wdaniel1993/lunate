using System.Globalization;
using System.Text;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>The two export forms: the session's own JSONL, or one self-contained HTML document.</summary>
internal enum ExportFormat
{
    Html,
    Jsonl,
}

/// <summary>One parsed <c>/export</c> request: the form, an optional path and whether to overwrite.</summary>
internal sealed record ExportRequest(ExportFormat Format, string? Path, bool Force)
{
    internal const string Usage = "usage: /export html|jsonl [path] [--force]";

    /// <summary>
    /// Parses <c>html|jsonl [path] [--force]</c>. The form is required — a bare path would have to
    /// guess the format from the extension — and <c>--force</c> may sit before or after the path.
    /// </summary>
    internal static bool TryParse(string argument, out ExportRequest request, out string error)
    {
        ArgumentNullException.ThrowIfNull(argument);
        request = new ExportRequest(ExportFormat.Html, null, false);
        error = Usage;
        string[] tokens = argument.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        if (tokens.Length == 0)
        {
            return false;
        }

        ExportFormat format;
        if (string.Equals(tokens[0], "html", StringComparison.OrdinalIgnoreCase))
        {
            format = ExportFormat.Html;
        }
        else if (string.Equals(tokens[0], "jsonl", StringComparison.OrdinalIgnoreCase))
        {
            format = ExportFormat.Jsonl;
        }
        else
        {
            return false;
        }

        string? path = null;
        var force = false;
        foreach (string token in tokens[1..])
        {
            if (string.Equals(token, "--force", StringComparison.Ordinal))
            {
                force = true;
                continue;
            }

            if (token.StartsWith("--", StringComparison.Ordinal) || path is not null)
            {
                return false;
            }

            path = token;
        }

        error = string.Empty;
        request = new ExportRequest(format, path, force);
        return true;
    }
}

/// <summary>
/// Where an export goes (design decision 8): never the current directory — an export inside a
/// repository ends up committed — but <c>~/.lunate/exports/&lt;yyyy-MM-dd&gt;-&lt;name or id&gt;.&lt;ext&gt;</c>,
/// with a path argument overriding it. The result is always absolute; the caller prints it whole.
/// </summary>
internal static class ExportTargets
{
    /// <summary>The directory name under the state root.</summary>
    internal const string DirectoryName = "exports";

    /// <summary>The longest session-name fragment a file name carries.</summary>
    internal const int MaxNameLength = 64;

    /// <summary>The absolute destination for one request.</summary>
    internal static string Resolve(
        ExportRequest request,
        string storeRoot,
        string workingDirectory,
        string sessionNameOrId,
        DateTimeOffset now
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(storeRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionNameOrId);

        if (request.Path is { Length: > 0 } path)
        {
            string anchored = System.IO.Path.IsPathRooted(path)
                ? path
                : System.IO.Path.Combine(workingDirectory, path);
            return System.IO.Path.GetFullPath(anchored);
        }

        string extension = request.Format == ExportFormat.Html ? ".html" : ".jsonl";
        string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                storeRoot,
                DirectoryName,
                date + "-" + FileName(sessionNameOrId) + extension
            )
        );
    }

    /// <summary>
    /// A session name as a file-name fragment: letters, digits, <c>-</c>, <c>_</c> and <c>.</c> stay,
    /// every other character becomes one <c>-</c>, runs collapse and the ends are trimmed. A name
    /// that normalizes away falls back to <c>session</c>.
    /// </summary>
    internal static string FileName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var builder = new StringBuilder(name.Length);
        foreach (char character in name.Trim())
        {
            char next =
                char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-';
            if (next == '-' && builder.Length > 0 && builder[^1] == '-')
            {
                continue;
            }

            builder.Append(next);
        }

        string sanitized = builder.ToString().Trim('-', '.', ' ');
        if (sanitized.Length == 0)
        {
            return "session";
        }

        return sanitized.Length <= MaxNameLength
            ? sanitized
            : sanitized[..MaxNameLength].TrimEnd('-', '.');
    }
}

/// <summary>What one export wrote: the absolute path and how many entries it carries.</summary>
internal sealed record ExportOutcome(string Path, int Entries);

/// <summary>
/// Writes the session (design decisions 4 and 6). JSONL is the session file byte for byte with only
/// the credential redaction applied — no re-serialization, so a field added tomorrow cannot make
/// the export drift from the file — and HTML is the one document <see cref="SessionHtmlWriter"/>
/// builds. The export inherits the session file's permissions so it is never more readable than the
/// file it came from.
/// </summary>
internal static class SessionExporter
{
    internal static ExportOutcome Write(
        Session session,
        ExportRequest request,
        string destination,
        string title,
        CredentialRedactor redactor
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(redactor);

        string? directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] bytes =
            request.Format == ExportFormat.Jsonl
                ? redactor.RedactUtf8(File.ReadAllBytes(session.Path))
                : SessionHtmlWriter.Bytes(session, title, redactor);
        File.WriteAllBytes(destination, bytes);
        InheritMode(session.Path, destination);
        return new ExportOutcome(destination, session.Entries.Count);
    }

    private static void InheritMode(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            // Best effort: the file exists and is written; permissions keep the process default.
        }
    }
}
