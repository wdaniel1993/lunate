namespace Lunate.Extensibility;

/// <summary>
/// The value-shaping half of the restorer: the question a decision needs, the lock a resolution
/// composes into, the two kinds of difference a rebuild can rest on, and the outcome rows. Split
/// from the flow so the order the spec cares about — trust, then lock, then engine — stays readable
/// in one screen, and so the one rule the split turns on is stated once: a graph difference asks, a
/// toolset difference only rebuilds.
/// </summary>
public sealed partial class SingleFileExtensionRestorer
{
    /// <summary>
    /// What writing <paramref name="resolved"/> would change, split by what it means.
    /// <see cref="GraphChanges"/> is the trust-relevant half and is what a question lists;
    /// <see cref="RebuildReasons"/> is why the lock is (re)written, toolset lines included.
    /// </summary>
    private sealed record Comparison(
        IReadOnlyList<string> GraphChanges,
        IReadOnlyList<string> RebuildReasons
    )
    {
        internal static Comparison None { get; } = new([], []);
    }

    private static SingleFileTrustRequest Question(SingleFileExtension candidate, bool changed) =>
        new()
        {
            Path = candidate.Path,
            Id = candidate.Id!,
            Version = candidate.Version ?? "0.0.0",
            Packages = [.. candidate.Header.Packages.Select(package => package.Written)],
            Generators = candidate.Header.Generators,
            Changed = changed,
        };

    private static ExtensionLock Compose(
        SingleFileExtension candidate,
        SingleFileTarget target,
        SingleFileRestoreResult result
    ) =>
        new()
        {
            ExtensionId = candidate.Id!,
            ExtensionVersion = candidate.Version ?? "0.0.0",
            SourceDigest = candidate.SourceDigest,
            PackagesWritten = [.. candidate.Header.Packages.Select(package => package.Written)],
            Toolset = new ExtensionLockToolset
            {
                Tfm = target.Tfm,
                Rid = target.Rid,
                Runtime = target.Runtime,
                ReferencePack = result.ReferencePack,
            },
            Sources = [.. result.Sources],
            Packages = [.. result.Packages],
        };

    /// <summary>
    /// What writing <paramref name="resolved"/> would change, compared over the two halves the lock
    /// has. The graph is compared byte for byte against the canonical graph bytes, so a lock that
    /// resolves the same graph is not a trust change; the toolset is compared field by field and is
    /// only ever a rebuild reason. A lock whose toolset block was refused keeps its graph, so the
    /// replacement of that block costs no question either.
    /// </summary>
    private static Comparison Compare(string lockPath, ExtensionLock resolved)
    {
        byte[] existingBytes;
        try
        {
            if (!File.Exists(lockPath))
            {
                return Comparison.None;
            }

            existingBytes = File.ReadAllBytes(lockPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Comparison(
                [$"the lock file could not be read and would be replaced: {exception.Message}"],
                []
            );
        }

        // The writer is canonical, so identical bytes mean there is nothing to do at all.
        if (existingBytes.AsSpan().SequenceEqual(ExtensionLockJson.ToBytes(resolved)))
        {
            return Comparison.None;
        }

        ExtensionLockJson.Reading existing = ExtensionLockJson.Read(
            System.Text.Encoding.UTF8.GetString(existingBytes)
        );
        IReadOnlyList<string> toolset = existing.Lock is { } parsed
            ? SingleFileLockDiff.ToolsetBetween(parsed, resolved)
            : [existing.Problem ?? "the lock file on disk could not be read and would be replaced"];

        // The same graph for another toolset: the .NET patch case. Rebuild, never ask.
        if (existing.GraphBytes.AsSpan().SequenceEqual(ExtensionLockJson.GraphBytes(resolved)))
        {
            return new Comparison([], toolset);
        }

        IReadOnlyList<string> graph = existing.Lock is { } onDisk
            ? SingleFileLockDiff.Between(onDisk, resolved)
            : ["the lock file on disk was refused and would be replaced"];
        return new Comparison(graph, [.. toolset, .. graph]);
    }

    private static string Denied(SingleFileExtension candidate) =>
        FormattableString.Invariant(
            $"the user did not trust '{candidate.Id}'; nothing was restored and nothing ran"
        );

    private static SingleFileRestoreOutcome Restored(
        SingleFileExtension candidate,
        ExtensionLock @lock,
        string lockPath,
        string? lockHash,
        IReadOnlyList<string> changes,
        IReadOnlyList<string> rebuildReasons
    ) =>
        new(
            SingleFileRestoreStatus.Restored,
            candidate with
            {
                Reason = null,
            },
            @lock,
            lockPath,
            lockHash,
            null,
            changes
        )
        {
            RebuildReasons = rebuildReasons,
        };

    private static SingleFileRestoreOutcome Fail(
        SingleFileExtension candidate,
        SingleFileRestoreStatus status,
        string message
    ) =>
        new(
            status,
            candidate with
            {
                State =
                    status == SingleFileRestoreStatus.Denied ? SingleFileExtensionState.TrustFailed
                    : status == SingleFileRestoreStatus.Failed
                        ? SingleFileExtensionState.RestoreFailed
                    : candidate.State,
                Reason = message,
            },
            null,
            null,
            null,
            message,
            []
        );

    private static bool Matches(ExtensionLock @lock, SingleFileTarget target) =>
        string.Equals(@lock.Toolset.Tfm, target.Tfm, StringComparison.Ordinal)
        && string.Equals(@lock.Toolset.Rid, target.Rid, StringComparison.Ordinal)
        && string.Equals(@lock.Toolset.Runtime, target.Runtime, StringComparison.Ordinal);

    private static SingleFileRestoreRequest Request(
        SingleFileExtension candidate,
        SingleFileTarget target
    ) =>
        new()
        {
            ExtensionId = candidate.Id!,
            ExtensionVersion = candidate.Version ?? "0.0.0",
            SourceDigest = candidate.SourceDigest,
            SourcePath = candidate.Path,
            Packages = candidate.Header.Packages,
            Target = target,
        };
}
