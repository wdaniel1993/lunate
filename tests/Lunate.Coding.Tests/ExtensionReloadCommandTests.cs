using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// <c>lunate extensions reload &lt;id|path&gt;</c> — the headless half of the reload: the same
/// preflight a session runs (trust for the bytes on disk, then the build bound to them), plus a load
/// into a register-only host, which is the only way to know the factory runs before a session tries.
/// It never approves trust, and a load that fails is a failure, not a "reloaded".
/// </summary>
public sealed class ExtensionReloadCommandTests
{
    private const string Source = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue
        #:package Refit@8.0.0

        public sealed class AcmeExtension : Lunate.Extensibility.Abstractions.IExtensionFactory
        {
            public Lunate.Extensibility.Abstractions.IExtension Create(
                Lunate.Extensibility.Abstractions.IExtensionContext context) => new Shell();
        }

        internal sealed class Shell : Lunate.Extensibility.Abstractions.IExtension { }
        """;

    [Fact]
    public void An_unknown_target_is_a_usage_error_naming_what_it_looked_for()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        WriteFile(store);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exit = Cli.Run(
            ["extensions", "reload", "nope"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams { StorePath = store }
        );

        Assert.Equal(ExtensionReloadCommand.ExitUsage, exit);
        Assert.Contains(
            "no single-file extension matches 'nope'",
            errors.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void An_untrusted_file_reports_the_trust_step_and_is_never_approved()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        WriteFile(store);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exit = Cli.Run(
            ["extensions", "reload", "acme-tools"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams
            {
                StorePath = store,
                RestoreEngine = () =>
                    throw new InvalidOperationException(
                        "an untrusted file must not reach a restore engine"
                    ),
                Compiler = () =>
                    throw new InvalidOperationException(
                        "an untrusted file must not reach a compiler"
                    ),
            }
        );

        Assert.Equal(ExtensionReloadCommand.ExitFailure, exit);
        Assert.Contains("failed (trust)", errors.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            "run an interactive session to approve it",
            errors.ToString(),
            StringComparison.Ordinal
        );
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void A_build_that_does_not_load_is_a_failure_not_a_reload()
    {
        using var temp = new TempDirectory();
        string store = Path.Combine(temp.Root, "store");
        string file = WriteFile(store);
        RecordTrust(store, file);
        using var output = new StringWriter();
        using var errors = new StringWriter();

        int exit = Cli.Run(
            ["extensions", "reload", "acme-tools", "--json"],
            output,
            errors,
            TestContext.Current.CancellationToken,
            extensions: new ExtensionSeams
            {
                StorePath = store,
                RestoreEngine = () => new OnePackageEngine(),
                // The bytes are not an assembly: the build succeeds and the load is what refuses.
                Compiler = () => new BytesCompiler(),
            }
        );

        Assert.Equal(ExtensionReloadCommand.ExitFailure, exit);
        Assert.Empty(output.ToString());
        Assert.Contains(
            "the new build could not be loaded",
            errors.ToString(),
            StringComparison.Ordinal
        );
    }

    /// <summary>The first-trust record an approving session writes; the CLI never writes one.</summary>
    private static void RecordTrust(string store, string file)
    {
        ExtensionHeader header = ExtensionHeaderScanner.Scan(File.ReadAllText(file));
        new ExtensionTrustStore(
            new ExtensionHostOptions { StorePath = store }
        ).RecordSingleFileTrust(
            file,
            SingleFileExtensionDigest.ComputeUnrestored(
                header.Id!,
                header.Packages,
                File.ReadAllBytes(file)
            )
        );
    }

    /// <summary>The file where a user extension lives: <c>&lt;store&gt;/extensions/acme.cs</c>.</summary>
    private static string WriteFile(string store, string? source = null)
    {
        string directory = Path.Combine(store, "extensions");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "acme.cs");
        File.WriteAllText(path, source ?? Source);
        return path;
    }

    /// <summary>One package, no assets: the lock binds, the compile is the seam's business.</summary>
    private sealed class OnePackageEngine : ISingleFileRestoreEngine
    {
        public Task<SingleFileRestoreResult> RestoreAsync(
            SingleFileRestoreRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                new SingleFileRestoreResult
                {
                    Packages =
                    [
                        new ExtensionLockPackage
                        {
                            Id = "Refit",
                            Version = "8.0.0",
                            Sha512 = "ZmFrZQ==",
                            Requested = "Refit@8.0.0",
                            Direct = true,
                            Files = ["lib/net10.0/Refit.dll"],
                            Compile = [],
                            Runtime = [],
                        },
                    ],
                    ReferencePack = new ExtensionLockReferencePack
                    {
                        Id = "Microsoft.NETCore.App.Ref",
                        Version = "10.0.13",
                        Rule = ReferencePackRules.Exact,
                        Assets = ["ref/net10.0/System.Runtime.dll"],
                    },
                    Sources = ["local"],
                    GlobalPackagesFolder = Path.Combine(Path.GetTempPath(), "unused-packages"),
                }
            );
        }
    }

    /// <summary>Compiles to bytes that are not a PE image: the load is what fails.</summary>
    private sealed class BytesCompiler : ISingleFileCompiler
    {
        public SingleFileCompileResult Compile(
            SingleFileCompileRequest request,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SingleFileCompileResult.Ok([0x4d, 0x5a]);
        }
    }
}
