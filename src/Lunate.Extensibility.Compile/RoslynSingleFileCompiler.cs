using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Lunate.Extensibility.Compile;

/// <summary>
/// Compiles a single-file extension in process with the Roslyn the product already ships. The
/// references are fixed by the request and nothing else: the host's contract assemblies, the
/// reference pack the lock names, and each locked package's compile assets — so no package's copy of
/// a contract can displace the host's, and no feed can be reached from here. The emit is
/// deterministic and carries no debug information, which is what makes the same source, lock and
/// toolset produce a byte-identical assembly on a rebuild instead of a new artifact.
/// </summary>
public sealed class RoslynSingleFileCompiler : ISingleFileCompiler
{
    /// <summary>
    /// Parses and compiles the request's source. A failure is a
    /// <see cref="SingleFileCompileResult"/> — the first diagnostic by position, or a problem
    /// outside the source — never an exception; the caller turns it into a
    /// <c>failed (compile)</c> row and writes nothing.
    /// </summary>
    public SingleFileCompileResult Compile(
        SingleFileCompileRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        // The header's `#:` lines are file-based-program directives to Roslyn, and without the
        // feature they are a hard error (CS9298). The scanner has already read them — they are the
        // declaration the host acts on — so the compiler only has to stop rejecting them; their
        // meaning stays the host's.
        CSharpParseOptions parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            DocumentationMode.None,
            SourceCodeKind.Regular
        ).WithFeatures([new KeyValuePair<string, string>("FileBasedProgram", "true")]);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            request.SourceText,
            parseOptions,
            path: request.SourcePath,
            cancellationToken: cancellationToken
        );

        List<MetadataReference> references = [];
        HashSet<string> taken = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, byte[] image) in HostContractReferences.All())
        {
            if (taken.Add(name))
            {
                references.Add(MetadataReference.CreateFromImage(image));
            }
        }

        string? problem = AddPack(request, references, taken);
        problem ??= AddPackages(request, references, taken);
        if (problem is not null)
        {
            return SingleFileCompileResult.Unusable(problem);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            request.AssemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                // Warnings are warnings: an unused local must not fail a working extension. Only
                // errors end a compile, and the first one by position is the one reported.
                optimizationLevel: OptimizationLevel.Release,
                // No PDB, no source path in the image, no timestamps: the MVID is derived from the
                // content, so the same inputs emit the same bytes.
                deterministic: true
            )
        );

        using var assembly = new MemoryStream();
        EmitResult emitted = compilation.Emit(assembly, cancellationToken: cancellationToken);
        return emitted.Success
            ? SingleFileCompileResult.Ok(assembly.ToArray())
            : SingleFileCompileResult.Failed(First(request, emitted.Diagnostics));
    }

    /// <summary>
    /// The reference pack's assemblies: <c>ref/&lt;tfm&gt;</c> from the pack's own folder, which is
    /// what a compile against the running runtime needs and what a published binary has no copy of.
    /// </summary>
    private static string? AddPack(
        SingleFileCompileRequest request,
        List<MetadataReference> references,
        HashSet<string> taken
    )
    {
        ExtensionLockReferencePack pack = request.Lock.Toolset.ReferencePack;
        if (pack.Assets.Count == 0)
        {
            return FormattableString.Invariant(
                $"the lock records no reference assemblies for {pack.Id} {pack.Version}; restore the extension again"
            );
        }

        string root = PackageRoot(request.GlobalPackagesFolder, pack.Id, pack.Version);
        return Add(pack.Assets, root, references, taken);
    }

    /// <summary>
    /// Each locked package's compile assets — <c>ref/</c> when it ships reference assemblies,
    /// <c>lib/</c> otherwise, as the lock recorded them. A package that ships its own copy of a
    /// contract contributes nothing: the host's copy is already referenced, and a second assembly
    /// with the same identity would be refused by the compiler.
    /// </summary>
    private static string? AddPackages(
        SingleFileCompileRequest request,
        List<MetadataReference> references,
        HashSet<string> taken
    )
    {
        foreach (ExtensionLockPackage package in request.Lock.Packages)
        {
            string root = PackageRoot(request.GlobalPackagesFolder, package.Id, package.Version);
            if (Add(package.Compile, root, references, taken) is { } problem)
            {
                return problem;
            }
        }

        return null;
    }

    /// <summary>
    /// Adds one package's assets, package-relative and forward-slashed as the lock records them. A
    /// missing file is reported by name: it means the packages folder disagrees with the lock, and
    /// compiling around it would produce an assembly that cannot load.
    /// </summary>
    private static string? Add(
        IReadOnlyList<string> assets,
        string root,
        List<MetadataReference> references,
        HashSet<string> taken
    )
    {
        foreach (string asset in assets)
        {
            string name = Path.GetFileNameWithoutExtension(asset);
            if (
                !string.Equals(Path.GetExtension(asset), ".dll", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            string path = Path.Combine(root, asset.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                return FormattableString.Invariant(
                    $"the compile reference '{asset}' is missing from '{root}'; restore the extension again"
                );
            }

            if (taken.Add(name))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return null;
    }

    /// <summary>NuGet's lowercase, version-normalized layout, the same one the restore wrote.</summary>
    private static string PackageRoot(string globalPackagesFolder, string id, string version) =>
        Path.Combine(globalPackagesFolder, id.ToLowerInvariant(), version.ToLowerInvariant());

    /// <summary>
    /// The first error by source position — the one a user has to read first — rendered in Roslyn's
    /// own form. Errors without a location (a metadata or emit failure) keep their message and fall
    /// back to the source path.
    /// </summary>
    private static SingleFileDiagnostic First(
        SingleFileCompileRequest request,
        IEnumerable<Diagnostic> diagnostics
    )
    {
        Diagnostic first =
            diagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .OrderBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line)
                .ThenBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Character)
                .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "the compile failed but reported no error diagnostic"
            );

        FileLinePositionSpan span = first.Location.GetLineSpan();
        string file = string.IsNullOrEmpty(span.Path) ? request.SourcePath : span.Path;
        return new SingleFileDiagnostic(
            file,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            first.Id,
            first.GetMessage(System.Globalization.CultureInfo.InvariantCulture)
        );
    }
}
