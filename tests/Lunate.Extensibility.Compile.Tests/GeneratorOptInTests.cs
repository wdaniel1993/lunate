using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Lunate.Extensibility;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 2.13: build-time code is off unless the header says <c>#:generators on</c>. The red cases are
/// the two halves the requirement names — with the opt-in, the packages' generator actually runs and
/// the trees it emits are compiled in; without it, no generator assembly is loaded at all — plus the
/// analyzer half, because "analyzers and source generators" is the phrase the prompt uses and an
/// analyzer that reports an error has to fail the compile the way it would in a project build.
/// </summary>
public sealed class GeneratorOptInTests
{
    /// <summary>
    /// A generator and an analyzer in one assembly, the way a package ships them: the generator
    /// emits <c>Acme.Generated.Marker</c> (which the extension below cannot compile without), and
    /// the analyzer reports <c>ACME001</c> — but only when the source declares a
    /// <c>ForbiddenMarker</c> type, so the two cases stay independent.
    /// </summary>
    private const string BuildTimeCode = """
        using System.Collections.Immutable;
        using Microsoft.CodeAnalysis;
        using Microsoft.CodeAnalysis.Diagnostics;

        namespace Acme.BuildTime
        {
            [Generator]
            public sealed class MarkerGenerator : ISourceGenerator
            {
                public void Initialize(GeneratorInitializationContext context) { }

                public void Execute(GeneratorExecutionContext context)
                {
                    context.AddSource(
                        "Acme.Generated.Marker.g.cs",
                        "namespace Acme.Generated { public static class Marker { public static string Value => \"generated\"; } }"
                    );
                }
            }

            [DiagnosticAnalyzer(LanguageNames.CSharp)]
            public sealed class ForbiddenAnalyzer : DiagnosticAnalyzer
            {
                private static readonly DiagnosticDescriptor Rule = new(
                    "ACME001",
                    "Forbidden type",
                    "The type '{0}' is not allowed here",
                    "Acme",
                    DiagnosticSeverity.Error,
                    isEnabledByDefault: true
                );

                public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

                public override void Initialize(AnalysisContext context)
                {
                    context.EnableConcurrentExecution();
                    context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
                    context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
                }

                private static void Analyze(SymbolAnalysisContext context)
                {
                    if (context.Symbol.Name == "ForbiddenMarker")
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(Rule, context.Symbol.Locations[0], context.Symbol.Name)
                        );
                    }
                }
            }
        }
        """;

    /// <summary>The extension: it names the generated type, so it compiles only if the tree exists.</summary>
    private const string Extension = """
        using Lunate.Extensibility.Abstractions;

        public static class Uses
        {
            public static string Value => Acme.Generated.Marker.Value;
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void An_opted_in_file_compiles_against_the_tree_its_generator_emits()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage buildTime = fixture.AddAnalyzerPackage(
            "Acme.BuildTime",
            "1.0.0",
            BuildTimeCode,
            Ct
        );

        SingleFileCompileResult result = fixture.Compile(
            Request(fixture, Extension, fixture.Lock(buildTime)) with
            {
                Generators = true,
            },
            Ct
        );

        byte[] assembly = CompileFixture.Assembly(result);
        Assembly loaded = Assembly.Load(assembly);
        Type marker = loaded.GetType("Acme.Generated.Marker")!;
        Assert.NotNull(marker);
        Assert.Equal("generated", marker.GetProperty("Value")!.GetValue(null));

        // The generated type is in the emitted image, not merely in memory: the reference to
        // Acme.Generated survives into metadata.
        Assert.Contains("Acme.Generated", TypeNames(assembly), StringComparer.Ordinal);
    }

    [Fact]
    public void Without_the_opt_in_no_generator_runs_and_no_build_time_assembly_is_loaded()
    {
        using CompileFixture fixture = CompileFixture.Create();
        // A distinct assembly name per case, so a previous test in this process cannot have loaded it.
        const string Name = "Acme.BuildTime.Off";
        ExtensionLockPackage buildTime = fixture.AddAnalyzerPackage(
            Name,
            "1.0.0",
            BuildTimeCode,
            Ct
        );

        SingleFileCompileResult result = fixture.Compile(
            Request(fixture, Extension, fixture.Lock(buildTime)),
            Ct
        );

        Assert.False(result.Succeeded);
        // The generated type simply is not there, which is the compiler's own way of saying the
        // generator was never asked to run.
        Assert.Equal("CS0103", Assert.IsType<SingleFileDiagnostic>(result.FirstError).Id);
        Assert.DoesNotContain(
            Name,
            AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name),
            StringComparer.Ordinal
        );
    }

    [Fact]
    public void An_opted_in_file_fails_the_compile_when_a_locked_analyzer_reports_an_error()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage buildTime = fixture.AddAnalyzerPackage(
            "Acme.BuildTime.Analyzer",
            "1.0.0",
            BuildTimeCode,
            Ct
        );
        string source = """
            public sealed class ForbiddenMarker
            {
            }
            """;

        SingleFileCompileResult result = fixture.Compile(
            Request(fixture, source, fixture.Lock(buildTime)) with
            {
                Generators = true,
            },
            Ct
        );

        Assert.False(result.Succeeded);
        SingleFileDiagnostic error = Assert.IsType<SingleFileDiagnostic>(result.FirstError);
        Assert.Equal("ACME001", error.Id);
        Assert.Equal(1, error.Line);
        Assert.Contains("ForbiddenMarker", error.Message, StringComparison.Ordinal);
    }

    private static SingleFileCompileRequest Request(
        CompileFixture fixture,
        string source,
        ExtensionLock @lock
    ) =>
        new()
        {
            ExtensionId = "acme-tools",
            ExtensionVersion = "1.0.0",
            SourcePath = Path.Combine(fixture.Extensions, "acme.cs"),
            SourceText = source,
            AssemblyName = "acme-tools",
            Target = fixture.Target,
            GlobalPackagesFolder = fixture.PackagesFolder,
            Lock = @lock,
        };

    /// <summary>The type names the emitted image declares, which is what a load would find.</summary>
    private static IReadOnlyList<string> TypeNames(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        return
        [
            .. metadata.TypeDefinitions.Select(handle =>
                metadata.GetString(metadata.GetTypeDefinition(handle).Namespace)
            ),
        ];
    }
}
