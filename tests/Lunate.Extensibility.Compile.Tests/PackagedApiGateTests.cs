using Lunate.Extensibility.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 3.2 on the packaged path, with a real DLL: a packaged assembly compiled against a newer
/// <c>Lunate.Extensibility.Abstractions</c> than this host implements is refused at load, naming the
/// version it needs and the version it got. The manifest it ships claims compatibility — that is the
/// point, since a manifest is a declaration and the assembly reference is what the compiler saw.
/// </summary>
public sealed class PackagedApiGateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_packaged_dll_built_against_a_newer_contract_is_refused_at_load()
    {
        using CompileFixture compile = CompileFixture.Create();
        using var temp = new TempDirectory();
        string above = SingleFileApiGateTests.VersionAboveCurrent();

        // The contract a newer Lunate would ship, at the version that Lunate implements.
        byte[] contract = Emit(
            "Lunate.Extensibility.Abstractions",
            $$"""
            [assembly: System.Reflection.AssemblyVersion("{{above}}.0")]

            namespace Lunate.Extensibility.Abstractions;

            public static class ExtensionApi
            {
                public const string Current = "{{above}}";
            }

            /// <summary>A type, so a probe that touches it keeps the assembly reference.</summary>
            public sealed class ContractMarker;
            """,
            References(compile)
        );
        byte[] probe = Emit(
            "Probe",
            """
            public static class Probe
            {
                // typeof, not the const: a const is inlined and leaves no assembly reference behind,
                // and the reference is exactly what the gate reads.
                public static System.Type Contract =>
                    typeof(Lunate.Extensibility.Abstractions.ContractMarker);
            }
            """,
            [.. References(compile), MetadataReference.CreateFromImage(contract)]
        );

        string store = temp.Path("store");
        Directory.CreateDirectory(store);
        string directory = Path.Combine(store, "extensions", "ahead");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "Probe.dll"), probe);
        File.WriteAllText(
            Path.Combine(directory, "extension.json"),
            """{"id":"ahead","version":"1.0.0","apiVersion":"^1.0.0","entryAssembly":"Probe.dll"}"""
        );
        var loader = new ExtensionLoader(new ExtensionHostOptions { StorePath = store });
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load("ahead", temp.Root, "repo", new ApprovingPrompt(), Ct)
        );

        Assert.Contains(
            FormattableString.Invariant($"requires Lunate ≥ {above}"),
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(ExtensionApi.Current, exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Lunate.Extensibility.Abstractions " + above,
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(loader.Tools.List());
    }

    /// <summary>The reference pack this machine compiles against, resolved the way a build resolves it.</summary>
    private static IReadOnlyList<MetadataReference> References(CompileFixture fixture) =>
        [
            .. fixture.ReferencePack.Assets.Select(asset =>
                MetadataReference.CreateFromFile(
                    SingleFilePackages.AssetPath(
                        fixture.PackagesFolder,
                        fixture.ReferencePack.Id,
                        fixture.ReferencePack.Version,
                        asset
                    )
                )
            ),
        ];

    private static byte[] Emit(
        string name,
        string source,
        IEnumerable<MetadataReference> references
    )
    {
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(
            result.Success,
            string.Join(
                "; ",
                result
                    .Diagnostics.Where(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error
                    )
                    .Select(diagnostic => diagnostic.ToString())
            )
        );
        return stream.ToArray();
    }
}
