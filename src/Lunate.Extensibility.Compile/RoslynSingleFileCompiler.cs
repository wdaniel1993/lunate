using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;

namespace Lunate.Extensibility.Compile;

/// <summary>
/// Compiles a single-file extension in process with the Roslyn the product already ships. The
/// references are fixed by the request and nothing else: the host's contract assemblies, the
/// reference pack the lock names, and each locked package's compile assets — so no package's copy of
/// a contract can displace the host's, and no feed can be reached from here. The emit is
/// deterministic and carries no debug information, which is what makes the same source, lock and
/// toolset produce a byte-identical assembly on a rebuild instead of a new artifact.
/// <para>
/// Build-time code — the packages' analyzers and source generators — is loaded only when the header
/// opted in (<c>#:generators on</c>): those assemblies execute inside this process, which is a
/// decision the user makes, not one the compile makes for them. Without the opt-in nothing under
/// <c>analyzers/</c> is opened, and the lock still records what was skipped so the view can say so.
/// </para>
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

        // The opt-in is the whole difference between the common compile and one that runs a
        // package's build-time code: without it the analyzer assets are never opened.
        if (request.Generators)
        {
            SingleFileCompileResult? buildTimeCode = RunBuildTimeCode(
                request,
                ref compilation,
                parseOptions,
                cancellationToken
            );
            if (buildTimeCode is not null)
            {
                return buildTimeCode;
            }
        }

        using var assembly = new MemoryStream();
        EmitResult emitted = compilation.Emit(assembly, cancellationToken: cancellationToken);
        return emitted.Success
            ? SingleFileCompileResult.Ok(assembly.ToArray())
            : SingleFileCompileResult.Failed(First(request, emitted.Diagnostics));
    }

    /// <summary>
    /// Loads the locked packages' build-time code and folds it into the compilation: each package's
    /// <c>analyzers/</c> assembly becomes an <see cref="AnalyzerFileReference"/>, its generators run
    /// through <see cref="CSharpGeneratorDriver"/> so the trees they emit are compiled in, and its
    /// analyzers run over the result through <see cref="Compilation.WithAnalyzers"/> so an analyzer
    /// that reports an error ends the compile the way it would in a project build.
    /// <para>
    /// Only reached when the header says <c>#:generators on</c>. A missing analyzer asset, an
    /// assembly that will not load and a diagnostic a generator reports are all returned as a
    /// result — never an exception — because the caller's job is to write a <c>failed (compile)</c>
    /// row and nothing else.
    /// </para>
    /// </summary>
    private static SingleFileCompileResult? RunBuildTimeCode(
        SingleFileCompileRequest request,
        ref CSharpCompilation compilation,
        CSharpParseOptions parseOptions,
        CancellationToken cancellationToken
    )
    {
        var loader = new BuildTimeAssemblyLoader();
        List<AnalyzerFileReference> references = [];
        foreach (ExtensionLockPackage package in request.Lock.Packages)
        {
            if (package.Analyzers.Count == 0)
            {
                continue;
            }

            string root = PackageRoot(request.GlobalPackagesFolder, package.Id, package.Version);
            foreach (string asset in package.Analyzers)
            {
                string path = Path.Combine(root, asset.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    return SingleFileCompileResult.Unusable(
                        FormattableString.Invariant(
                            $"the build-time assembly '{asset}' is missing from '{root}'; restore the extension again"
                        )
                    );
                }

                references.Add(new AnalyzerFileReference(path, loader));
            }
        }

        if (references.Count == 0)
        {
            return null;
        }

        ImmutableArray<ISourceGenerator> generators;
        ImmutableArray<DiagnosticAnalyzer> analyzers;
        try
        {
            generators =
            [
                .. references.SelectMany(reference =>
                    reference.GetGenerators(LanguageNames.CSharp)
                ),
            ];
            analyzers =
            [
                .. references.SelectMany(reference => reference.GetAnalyzers(LanguageNames.CSharp)),
            ];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return SingleFileCompileResult.Unusable(
                FormattableString.Invariant(
                    $"the build-time code from the locked packages could not be loaded: {exception.Message}"
                )
            );
        }

        if (generators.Length > 0)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                generators,
                parseOptions: parseOptions
            );
            driver = driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation generated,
                out ImmutableArray<Diagnostic> generatorDiagnostics
            );
            compilation = (CSharpCompilation)generated;

            // A generator's own error is a compile failure with a diagnostic like any other; a
            // warning from one is a warning.
            if (FirstError(request, generatorDiagnostics) is { } generatorError)
            {
                return SingleFileCompileResult.Failed(generatorError);
            }
        }

        if (analyzers.Length > 0)
        {
            ImmutableArray<Diagnostic> analyzed;
            try
            {
                analyzed = compilation
                    .WithAnalyzers(analyzers)
                    .GetAllDiagnosticsAsync(cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return SingleFileCompileResult.Unusable(
                    FormattableString.Invariant(
                        $"the locked packages' analyzers could not be run: {exception.Message}"
                    )
                );
            }

            if (FirstError(request, analyzed) is { } analyzerError)
            {
                return SingleFileCompileResult.Failed(analyzerError);
            }
        }

        return null;
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
    /// own form. Errors without a location (a metadata or emit failure, a generator's own error)
    /// keep their message and fall back to the source path. Null when there is no error at all,
    /// which is what the emit path turns into an invariant failure.
    /// </summary>
    private static SingleFileDiagnostic? FirstError(
        SingleFileCompileRequest request,
        IEnumerable<Diagnostic> diagnostics
    )
    {
        Diagnostic? first = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .OrderBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Character)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        FileLinePositionSpan span = first.Location.GetLineSpan();
        string file = string.IsNullOrEmpty(span.Path) ? request.SourcePath : span.Path;
        return new SingleFileDiagnostic(
            file,
            span.StartLinePosition.Line + 1,
            span.StartLinePosition.Character + 1,
            first.Id,
            first.GetMessage(CultureInfo.InvariantCulture)
        );
    }

    /// <summary>The emit failed, so an error diagnostic has to exist; anything else is a bug here.</summary>
    private static SingleFileDiagnostic First(
        SingleFileCompileRequest request,
        IEnumerable<Diagnostic> diagnostics
    ) =>
        FirstError(request, diagnostics)
        ?? throw new InvalidOperationException(
            "the compile failed but reported no error diagnostic"
        );

    /// <summary>
    /// Loads one compile's build-time code. <see cref="Assembly.LoadFrom(string)"/> is deliberate:
    /// the packages folder is not a probing path, and a generator's dependency on
    /// <c>Microsoft.CodeAnalysis</c> must bind to the Roslyn already in this process — a second copy
    /// would hand the driver objects it cannot recognise. Reached only when the header opted in,
    /// which is the decision the trust prompt names; a process that compiles a file without the
    /// directive never gets here and loads nothing.
    /// </summary>
    private sealed class BuildTimeAssemblyLoader : IAnalyzerAssemblyLoader
    {
        /// <summary>
        /// A dependency's location. Nothing to record: <see cref="Assembly.LoadFrom(string)"/> probes
        /// the assembly's own directory, and the host's assemblies are already resolvable.
        /// </summary>
        public void AddDependencyLocation(string fullPath) { }

        public Assembly LoadFromPath(string fullPath) => Assembly.LoadFrom(fullPath);
    }
}
