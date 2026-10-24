namespace Lunate.Extensibility;

/// <summary>One feed a restore may read, by name and URL; the name is what the lock records.</summary>
public sealed record SingleFilePackageSource(string Name, string Url);

/// <summary>
/// One restore request: what to resolve, for which toolset, and nothing else. It carries the
/// declared packages as written (a range included), so the engine sees the same bytes the user
/// approved, and the source-level digest so the lock it produces can be tied to them.
/// </summary>
public sealed record SingleFileRestoreRequest
{
    public required string ExtensionId { get; init; }

    public required string ExtensionVersion { get; init; }

    public required string SourceDigest { get; init; }

    /// <summary>The absolute path of the <c>.cs</c> file; the project <c>nuget.config</c> chain starts here.</summary>
    public required string SourcePath { get; init; }

    /// <summary>Every <c>#:package</c> declaration, in header order, exactly as written.</summary>
    public IReadOnlyList<ExtensionPackageReference> Packages { get; init; } = [];

    public required SingleFileTarget Target { get; init; }
}

/// <summary>
/// What a restore resolved: the locked packages (exact version, SHA-512, assets), the reference pack
/// the compile will use, the feeds it read and where the packages live. The engine is a seam: the
/// NuGet client libraries sit behind it, so nothing that composes a session loads them.
/// </summary>
public sealed record SingleFileRestoreResult
{
    public IReadOnlyList<ExtensionLockPackage> Packages { get; init; } = [];

    public required ExtensionLockReferencePack ReferencePack { get; init; }

    public IReadOnlyList<string> Sources { get; init; } = [];

    public required string GlobalPackagesFolder { get; init; }
}

/// <summary>
/// The restore seam. Implementations may use the NuGet client libraries; the host only ever calls
/// this after a trust decision, and only when a lock has to be produced.
/// </summary>
public interface ISingleFileRestoreEngine
{
    Task<SingleFileRestoreResult> RestoreAsync(
        SingleFileRestoreRequest request,
        CancellationToken cancellationToken
    );
}
