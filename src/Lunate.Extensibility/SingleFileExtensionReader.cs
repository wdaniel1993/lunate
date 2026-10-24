using System.Text;

namespace Lunate.Extensibility;

/// <summary>
/// Reads the single-file extension candidates of one extensions root: every <c>*.cs</c> file
/// directly under it (nested files belong to a packaged extension), parsed before anything is
/// restored or compiled. A file with no <c>#:extension</c> and a file whose header refuses it are
/// candidates too — <c>/extensions</c> lists them with the reason instead of hiding them.
/// </summary>
internal sealed class SingleFileExtensionReader(ExtensionTrustStore trust)
{
    private readonly ExtensionTrustStore _trust = trust;

    /// <summary>
    /// The candidates of <paramref name="root"/> in file-name order; empty when the directory does
    /// not exist. An unreadable directory is reported through <paramref name="errors"/> and yields
    /// no candidates.
    /// </summary>
    internal IReadOnlyList<SingleFileExtension> Read(
        string root,
        ExtensionScope scope,
        List<ExtensionDiscoveryError> errors
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(errors);

        if (!Directory.Exists(root))
        {
            return [];
        }

        List<string> files;
        try
        {
            files = [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add(
                new ExtensionDiscoveryError(
                    root,
                    $"single-file extensions in '{root}' could not be listed: {exception.Message}"
                )
            );
            return [];
        }

        files.Sort(
            (left, right) =>
                StringComparer.Ordinal.Compare(Path.GetFileName(left), Path.GetFileName(right))
        );

        List<SingleFileExtension> candidates = [];
        foreach (string path in files)
        {
            candidates.Add(ReadFile(path, scope));
        }

        return candidates;
    }

    private SingleFileExtension ReadFile(string path, ExtensionScope scope)
    {
        string fullPath = Path.GetFullPath(path);
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = ExtensionHeader.None,
                State = SingleFileExtensionState.TrustFailed,
                Reason = $"the file could not be read: {exception.Message}",
            };
        }

        ExtensionHeader header = ExtensionHeaderScanner.Scan(Decode(bytes));
        if (header.Errors.Count > 0)
        {
            return new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.Invalid,
                Reason = Describe(header.Errors),
            };
        }

        if (header.Id is null)
        {
            return new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.Invalid,
                Reason = "no #:extension",
            };
        }

        string digest = SingleFileExtensionDigest.Compute(header.Id, header.Packages, bytes);
        SingleFileExtensionTrust trust;
        try
        {
            trust = _trust.EvaluateSingleFileTrust(fullPath, digest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.TrustFailed,
                Digest = digest,
                Reason = $"the trust store could not be read: {exception.Message}",
            };
        }

        return trust switch
        {
            SingleFileExtensionTrust.Stale => new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.Stale,
                Digest = digest,
                Reason =
                    "the file or its declared packages changed since it was trusted; it must be "
                    + "trusted again before it is built",
            },
            SingleFileExtensionTrust.Unreadable => new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.TrustFailed,
                Digest = digest,
                Reason =
                    "the trust record for this file is unreadable; remove its entry from "
                    + "trust.json and trust the file again",
            },
            _ => new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.NotBuilt,
                Digest = digest,
            },
        };
    }

    /// <summary>The source text, without a byte-order mark the scanner would read as content.</summary>
    private static string Decode(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    /// <summary>The first problem, plus how many others the file has (the reason stays one line).</summary>
    private static string Describe(IReadOnlyList<ExtensionHeaderIssue> errors) =>
        errors.Count == 1
            ? errors[0].ToString()
            : FormattableString.Invariant($"{errors[0]} (and {errors.Count - 1} more)");
}
