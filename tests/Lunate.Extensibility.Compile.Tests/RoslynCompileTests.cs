using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Lunate.Extensibility;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// Task 2.7: the compile itself, against the real reference pack, the host's contract assemblies
/// and a locked package's compile assets. The red cases are the two the requirement names — a
/// failed compile reports the first diagnostic, and it leaves nothing behind — plus the two the
/// cache depends on: the same inputs emit the same bytes, and a package's copy of a contract never
/// displaces the host's.
/// </summary>
public sealed class RoslynCompileTests
{
    private const string Factory = """
        using Acme.Tools;
        using Lunate.Extensibility.Abstractions;

        public sealed class AcmeExtension : IExtensionFactory
        {
            public IExtension Create(IExtensionContext context) => new Shell(context);
        }

        internal sealed class Shell(IExtensionContext context) : IExtension
        {
            private readonly string _greeting = Tools.Hello();
        }
        """;

    private const string Package = """
        namespace Acme.Tools;

        public static class Tools
        {
            public static string Hello() => "hello";
        }
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Compile_references_use_only_the_extension_contract_and_shared_meai()
    {
        Assert.Equal(
            new[]
            {
                "Lunate.Contracts",
                "Lunate.Extensibility.Abstractions",
                "Microsoft.Extensions.AI.Abstractions",
            },
            HostContractReferences.ContractNames
        );
    }

    [Fact]
    public void An_extension_compiles_against_the_host_contract_and_a_locked_package()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage package = fixture.AddPackage("Acme.Tools", "2.0.0", Package, Ct);

        SingleFileCompileResult result = fixture.Compile(
            Request(fixture, Factory, fixture.Lock(package)),
            Ct
        );

        byte[] assembly = CompileFixture.Assembly(result);
        Assert.Contains("Lunate.Extensibility.Abstractions", References(assembly));
        Assert.Contains("Acme.Tools", References(assembly));
        Assert.Equal("acme-tools", AssemblyName(assembly));
    }

    [Fact]
    public void The_first_error_is_reported_with_its_file_line_column_and_id()
    {
        using CompileFixture fixture = CompileFixture.Create();
        // One string per line, so the expected position is the line and column a user reads.
        string source = string.Join(
            "\n",
            "using Lunate.Extensibility.Abstractions;",
            "",
            "public sealed class Broken : IExtensionFactory",
            "{",
            "    public IExtension Create(IExtensionContext context)",
            "    {",
            "        int first = Missing.One;",
            "        return null;",
            "    }",
            "}"
        );

        SingleFileCompileResult result = fixture.Compile(
            Request(fixture, source, fixture.Lock()),
            Ct
        );

        Assert.False(result.Succeeded);
        SingleFileDiagnostic error = Assert.IsType<SingleFileDiagnostic>(result.FirstError);
        Assert.Equal("CS0103", error.Id);
        Assert.Equal(7, error.Line);
        Assert.Equal(21, error.Column);
        Assert.Equal(Path.Combine(fixture.Extensions, "acme.cs"), error.File);
        // The rendered form is Roslyn's own, so the row reads like a compiler message.
        Assert.StartsWith(
            FormattableString.Invariant(
                $"{error.File}(7,21): error CS0103: The name 'Missing' does not exist"
            ),
            result.Reason,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_warning_is_not_a_failure()
    {
        using CompileFixture fixture = CompileFixture.Create();
        string source = """
            using Lunate.Extensibility.Abstractions;

            public sealed class Noisy : IExtensionFactory
            {
                public IExtension Create(IExtensionContext context)
                {
                    int unused = 1;
                    return null;
                }
            }
            """;

        SingleFileCompileResult result = fixture.Compile(
            Request(fixture, source, fixture.Lock()),
            Ct
        );

        Assert.True(result.Succeeded, result.Reason);
    }

    [Fact]
    public void A_missing_reference_is_reported_rather_than_compiled_around()
    {
        using CompileFixture fixture = CompileFixture.Create();
        var absent = new ExtensionLockPackage
        {
            Id = "Acme.Tools",
            Version = "9.9.9",
            Sha512 = "ZmFrZQ==",
            Requested = "Acme.Tools@9.9.9",
            Direct = true,
            Compile = ["lib/net10.0/Acme.Tools.dll"],
            Runtime = ["lib/net10.0/Acme.Tools.dll"],
        };

        SingleFileCompileResult result = fixture.Compile(
            Request(fixture, Factory, fixture.Lock(absent)),
            Ct
        );

        Assert.False(result.Succeeded);
        Assert.Null(result.FirstError);
        Assert.Contains("lib/net10.0/Acme.Tools.dll", result.Reason, StringComparison.Ordinal);
        Assert.Contains("restore the extension again", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_packages_copy_of_a_contract_is_ignored()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage tools = fixture.AddPackage("Acme.Tools", "2.0.0", Package, Ct);

        // A package that ships its own Lunate.Extensibility.Abstractions.dll — same assembly
        // identity, different bytes. Two references with one identity would be CS1703, so this only
        // compiles when the host's copy wins and the package's is dropped.
        byte[] impostor = CompileFixture.Assembly(
            fixture.Compile(
                Request(
                    fixture,
                    """
                    namespace Lunate.Extensibility.Abstractions;

                    public interface IImpostor { }
                    """,
                    fixture.Lock()
                ) with
                {
                    AssemblyName = "Lunate.Extensibility.Abstractions",
                },
                Ct
            )
        );
        string copier = "Acme.Copier";
        string version = "1.0.0";
        string asset = FormattableString.Invariant(
            $"lib/{fixture.Target.Tfm}/Lunate.Extensibility.Abstractions.dll"
        );
        string directory = Path.Combine(
            fixture.PackagesFolder,
            copier.ToLowerInvariant(),
            version,
            "lib",
            fixture.Target.Tfm
        );
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(
            Path.Combine(directory, "Lunate.Extensibility.Abstractions.dll"),
            impostor
        );

        SingleFileCompileResult result = fixture.Compile(
            Request(
                fixture,
                Factory,
                fixture.Lock(
                    tools,
                    new ExtensionLockPackage
                    {
                        Id = copier,
                        Version = version,
                        Sha512 = "ZmFrZQ==",
                        Requested = FormattableString.Invariant($"{copier}@{version}"),
                        Direct = true,
                        Files = [asset],
                        Compile = [asset],
                        Runtime = [asset],
                    }
                )
            ),
            Ct
        );

        byte[] assembly = CompileFixture.Assembly(result);
        Assert.Contains("Lunate.Extensibility.Abstractions", References(assembly));
    }

    [Fact]
    public void The_same_source_and_lock_compile_to_byte_identical_output()
    {
        using CompileFixture fixture = CompileFixture.Create();
        ExtensionLockPackage package = fixture.AddPackage("Acme.Tools", "2.0.0", Package, Ct);

        byte[] first = CompileFixture.Assembly(
            fixture.Compile(Request(fixture, Factory, fixture.Lock(package)), Ct)
        );
        byte[] second = CompileFixture.Assembly(
            fixture.Compile(Request(fixture, Factory, fixture.Lock(package)), Ct)
        );

        Assert.Equal(first, second);

        // The same bytes from another directory: a rebuild after a move is still the same artifact,
        // because nothing about the path reaches the image.
        string other = Path.Combine(Path.GetDirectoryName(fixture.Extensions)!, "elsewhere");
        Directory.CreateDirectory(other);
        string moved = Path.Combine(other, "acme.cs");
        File.WriteAllText(moved, Factory);
        byte[] third = CompileFixture.Assembly(
            fixture.Compile(
                Request(fixture, Factory, fixture.Lock(package)) with
                {
                    SourcePath = moved,
                },
                Ct
            )
        );

        Assert.Equal(first, third);
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

    /// <summary>The assembly references the image records, which is what the load will resolve.</summary>
    private static IReadOnlyList<string> References(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        return
        [
            .. metadata.AssemblyReferences.Select(handle =>
                metadata.GetString(metadata.GetAssemblyReference(handle).Name)
            ),
        ];
    }

    private static string AssemblyName(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        return metadata.GetString(metadata.GetAssemblyDefinition().Name);
    }
}
