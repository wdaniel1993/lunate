using System.Text;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// Reads the single-file extension candidates of one extensions root: every <c>*.cs</c> file
/// directly under it (nested files belong to a packaged extension), parsed before anything is
/// restored or compiled. A file with no <c>#:extension</c> and a file whose header refuses it are
/// candidates too — <c>/extensions</c> lists them with the reason instead of hiding them.
/// </summary>
internal sealed class SingleFileExtensionReader(
    ExtensionHostOptions options,
    SingleFileTarget? target = null
)
{
    private readonly ExtensionTrustStore _trust = new(options);
    private readonly ExtensionHostOptions _options = options;

    /// <summary>
    /// The toolset a build must have been made for to count: the current process's, unless a test
    /// says otherwise. Reading it here keeps <c>built</c> honest — a build compiled for another
    /// runtime is not this runtime's build, and the key says so.
    /// </summary>
    private readonly SingleFileTarget _target = target ?? SingleFileTarget.Current();

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
            candidates.Add(ReadOne(path, scope));
        }

        return candidates;
    }

    /// <summary>
    /// One candidate, read fresh from disk: the restorer calls this so a restore never acts on a
    /// snapshot taken before the user's last edit.
    /// </summary>
    internal SingleFileExtension ReadOne(string path, ExtensionScope scope)
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
            return Invalid(fullPath, scope, header, Describe(header.Errors));
        }

        if (header.Id is null)
        {
            return Invalid(fullPath, scope, header, "no #:extension");
        }

        string sourceDigest = SingleFileExtensionDigest.ComputeSource(
            header.Id,
            header.Packages,
            bytes
        );
        LockRead read = ReadLock(header.Id, sourceDigest);
        string digest = SingleFileExtensionDigest.Compute(
            header.Id,
            header.Packages,
            bytes,
            read.GraphBytes
        );

        if (read.Fatal is not null)
        {
            return new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.Stale,
                Digest = digest,
                SourceDigest = sourceDigest,
                Reason = read.Fatal,
            };
        }

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
                SourceDigest = sourceDigest,
                Lock = read.Lock,
                LockHash = read.Hash,
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
                SourceDigest = sourceDigest,
                Lock = read.Lock,
                LockHash = read.Hash,
                Reason =
                    "the file, its declared packages or the resolution in its lock changed since "
                    + "it was trusted; it must be trusted again before it is restored",
            },
            SingleFileExtensionTrust.Unreadable => new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.TrustFailed,
                Digest = digest,
                SourceDigest = sourceDigest,
                Lock = read.Lock,
                LockHash = read.Hash,
                Reason =
                    "the trust record for this file is unreadable; remove its entry from "
                    + "trust.json and trust the file again",
            },
            // Untrusted is not broken: the file is usable and nothing has approved it yet, so the
            // row says what is missing and which command asks for it. Without the sentence a
            // discovered file that provides nothing reads like a defect instead of a decision.
            // No build is named either — a build the cache holds for these bytes must not make an
            // unapproved file look loadable, and the load path refuses it before the key is read.
            SingleFileExtensionTrust.Untrusted => new SingleFileExtension
            {
                Path = fullPath,
                Scope = scope,
                Header = header,
                State = SingleFileExtensionState.NotBuilt,
                Digest = digest,
                SourceDigest = sourceDigest,
                Lock = read.Lock,
                LockHash = read.Hash,
                Reason = "not trusted yet; /extensions reload asks before it builds",
            },
            _ => Built(fullPath, scope, header, digest, sourceDigest, read),
        };
    }

    /// <summary>
    /// The trusted row: <c>built</c> when an assembly exists for exactly these inputs (the source
    /// digest, the whole lock, the host contract version and this toolset), <c>not-built</c>
    /// otherwise. The check is a key computation and a file existence test — no compiler, no NuGet,
    /// so discovery stays on the startup budget. Without a lock there is no key to compute, and a
    /// refused toolset block is the reason the row carries.
    /// </summary>
    private SingleFileExtension Built(
        string fullPath,
        ExtensionScope scope,
        ExtensionHeader header,
        string digest,
        string sourceDigest,
        LockRead read
    )
    {
        string? buildKey = null;
        string? assemblyPath = null;
        if (read.Lock is not null && read.Hash is { } lockHash)
        {
            buildKey = SingleFileBuildKey.Compute(
                sourceDigest,
                lockHash,
                ExtensionApi.Current,
                _target.Tfm,
                _target.Rid
            );
            string path = SingleFileExtensionPaths.AssemblyPath(
                _options.StorePath,
                header.Id!,
                buildKey
            );
            if (File.Exists(path))
            {
                assemblyPath = path;
            }
            else
            {
                buildKey = null;
            }
        }

        return new SingleFileExtension
        {
            Path = fullPath,
            Scope = scope,
            Header = header,
            State = assemblyPath is null
                ? SingleFileExtensionState.NotBuilt
                : SingleFileExtensionState.Built,
            Digest = digest,
            SourceDigest = sourceDigest,
            Lock = read.Lock,
            LockHash = read.Hash,
            BuildKey = buildKey,
            AssemblyPath = assemblyPath,
            Reason = assemblyPath is null ? read.Refused : null,
        };
    }

    /// <summary>
    /// What one lock file yielded for a file: the lock when it answers for these source bytes and
    /// its toolset block is intact, the graph bytes the trust digest covers, and the reasons it
    /// cannot be used. <see cref="Fatal"/> means no graph could be read at all (nothing to trust
    /// against); <see cref="Refused"/> means the graph is intact but the toolset block is not, so
    /// the file stays trusted and the next restore rebuilds the block instead of asking.
    /// </summary>
    private sealed record LockRead(
        ExtensionLock? Lock,
        string? Hash,
        byte[] GraphBytes,
        string? Fatal,
        string? Refused
    )
    {
        internal static LockRead None { get; } = new(null, null, [], null, null);
    }

    /// <summary>
    /// The lock that answers for <paramref name="sourceDigest"/>, its whole-file hash and the graph
    /// bytes the digest hashes. A lock resolved from other bytes belongs to an earlier version of
    /// the file: it is not part of this file's digest and is not reused, and the next restore reports
    /// it instead of overwriting it. A lock file that cannot be read at all is a problem, never a
    /// silent absence; one whose toolset block was modified keeps its graph and is refused.
    /// </summary>
    private LockRead ReadLock(string id, string sourceDigest)
    {
        string path = SingleFileExtensionPaths.LockPath(_options.StorePath, id);
        if (!File.Exists(path))
        {
            return LockRead.None;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new LockRead(
                null,
                null,
                [],
                $"extension.lock.json could not be read: {exception.Message}",
                null
            );
        }

        ExtensionLockJson.Reading reading = ExtensionLockJson.Read(Decode(bytes));
        if (reading.Problem is { } problem)
        {
            // A refused toolset block still leaves the graph the user approved: the file stays
            // trusted for it, and the rebuild replaces the block without a question.
            return reading.GraphBytes.Length == 0
                ? new LockRead(
                    null,
                    null,
                    [],
                    $"extension.lock.json is unusable ({problem}); the next restore asks before "
                        + "replacing it",
                    null
                )
                : new LockRead(null, null, reading.GraphBytes, null, problem);
        }

        // A lock that resolved other source bytes belongs to an earlier version of the file: it is
        // not reused and its graph is not part of this file's digest.
        return reading.Lock is { } parsed && parsed.IsCurrentFor(sourceDigest)
            ? new LockRead(parsed, ExtensionLockJson.Hash(bytes), reading.GraphBytes, null, null)
            : LockRead.None;
    }

    private static SingleFileExtension Invalid(
        string path,
        ExtensionScope scope,
        ExtensionHeader header,
        string reason
    ) =>
        new()
        {
            Path = path,
            Scope = scope,
            Header = header,
            State = SingleFileExtensionState.Invalid,
            Reason = reason,
        };

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
