using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class WriteVerifierContractTests
{
    [Fact]
    public void Manifest_parses_verifier_names_and_multiple_patterns()
    {
        ExtensionManifest manifest = Manifest(
            """[{"name":"csharp","patterns":["*.cs","*.csx"]}]"""
        );

        WriteVerifierDeclaration declaration = Assert.Single(manifest.WriteVerifiers);
        Assert.Equal("csharp", declaration.Name);
        Assert.Equal(["*.cs", "*.csx"], declaration.Patterns);
    }

    [Theory]
    [InlineData("""[{"name":"csharp","patterns":[""]}]""")]
    [InlineData("""[{"name":"csharp","patterns":["src/*.cs"]}]""")]
    [InlineData("""[{"name":"csharp","patterns":["*.cs","*.cs"]}]""")]
    [InlineData(
        """[{"name":"csharp","patterns":["*.cs"]},{"name":"csharp","patterns":["*.csx"]}]"""
    )]
    public void Manifest_rejects_invalid_or_duplicate_verifier_patterns(string declarations)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            Manifest(declarations)
        );

        Assert.Contains("writeVerifiers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_registered_undeclared_verifier_refuses_activation()
    {
        using var temp = new TempDirectory();
        var verifier = new TestVerifier();
        ExtensionManifest manifest = Manifest("[]", "echo");
        var loader = Loader(temp, manifest, new RegisteringVerifierFactory("csharp", verifier));
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(
            async () =>
                await loader.Load(
                    "echo",
                    temp.Root,
                    "repo",
                    new FakeExtensionTrustPrompt(approve: false),
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Contains("write verifier 'csharp'", exception.Message, StringComparison.Ordinal);
        Assert.False(loader.IsLoaded("echo"));
        Assert.Null(loader.WriteVerifiers.Find("nested/file.cs"));
    }

    [Fact]
    public async Task A_declared_unregistered_verifier_is_logged_and_omitted()
    {
        using var temp = new TempDirectory();
        var log = new RecordingExtensionLog();
        ExtensionManifest manifest = Manifest(
            """[{"name":"csharp","patterns":["*.cs"]}]""",
            "echo"
        );
        var loader = Loader(temp, manifest, new RegisteringVerifierFactory(), log);
        loader.Discover(temp.Root, "repo");

        await loader.Load(
            "echo",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: false),
            TestContext.Current.CancellationToken
        );

        Assert.True(log.Contains("manifest declares write verifier 'csharp'"));
        Assert.Null(loader.WriteVerifiers.Find("nested/file.cs"));
    }

    [Fact]
    public async Task Verifier_patterns_match_nested_basenames_case_insensitively()
    {
        using var temp = new TempDirectory();
        var verifier = new TestVerifier();
        ExtensionManifest manifest = Manifest(
            """[{"name":"csharp","patterns":["*.cs"]}]""",
            "echo"
        );
        var loader = Loader(temp, manifest, new RegisteringVerifierFactory("csharp", verifier));
        loader.Discover(temp.Root, "repo");

        await loader.Load(
            "echo",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: false),
            TestContext.Current.CancellationToken
        );

        Assert.Same(verifier, loader.WriteVerifiers.Find("nested/PROGRAM.CS"));
        Assert.Null(loader.WriteVerifiers.Find("nested/PROGRAM.CSX"));
    }

    private static ExtensionManifest Manifest(string declarations, string id = "echo") =>
        ExtensionManifest.Parse(
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.0.0","builtin":true,"writeVerifiers":{{declarations}}}""",
            $"host/{id}/extension.json"
        );

    private static ExtensionLoader Loader(
        TempDirectory temp,
        ExtensionManifest manifest,
        IExtensionFactory factory,
        RecordingExtensionLog? log = null
    ) =>
        new(
            new ExtensionHostOptions
            {
                StorePath = temp.Subdirectory("store"),
                Log = log,
                BuiltIns =
                [
                    new BuiltInExtension(
                        $"host/{manifest.Id}/extension.json",
                        manifest,
                        () => factory
                    ),
                ],
            }
        );

    private sealed class RegisteringVerifierFactory(
        string? name = null,
        IWriteVerifier? verifier = null
    ) : IExtensionFactory
    {
        public IExtension Create(IExtensionContext context)
        {
            if (name is not null && verifier is not null)
            {
                context.RegisterWriteVerifier(new WriteVerifierRegistration(name, verifier));
            }

            return new EmptyExtension();
        }
    }

    private sealed class TestVerifier : IWriteVerifier
    {
        public ValueTask<WriteVerificationResult> VerifyAsync(
            WriteVerificationRequest request,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(new WriteVerificationResult(WriteVerificationStatus.Verified));
    }

    private sealed class EmptyExtension : IExtension { }
}
