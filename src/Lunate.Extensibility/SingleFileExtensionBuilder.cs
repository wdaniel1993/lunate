using System.Text;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>How one build attempt ended.</summary>
public enum SingleFileBuildStatus
{
    /// <summary>Compiled now: a new build directory was written.</summary>
    Built,

    /// <summary>Already compiled for exactly these inputs: the existing build was reused.</summary>
    CacheHit,

    /// <summary>The bytes are not the ones the user approved; nothing was restored or compiled.</summary>
    TrustRequired,

    /// <summary>The user refused the trust question.</summary>
    Denied,

    /// <summary>The header refuses the file, so there is nothing to build.</summary>
    Invalid,

    /// <summary>A restore failed (or no engine is available), so no lock could answer for the file.</summary>
    RestoreFailed,

    /// <summary>The compile failed: nothing was written, and <see cref="SingleFileBuildOutcome.Diagnostic"/> says why.</summary>
    CompileFailed,
}

/// <summary>
/// What one build attempt produced: the state <c>/extensions</c> and the CLI report, the build the
/// entry now points at, and — for a failed compile — the first diagnostic. A failed compile leaves
/// no build directory behind, so <see cref="Build"/> is null exactly when nothing was written;
/// <see cref="Extension"/> still carries the state and reason, so one row is always renderable.
/// </summary>
public sealed record SingleFileBuildOutcome
{
    public required SingleFileBuildStatus Status { get; init; }

    public required SingleFileExtension Extension { get; init; }

    public SingleFileBuild? Build { get; init; }

    public SingleFileDiagnostic? Diagnostic { get; init; }

    public string? Reason { get; init; }

    /// <summary>
    /// What the restore had to rewrite to answer for this toolset (the toolset half only). It is a
    /// rebuild reason, never a trust question.
    /// </summary>
    public IReadOnlyList<string> RebuildReasons { get; init; } = [];

    /// <summary>True when there is now a built assembly for these inputs, new or reused.</summary>
    public bool Succeeded =>
        Status is SingleFileBuildStatus.Built or SingleFileBuildStatus.CacheHit;
}

/// <summary>
/// The step both the CLI trigger and <c>/extensions reload</c> call (phase 3 calls this same
/// service): restore if the lock cannot answer, then compile against that lock, then keep the build.
/// The order is the contract — trust, then a lock that answers for the trusted bytes, then the cache,
/// then the compiler — so a stale file reaches neither a feed nor a compiler, and a lock that is not
/// the trusted one never compiles anything. A cache hit is a directory check: the same source, the
/// same lock (both halves), the same host contract version and the same toolset produce the same key,
/// and the assembly at that key is the answer without the compiler running.
/// </summary>
public sealed class SingleFileExtensionBuilder
{
    private readonly ExtensionHostOptions _options;
    private readonly SingleFileExtensionRestorer _restorer;
    private readonly ExtensionTrustStore _trust;
    private readonly Func<ISingleFileCompiler?> _compiler;
    private readonly SingleFileTarget _target;
    private readonly string _packagesFolder;

    /// <summary>
    /// <paramref name="compiler"/> is a factory for the same reason the restore engine is: the
    /// Roslyn assemblies are loaded by the first compile, not by composing a session.
    /// <paramref name="globalPackagesFolder"/> null means NuGet's own default — <c>NUGET_PACKAGES</c>
    /// when set, otherwise <c>~/.nuget/packages</c> — which is where the restore engine installs.
    /// <paramref name="target"/> null means this process's toolset.
    /// </summary>
    public SingleFileExtensionBuilder(
        ExtensionHostOptions options,
        Func<ISingleFileRestoreEngine?>? engine = null,
        Func<ISingleFileCompiler?>? compiler = null,
        SingleFileTarget? target = null,
        string? globalPackagesFolder = null
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _trust = new ExtensionTrustStore(options);
        _restorer = new SingleFileExtensionRestorer(options, engine, target);
        _compiler = compiler ?? (static () => null);
        _target = target ?? SingleFileTarget.Current();
        string? configured = string.IsNullOrWhiteSpace(globalPackagesFolder)
            ? options.GlobalPackagesFolder
            : globalPackagesFolder;
        _packagesFolder = string.IsNullOrWhiteSpace(configured)
            ? SingleFilePackages.DefaultFolder()
            : Path.GetFullPath(configured);
    }

    /// <summary>The toolset every build is keyed by and compiled for.</summary>
    public SingleFileTarget Target => _target;

    /// <summary>Where the compile resolves the locked packages and the reference pack from.</summary>
    public string GlobalPackagesFolder => _packagesFolder;

    /// <summary>
    /// Builds one file: ensures a lock that answers for the trusted bytes (restoring only when it
    /// does not), then compiles it against that lock, or reuses the build the key already names.
    /// Never throws for a trust, restore, compile or cache problem — the outcome carries the state
    /// and the reason, so <c>/extensions</c> can show it and the CLI can exit non-zero.
    /// </summary>
    public async Task<SingleFileBuildOutcome> BuildAsync(
        string path,
        ExtensionScope scope,
        IExtensionTrustPrompt? prompt = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        SingleFileRestoreOutcome restored = await _restorer
            .EnsureRestoredAsync(path, scope, prompt, cancellationToken)
            .ConfigureAwait(false);

        if (
            restored.Status != SingleFileRestoreStatus.Restored
            || restored.Candidate.Id is not { } id
            || restored.Lock is not { } @lock
            || restored.LockHash is not { } lockHash
        )
        {
            return NotBuilt(restored);
        }

        SingleFileExtension candidate = restored.Candidate;

        // The compile gate the requirement names: the lock must be the one the trusted digest
        // covers. The restorer only reports Restored for such a lock, and this re-checks it against
        // the bytes as they are now, so an edit between the two reads stops here instead of
        // compiling something nobody approved.
        if (!IsTrusted(candidate))
        {
            return Fail(
                SingleFileBuildStatus.TrustRequired,
                candidate with
                {
                    State = SingleFileExtensionState.Stale,
                    Reason =
                        "the file changed after it was restored; it must be trusted again before it "
                        + "is compiled",
                },
                reason: candidate.Reason
                    ?? "the file is not trusted for exactly these bytes; nothing was compiled"
            );
        }

        string key = SingleFileBuildKey.Compute(
            candidate.SourceDigest,
            lockHash,
            ExtensionApi.Current,
            _target.Tfm,
            _target.Rid
        );
        var cache = new SingleFileBuildCache(_options.StorePath);

        if (cache.Find(id, key) is { } hit)
        {
            Try(() => cache.MarkCurrent(hit));
            return new SingleFileBuildOutcome
            {
                Status = SingleFileBuildStatus.CacheHit,
                Extension = candidate with
                {
                    State = SingleFileExtensionState.Built,
                    Reason = null,
                },
                Build = hit,
                RebuildReasons = restored.RebuildReasons,
            };
        }

        string source;
        try
        {
            byte[] bytes = File.ReadAllBytes(candidate.Path);
            if (
                !string.Equals(
                    SingleFileExtensionDigest.ComputeSource(id, candidate.Header.Packages, bytes),
                    candidate.SourceDigest,
                    StringComparison.Ordinal
                )
            )
            {
                return Fail(
                    SingleFileBuildStatus.TrustRequired,
                    candidate with
                    {
                        State = SingleFileExtensionState.Stale,
                        Reason =
                            "the file changed while it was being built; nothing was built — build "
                            + "it again",
                    },
                    reason: "the file changed while it was being built; nothing was built"
                );
            }

            source = Decode(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(
                SingleFileBuildStatus.CompileFailed,
                candidate with
                {
                    State = SingleFileExtensionState.CompileFailed,
                    Reason = FormattableString.Invariant(
                        $"the file could not be read for the compile: {exception.Message}"
                    ),
                },
                reason: FormattableString.Invariant(
                    $"the file could not be read for the compile: {exception.Message}"
                )
            );
        }

        ISingleFileCompiler? compiler = _compiler();
        if (compiler is null)
        {
            return Fail(
                SingleFileBuildStatus.CompileFailed,
                candidate with
                {
                    State = SingleFileExtensionState.CompileFailed,
                    Reason =
                        "no compiler is available in this build, so the extension cannot be "
                        + "compiled",
                },
                reason: "no compiler is available in this build, so the extension cannot be compiled"
            );
        }

        SingleFileCompileResult compiled;
        try
        {
            compiled = compiler.Compile(Request(candidate, @lock, source), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(
                SingleFileBuildStatus.CompileFailed,
                candidate with
                {
                    State = SingleFileExtensionState.CompileFailed,
                    Reason = FormattableString.Invariant(
                        $"the compile failed: {exception.Message}"
                    ),
                },
                reason: FormattableString.Invariant($"the compile failed: {exception.Message}")
            );
        }

        if (!compiled.Succeeded)
        {
            // Nothing is written: no build directory, no pointer, no trimmed history. A file that
            // does not compile leaves the cache exactly as it was, so the previous build — if any —
            // is still the one the entry points at.
            string reason = compiled.Reason;
            return Fail(
                SingleFileBuildStatus.CompileFailed,
                candidate with
                {
                    State = SingleFileExtensionState.CompileFailed,
                    Reason = reason,
                },
                reason,
                compiled.FirstError
            );
        }

        SingleFileBuild build;
        try
        {
            build = cache.Save(
                id,
                key,
                compiled.Assembly,
                Manifest(candidate, @lock, key, lockHash)
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(
                SingleFileBuildStatus.CompileFailed,
                candidate with
                {
                    State = SingleFileExtensionState.CompileFailed,
                    Reason = FormattableString.Invariant(
                        $"the build could not be written: {exception.Message}"
                    ),
                },
                reason: FormattableString.Invariant(
                    $"the build could not be written: {exception.Message}"
                )
            );
        }

        return new SingleFileBuildOutcome
        {
            Status = SingleFileBuildStatus.Built,
            Extension = candidate with { State = SingleFileExtensionState.Built, Reason = null },
            Build = build,
            RebuildReasons = restored.RebuildReasons,
        };
    }

    private SingleFileCompileRequest Request(
        SingleFileExtension candidate,
        ExtensionLock @lock,
        string source
    ) =>
        new()
        {
            ExtensionId = candidate.Id!,
            ExtensionVersion = candidate.Version ?? "0.0.0",
            SourcePath = candidate.Path,
            SourceText = source,
            AssemblyName = candidate.Id!,
            Target = _target,
            GlobalPackagesFolder = _packagesFolder,
            Lock = @lock,
            Generators = candidate.Header.Generators,
        };

    /// <summary>
    /// The manifest a build carries: its key's inputs in full and the resolution phase 3 loads
    /// from. Package-relative assets only, so a build directory can be read on any machine.
    /// </summary>
    private static SingleFileBuildManifest Manifest(
        SingleFileExtension candidate,
        ExtensionLock @lock,
        string key,
        string lockHash
    ) =>
        new()
        {
            Key = key,
            Assembly = SingleFileExtensionPaths.AssemblyFileName(candidate.Id!),
            ExtensionId = candidate.Id!,
            ExtensionVersion = candidate.Version ?? "0.0.0",
            ApiVersion = ExtensionApi.Current,
            Tfm = @lock.Toolset.Tfm,
            Rid = @lock.Toolset.Rid,
            SourceHash = candidate.SourceDigest,
            LockHash = lockHash,
            Packages =
            [
                .. @lock.Packages.Select(package => new SingleFileBuildPackage
                {
                    Id = package.Id,
                    Version = package.Version,
                    Runtime = package.Runtime,
                    Native = package.Native,
                }),
            ],
        };

    private bool IsTrusted(SingleFileExtension candidate)
    {
        try
        {
            return _trust.EvaluateSingleFileTrust(candidate.Path, candidate.Digest)
                == SingleFileExtensionTrust.Trusted;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// A restore-side outcome, carried through as the same row shape: the state the restorer set
    /// and the reason it set are what the user needs to read.
    /// </summary>
    private static SingleFileBuildOutcome NotBuilt(SingleFileRestoreOutcome restored) =>
        new()
        {
            Status = restored.Status switch
            {
                SingleFileRestoreStatus.Denied => SingleFileBuildStatus.Denied,
                SingleFileRestoreStatus.TrustRequired => SingleFileBuildStatus.TrustRequired,
                SingleFileRestoreStatus.Invalid => SingleFileBuildStatus.Invalid,
                _ => SingleFileBuildStatus.RestoreFailed,
            },
            Extension = restored.Candidate,
            Reason = restored.Message ?? restored.Candidate.Reason,
            RebuildReasons = restored.RebuildReasons,
        };

    private static SingleFileBuildOutcome Fail(
        SingleFileBuildStatus status,
        SingleFileExtension extension,
        string reason,
        SingleFileDiagnostic? diagnostic = null
    ) =>
        new()
        {
            Status = status,
            Extension = extension,
            Reason = reason,
            Diagnostic = diagnostic,
        };

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The pointer is an optimisation for the next reader; a build that exists is still
            // usable, and the next successful build rewrites the pointer anyway.
        }
    }

    /// <summary>The source text, without a byte-order mark the compiler would read as content.</summary>
    private static string Decode(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }
}
