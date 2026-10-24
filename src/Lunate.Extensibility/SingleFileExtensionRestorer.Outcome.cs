namespace Lunate.Extensibility;

/// <summary>
/// The value-shaping half of the restorer: the question a decision needs, the lock a resolution
/// composes into, the lines that say what writing it would change, and the outcome rows. Split from
/// the flow so the order the spec cares about — trust, then lock, then engine — stays readable in
/// one screen.
/// </summary>
public sealed partial class SingleFileExtensionRestorer
{
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
    /// What writing <paramref name="resolved"/> would change. The comparison is over the canonical
    /// bytes, so a lock that says the same thing is not rewritten (and costs no question).
    /// </summary>
    private static IReadOnlyList<string> Compare(string lockPath, ExtensionLock resolved)
    {
        byte[] bytes = ExtensionLockJson.ToBytes(resolved);
        byte[] existing;
        try
        {
            if (!File.Exists(lockPath))
            {
                return [];
            }

            existing = File.ReadAllBytes(lockPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [$"the lock file could not be read and would be replaced: {exception.Message}"];
        }

        if (existing.AsSpan().SequenceEqual(bytes))
        {
            return [];
        }

        ExtensionLock? parsed = ExtensionLockJson.TryParse(
            System.Text.Encoding.UTF8.GetString(existing),
            out _
        );
        return SingleFileLockDiff.Between(parsed, resolved);
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
        IReadOnlyList<string> changes
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
        );

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
