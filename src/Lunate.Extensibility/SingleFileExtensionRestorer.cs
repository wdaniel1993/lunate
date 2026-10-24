namespace Lunate.Extensibility;

/// <summary>
/// Turns a trusted single-file extension into a lock. The order is the contract: the bytes are
/// re-read, the trust digest is checked, and only then is a restore engine obtained — so a stale
/// file stops before a source repository or an HTTP client exists. The first successful restore
/// writes <c>extension.lock.json</c> into the extension's cache entry and binds its hash into the
/// trust record without a second question, because a restore downloads and executes nothing. A
/// restore that would change an existing lock reports the change and asks first.
/// </summary>
public sealed partial class SingleFileExtensionRestorer
{
    private readonly ExtensionHostOptions _options;
    private readonly ExtensionTrustStore _trust;
    private readonly SingleFileExtensionReader _reader;
    private readonly Func<ISingleFileRestoreEngine?> _engine;
    private readonly SingleFileTarget? _target;

    /// <summary>
    /// <paramref name="engine"/> is a factory, not an engine: the host hands over a delegate that
    /// constructs the NuGet-backed engine, and it is invoked only once a restore has to run. A null
    /// factory (or one that returns null) reports a restore failure instead of pretending.
    /// <paramref name="target"/> null means this process's toolset.
    /// </summary>
    public SingleFileExtensionRestorer(
        ExtensionHostOptions options,
        Func<ISingleFileRestoreEngine?>? engine = null,
        SingleFileTarget? target = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _trust = new ExtensionTrustStore(options);
        _reader = new SingleFileExtensionReader(options);
        _engine = engine ?? (static () => null);
        _target = target;
    }

    /// <summary>
    /// Ensures the extension has a lock that answers for its current bytes, asking
    /// <paramref name="prompt"/> for any decision it needs. Never throws for a restore problem: the
    /// outcome carries the status and the reason so <c>/extensions</c> can show it.
    /// </summary>
    public async Task<SingleFileRestoreOutcome> EnsureRestoredAsync(
        string path,
        ExtensionScope scope,
        IExtensionTrustPrompt? prompt,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SingleFileExtension candidate = _reader.ReadOne(path, scope);
        if (!candidate.Header.IsValid || candidate.Id is null)
        {
            return new SingleFileRestoreOutcome(
                SingleFileRestoreStatus.Invalid,
                candidate,
                null,
                null,
                null,
                candidate.Reason ?? "the header does not declare an extension",
                []
            );
        }

        SingleFileTarget target = _target ?? SingleFileTarget.Current();

        // The gate. Nothing below this point runs when the bytes are not the ones the user approved:
        // no engine, no source repository, no HttpClient, no feed request.
        SingleFileExtensionTrust trust = Evaluate(candidate);
        if (trust is SingleFileExtensionTrust.Unreadable)
        {
            return Fail(
                candidate,
                SingleFileRestoreStatus.TrustRequired,
                "the trust record for this file is unreadable; remove its entry from trust.json "
                    + "and trust the file again"
            );
        }

        if (trust is SingleFileExtensionTrust.Stale or SingleFileExtensionTrust.Untrusted)
        {
            SingleFileTrustRequest request = Question(
                candidate,
                changed: trust is SingleFileExtensionTrust.Stale
            );
            if (prompt is null)
            {
                return Fail(
                    candidate,
                    SingleFileRestoreStatus.TrustRequired,
                    "the file is not trusted for these bytes; it must be trusted before it is restored"
                );
            }

            if (
                !await prompt
                    .ApproveSingleFileAsync(request, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                return Fail(candidate, SingleFileRestoreStatus.Denied, Denied(candidate));
            }

            if (!TryRecord(candidate.Path, candidate.Digest))
            {
                return Fail(
                    candidate,
                    SingleFileRestoreStatus.Failed,
                    "the trust decision could not be written; the file was not restored"
                );
            }
        }

        // A lock that answers for these bytes and this toolset is the whole restore: no feed.
        if (
            candidate.Lock is { } current
            && Matches(current, target)
            && candidate.LockHash is { } currentHash
        )
        {
            return Restored(
                candidate,
                current,
                SingleFileExtensionPaths.LockPath(_options.StorePath, candidate.Id),
                currentHash,
                []
            );
        }

        ISingleFileRestoreEngine? engine = _engine();
        if (engine is null)
        {
            return Fail(
                candidate,
                SingleFileRestoreStatus.Failed,
                "no restore engine is available in this build, so the declared packages cannot be "
                    + "restored"
            );
        }

        SingleFileRestoreResult result;
        try
        {
            result = await engine
                .RestoreAsync(Request(candidate, target), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(
                candidate,
                SingleFileRestoreStatus.Failed,
                $"restore failed: {exception.Message}"
            );
        }

        ExtensionLock resolved = Compose(candidate, target, result);
        string lockPath = SingleFileExtensionPaths.LockPath(_options.StorePath, candidate.Id);
        IReadOnlyList<string> changes = Compare(lockPath, resolved);

        // An existing lock is never rewritten behind the user's back: the question carries what
        // would change, and a refusal leaves the file exactly as it was.
        if (changes.Count > 0 && prompt is null)
        {
            return new SingleFileRestoreOutcome(
                SingleFileRestoreStatus.TrustRequired,
                candidate,
                resolved,
                lockPath,
                null,
                "restoring this file would change its lock file; the change must be approved before "
                    + "it is written",
                changes
            );
        }

        if (changes.Count > 0)
        {
            SingleFileTrustRequest request = Question(candidate, changed: true) with
            {
                LockChanges = changes,
            };
            if (
                !await prompt!
                    .ApproveSingleFileAsync(request, cancellationToken)
                    .ConfigureAwait(false)
            )
            {
                return new SingleFileRestoreOutcome(
                    SingleFileRestoreStatus.Denied,
                    candidate,
                    resolved,
                    lockPath,
                    null,
                    Denied(candidate),
                    changes
                );
            }
        }

        byte[] bytes = ExtensionLockJson.ToBytes(resolved);
        string lockHash = ExtensionLockJson.Hash(bytes);
        string binding;
        try
        {
            byte[] source = File.ReadAllBytes(candidate.Path);
            if (
                !string.Equals(
                    SingleFileExtensionDigest.ComputeSource(
                        candidate.Id,
                        candidate.Header.Packages,
                        source
                    ),
                    candidate.SourceDigest,
                    StringComparison.Ordinal
                )
            )
            {
                return Fail(
                    candidate,
                    SingleFileRestoreStatus.Failed,
                    "the file changed while it was being restored; nothing was bound — restore it again"
                );
            }

            binding = SingleFileExtensionDigest.Compute(
                candidate.Id,
                candidate.Header.Packages,
                source,
                bytes
            );
            ExtensionLockJson.WriteTo(lockPath, resolved);
            if (!TryRecord(candidate.Path, binding))
            {
                return Fail(
                    candidate with
                    {
                        Lock = null,
                        LockHash = null,
                    },
                    SingleFileRestoreStatus.Failed,
                    "the lock was written but its hash could not be bound into the trust record; "
                        + "the file will ask again"
                );
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(
                candidate with
                {
                    Lock = null,
                    LockHash = null,
                },
                SingleFileRestoreStatus.Failed,
                $"the lock could not be written: {exception.Message}"
            );
        }

        SingleFileExtension restored = _reader.ReadOne(candidate.Path, candidate.Scope);
        return Restored(restored, resolved, lockPath, lockHash, changes);
    }

    /// <summary>
    /// Writes one trust record; false when the store cannot be written. A restore that cannot bind
    /// its decision must not look like it succeeded, and it must not surface a raw IO exception
    /// from a path the user asked for.
    /// </summary>
    private bool TryRecord(string path, string digest)
    {
        try
        {
            _trust.RecordSingleFileTrust(path, digest);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private SingleFileExtensionTrust Evaluate(SingleFileExtension candidate)
    {
        try
        {
            return _trust.EvaluateSingleFileTrust(candidate.Path, candidate.Digest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SingleFileExtensionTrust.Unreadable;
        }
    }
}
