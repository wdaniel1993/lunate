using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>The outcome of one best-effort spill write.</summary>
internal readonly record struct ToolOutputSpillResult(string? Path, bool TooLarge, string? Error);

/// <summary>
/// Writes a truncated tool result's full output beside its session at
/// <c>&lt;sessions-dir&gt;/&lt;session-id&gt;/tool-output/&lt;run&gt;-&lt;seq&gt;.txt</c>. A write is
/// best-effort: the caller keeps the truncation marker when it fails, and a result over the ceiling
/// is never written. Files are flushed to disk and moved into place atomically.
/// </summary>
internal static class ToolOutputSpill
{
    /// <summary>The largest output, in UTF-8 bytes, a spill file may hold.</summary>
    internal const long MaxBytes = 64L * 1024 * 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The session's own tool-output directory, beside its image blobs.</summary>
    internal static string DirectoryFor(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        string sessionDirectory =
            Path.GetDirectoryName(Path.GetFullPath(session.Path))
            ?? throw new ArgumentException(
                $"Session path '{session.Path}' has no directory to place spilled output in.",
                nameof(session)
            );
        return Path.Combine(sessionDirectory, session.SessionId, "tool-output");
    }

    /// <summary>
    /// Recognizes one existing spill written directly by this writer. A directory-prefix match is
    /// not enough: the resolved candidate must be the exact generated run/sequence file.
    /// </summary>
    internal static bool IsKnownFile(Session session, string path)
    {
        try
        {
            string root = GitIdentity.Canonicalize(DirectoryFor(session));
            string candidate = GitIdentity.Canonicalize(path);
            StringComparison comparison = OperatingSystem.IsLinux()
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return string.Equals(Path.GetDirectoryName(candidate), root, comparison)
                && IsSpillFileName(Path.GetFileName(candidate))
                && File.Exists(candidate)
                && session
                    .Entries.OfType<SessionMessageEntry>()
                    .SelectMany(entry => entry.Message.Contents)
                    .OfType<FunctionResultContent>()
                    .Any(result => ResultNamesSpill(result.Result, candidate, comparison));
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return false;
        }
    }

    /// <summary>Writes <paramref name="output"/> under a deterministic run and sequence name.</summary>
    internal static ToolOutputSpillResult TryWrite(
        Session session,
        string runId,
        int sequence,
        string output
    )
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        // The admission mask runs first in the harness, so this text is already masked; the
        // redaction here is the persistence boundary for the bytes on disk, defence in depth.
        string redacted = session.Redactor.Redact(output);
        if (Encoding.UTF8.GetByteCount(redacted) > MaxBytes)
        {
            return new(Path: null, TooLarge: true, Error: null);
        }

        string directory = DirectoryFor(session);
        string target = Path.Combine(
            directory,
            string.Create(CultureInfo.InvariantCulture, $"{runId}-{sequence}.txt")
        );
        string temp = Path.Combine(
            directory,
            ".tmp-"
                + runId
                + "-"
                + sequence.ToString(CultureInfo.InvariantCulture)
                + "-"
                + Guid.NewGuid().ToString("N")
                + ".txt"
        );
        try
        {
            SecureFiles.CreateOwnerOnlyDirectory(directory);
            using (FileStream stream = SessionFiles.CreateNewOwnerOnly(temp))
            using (
                var writer = new StreamWriter(
                    stream,
                    Utf8NoBom,
                    bufferSize: 64 * 1024,
                    leaveOpen: true
                )
            )
            {
                writer.Write(redacted);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
            return new(Path.GetFullPath(target), TooLarge: false, Error: null);
        }
        catch (Exception exception)
        {
            TryDelete(temp);
            return new(
                Path: null,
                TooLarge: false,
                Error: $"could not spill truncated tool output to '{target}': {exception.Message}"
            );
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsSpillFileName(string fileName)
    {
        if (!fileName.EndsWith(".txt", StringComparison.Ordinal))
        {
            return false;
        }

        string name = fileName[..^4];
        int separator = name.LastIndexOf('-');
        return separator > 0
            && int.TryParse(
                name.AsSpan(separator + 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int sequence
            )
            && sequence >= 0;
    }

    private static bool ResultNamesSpill(
        object? result,
        string candidate,
        StringComparison comparison
    )
    {
        string? output = result switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null,
        };
        const string marker = "full output at ";
        if (output is null)
        {
            return false;
        }

        int start = output.LastIndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        start += marker.Length;
        int end = output.IndexOf(']', start);
        if (end <= start)
        {
            return false;
        }

        try
        {
            string namedPath = GitIdentity.Canonicalize(output[start..end]);
            return string.Equals(namedPath, candidate, comparison);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return false;
        }
    }
}
