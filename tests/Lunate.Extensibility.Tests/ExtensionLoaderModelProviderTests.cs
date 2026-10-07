namespace Lunate.Extensibility.Tests;

public sealed class ExtensionLoaderModelProviderTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private const string AcmeProvider = """
        [{"id":"acme-local","displayName":"Acme Local","endpoint":"http://localhost:11434/v1",
          "secretName":"acme-key","modelIds":["acme-7b"]}]
        """;

    [Fact]
    public async Task Manifest_providers_are_listed_after_load_and_removed_on_unload()
    {
        using var temp = new TempDirectory();
        InstallWithProviders(temp, "hello", AcmeProvider);
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");

        await loader.Load("hello", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        RegisteredModelProvider provider = Assert.Single(loader.ModelProviders.List());
        Assert.Equal("ext/hello/acme-local", provider.NamespacedId);
        Assert.Equal("acme-key", provider.SecretName);

        await loader.Unload("hello");

        Assert.Empty(loader.ModelProviders.List());
    }

    [Fact]
    public async Task Duplicate_provider_ids_across_extensions_refuse_the_second_load_naming_both()
    {
        using var temp = new TempDirectory();
        InstallWithProviders(temp, "first", AcmeProvider);
        InstallWithProviders(temp, "second", AcmeProvider);
        var loader = new ExtensionLoader(TestExtensions.Options(temp.Subdirectory("store")));
        loader.Discover(temp.Root, "repo");
        await loader.Load("first", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct);

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load("second", temp.Root, "repo", new FakeExtensionTrustPrompt(true), Ct)
        );

        Assert.Contains("'first'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'second'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'acme-local'", exception.Message, StringComparison.Ordinal);
        Assert.Single(loader.ModelProviders.List());
    }

    private static void InstallWithProviders(TempDirectory temp, string id, string providers)
    {
        string directory = TestExtensions.GlobalExtensionDirectory(temp, id);
        TestExtensions.CopyFixture(directory, "HelloExtension.dll", "HelloExtension.Support.dll");
        TestExtensions.WriteManifest(directory, id, modelProviders: providers);
    }
}
