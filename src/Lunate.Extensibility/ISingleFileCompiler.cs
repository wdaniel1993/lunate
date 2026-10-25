namespace Lunate.Extensibility;

/// <summary>
/// One compile request: the bytes to compile, the lock that supplies every reference, and the
/// toolset. It carries no package declarations and no ranges — a compile runs against a lock the
/// trust decision already covers, so nothing here can re-resolve a graph or reach a feed.
/// </summary>
public sealed record SingleFileCompileRequest
{
    /// <summary>The declared extension id; it becomes the assembly's simple name.</summary>
    public required string ExtensionId { get; init; }

    /// <summary>The declared extension version, recorded in the assembly's metadata.</summary>
    public required string ExtensionVersion { get; init; }

    /// <summary>The absolute path of the <c>.cs</c> file; diagnostics name it as written.</summary>
    public required string SourcePath { get; init; }

    /// <summary>The source text, exactly the bytes the trust digest covers.</summary>
    public required string SourceText { get; init; }

    public required string AssemblyName { get; init; }

    public required SingleFileTarget Target { get; init; }

    /// <summary>Where the locked packages and the reference pack are installed.</summary>
    public required string GlobalPackagesFolder { get; init; }

    /// <summary>The lock the compile reads its references from. Never a re-resolution.</summary>
    public required ExtensionLock Lock { get; init; }

    /// <summary>
    /// True when the header opted in to the declared packages' analyzers and source generators
    /// (<c>#:generators on</c>). False means the compile references their assemblies and loads no
    /// build-time code — the default, and the only behaviour this phase implements.
    /// </summary>
    public bool Generators { get; init; }
}

/// <summary>
/// One compile diagnostic, in the shape the requirement names: the file, the 1-based line and
/// column, the compiler id and the message. <see cref="ToString"/> renders Roslyn's own form —
/// <c>path(line,col): error CS0103: message</c> — so the row a user reads matches the message a
/// compiler would have printed.
/// </summary>
public sealed record SingleFileDiagnostic(
    string File,
    int Line,
    int Column,
    string Id,
    string Message
)
{
    public override string ToString() =>
        Line > 0
            ? FormattableString.Invariant($"{File}({Line},{Column}): error {Id}: {Message}")
            : FormattableString.Invariant($"{File}: error {Id}: {Message}");
}

/// <summary>
/// What one compile produced: the assembly's bytes, or why there is none. A failure is either a
/// compiler diagnostic (<see cref="FirstError"/>, the first error by position) or a problem outside
/// the source (<see cref="Problem"/>: a reference the packages folder does not hold, a manifest
/// mismatch). Exactly one of them is set when the compile did not succeed.
/// </summary>
public sealed record SingleFileCompileResult
{
    public byte[]? Assembly { get; init; }

    public SingleFileDiagnostic? FirstError { get; init; }

    public string? Problem { get; init; }

    public bool Succeeded => Assembly is not null;

    /// <summary>The one-line reason a failed compile reports.</summary>
    public string Reason =>
        FirstError?.ToString() ?? Problem ?? "the compile failed and reported no diagnostic";

    public static SingleFileCompileResult Ok(byte[] assembly) => new() { Assembly = assembly };

    public static SingleFileCompileResult Failed(SingleFileDiagnostic firstError) =>
        new() { FirstError = firstError };

    public static SingleFileCompileResult Unusable(string problem) => new() { Problem = problem };
}

/// <summary>
/// The compile seam. <see cref="Extensibility"/> declares it and owns the cache and the order
/// (trust, then lock, then compile); the Roslyn implementation lives in
/// <c>Lunate.Extensibility.Compile</c>, the only project that references the compiler, exactly as
/// the NuGet engine lives behind <see cref="ISingleFileRestoreEngine"/>. The host hands over a
/// factory so a session that never compiles never loads the compiler.
/// </summary>
public interface ISingleFileCompiler
{
    SingleFileCompileResult Compile(
        SingleFileCompileRequest request,
        CancellationToken cancellationToken
    );
}
