using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ModelProviderRegistryTests
{
    [Fact]
    public void Declared_providers_are_listed_namespaced()
    {
        var registry = new ModelProviderRegistry();

        registry.Register(
            "acme",
            [
                new ModelProviderDescriptor(
                    "acme-local",
                    "Acme Local",
                    "http://localhost:11434/v1",
                    "acme-key",
                    ["acme-7b", "acme-13b"]
                ),
            ]
        );

        RegisteredModelProvider provider = Assert.Single(registry.List());
        Assert.Equal("acme-local", provider.Id);
        Assert.Equal("ext/acme/acme-local", provider.NamespacedId);
        Assert.Equal("acme", provider.ExtensionId);
        Assert.Equal("Acme Local", provider.DisplayName);
        Assert.Equal("http://localhost:11434/v1", provider.Endpoint);
        Assert.Equal("acme-key", provider.SecretName);
        Assert.Equal(["acme-7b", "acme-13b"], provider.ModelIds);
    }

    [Fact]
    public void Secret_values_never_appear_in_the_listing()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        Directory.CreateDirectory(Path.Combine(store, "extensions-secrets"));
        File.WriteAllText(
            Path.Combine(store, "extensions-secrets", "acme.json"),
            """{"acme-key":"super-secret-value"}"""
        );
        var registry = new ModelProviderRegistry();

        registry.Register(
            "acme",
            [
                new ModelProviderDescriptor(
                    "acme-local",
                    "Acme Local",
                    "http://localhost:11434/v1",
                    "acme-key",
                    ["acme-7b"]
                ),
            ]
        );

        string listing = string.Join(" ", registry.List().Select(provider => provider.ToString()));
        Assert.Contains("acme-key", listing, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", listing, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_provider_ids_across_extensions_are_refused_naming_both()
    {
        var registry = new ModelProviderRegistry();
        registry.Register("a", [Provider("acme-local")]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("b", [Provider("acme-local")])
        );

        Assert.Contains("'acme-local'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bad_batch_adds_nothing()
    {
        var registry = new ModelProviderRegistry();
        registry.Register("a", [Provider("first")]);

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register("b", [Provider("second"), Provider("first")])
        );

        RegisteredModelProvider provider = Assert.Single(registry.List());
        Assert.Equal("ext/a/first", provider.NamespacedId);
    }

    [Fact]
    public void Malformed_ids_are_refused_naming_the_extension()
    {
        var registry = new ModelProviderRegistry();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("a", [Provider("Acme")]
            )
        );

        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Acme'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unregister_removes_only_that_extensions_providers()
    {
        var registry = new ModelProviderRegistry();
        registry.Register("a", [Provider("first")]);
        registry.Register("b", [Provider("second")]);

        registry.Unregister("a");

        RegisteredModelProvider provider = Assert.Single(registry.List());
        Assert.Equal("ext/b/second", provider.NamespacedId);
    }

    private static ModelProviderDescriptor Provider(string id) =>
        new(id, id, "http://localhost:11434/v1", "key", ["model"]);
}
