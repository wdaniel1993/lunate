using System.Diagnostics.CodeAnalysis;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ModelRegistryRegistrationTests
{
    [Fact]
    public void Declared_providers_are_listed_namespaced_with_their_capabilities()
    {
        var registry = new ModelRegistry();

        registry.RegisterDeclared(
            "acme",
            [
                new ModelProviderDescriptor(
                    "acme-local",
                    "Acme Local",
                    "http://localhost:11434/v1",
                    "acme-key",
                    ["acme-7b", "acme-13b"],
                    32768,
                    SupportsTools: false
                ),
            ],
            new TestSecrets()
        );

        RegisteredModelProvider provider = Assert.Single(registry.List());
        Assert.Equal("acme-local", provider.Id);
        Assert.Equal("ext/acme/acme-local", provider.NamespacedId);
        Assert.Equal("acme", provider.ExtensionId);
        Assert.Equal("Acme Local", provider.DisplayName);
        Assert.Equal("http://localhost:11434/v1", provider.Endpoint);
        Assert.Equal("acme-key", provider.SecretName);
        Assert.Equal(["acme-7b", "acme-13b"], provider.ModelIds);
        Assert.Equal(32768, provider.ContextWindow);
        Assert.False(provider.SupportsTools);
    }

    [Fact]
    public void Secret_values_never_appear_in_the_listing()
    {
        var registry = new ModelRegistry();
        var secrets = new TestSecrets { ["acme-key"] = "super-secret-value" };

        registry.RegisterDeclared("acme", [Provider("acme-local")], secrets);

        string listing = string.Join(" ", registry.List().Select(provider => provider.ToString()));
        Assert.Contains("SecretName = key", listing, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", listing, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_provider_ids_across_extensions_are_refused_naming_both()
    {
        var registry = new ModelRegistry();
        registry.RegisterDeclared("a", [Provider("acme-local")], new TestSecrets());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterDeclared("b", [Provider("acme-local")], new TestSecrets())
        );

        Assert.Contains("'acme-local'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bad_batch_adds_nothing()
    {
        var registry = new ModelRegistry();
        registry.RegisterDeclared("a", [Provider("first")], new TestSecrets());

        Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterDeclared(
                "b",
                [Provider("second"), Provider("first")],
                new TestSecrets()
            )
        );

        RegisteredModelProvider provider = Assert.Single(registry.List());
        Assert.Equal("ext/a/first", provider.NamespacedId);
    }

    [Fact]
    public void Malformed_provider_ids_are_refused_naming_the_extension()
    {
        var registry = new ModelRegistry();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterDeclared("a", [Provider("Acme")], new TestSecrets())
        );

        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'Acme'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_served_model_ids_across_extensions_are_refused_naming_both()
    {
        var registry = new ModelRegistry();
        registry.RegisterDeclared("a", [Provider("first", "fast")], new TestSecrets());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterDeclared("b", [Provider("second", "fast")], new TestSecrets())
        );

        Assert.Contains("'fast'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_served_model_id_cannot_shadow_a_catalog_model()
    {
        var registry = new ModelRegistry(Catalog("fast"));
        registry.RegisterDeclared("a", [Provider("first", "other")], new TestSecrets());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterDeclared("b", [Provider("second", "fast")], new TestSecrets())
        );

        Assert.Contains("'fast'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_virtual_model_id_cannot_shadow_a_catalog_model()
    {
        var registry = new ModelRegistry(Catalog("ext/acme/auto"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterVirtualModel("acme", Virtual("auto", "ext/acme/deep"))
        );

        Assert.Contains("'ext/acme/auto'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'acme'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_programmatic_provider_cannot_reuse_a_declared_provider_id()
    {
        var registry = new ModelRegistry();
        registry.RegisterDeclared("a", [Provider("acme-local")], new TestSecrets());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterProvider(
                "b",
                new ExtensionModelProvider("acme-local", ["m"], () => null!)
            )
        );

        Assert.Contains("'acme-local'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_virtual_model_ids_are_refused_naming_both()
    {
        var registry = new ModelRegistry();
        registry.RegisterVirtualModel("a", Virtual("auto", "deep"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterVirtualModel("a", Virtual("auto", "deep"))
        );

        Assert.Contains("'ext/a/auto'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_caps_apply_per_extension()
    {
        var registry = new ModelRegistry();
        for (var index = 0; index < 8; index++)
        {
            registry.RegisterProvider(
                "a",
                new ExtensionModelProvider($"p{index}", [$"m{index}"], () => null!)
            );
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterProvider("a", new ExtensionModelProvider("p8", ["m8"], () => null!))
        );

        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("8", exception.Message, StringComparison.Ordinal);

        registry.RegisterProvider("b", new ExtensionModelProvider("q0", ["q0"], () => null!));
    }

    [Fact]
    public void Virtual_model_caps_apply_per_extension()
    {
        var registry = new ModelRegistry();
        for (var index = 0; index < 8; index++)
        {
            registry.RegisterVirtualModel("a", Virtual($"auto{index}", "deep"));
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterVirtualModel("a", Virtual("auto8", "deep"))
        );

        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("8", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Middleware_caps_apply_per_extension()
    {
        var registry = new ModelRegistry();
        for (var index = 0; index < 16; index++)
        {
            registry.RegisterModelMiddleware(
                "a",
                new ExtensionModelMiddleware(
                    ModelMiddlewareSlot.BeforeRouting,
                    index,
                    inner => inner
                )
            );
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterModelMiddleware(
                "a",
                new ExtensionModelMiddleware(ModelMiddlewareSlot.BeforeRouting, 0, inner => inner)
            )
        );

        Assert.Contains("'a'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("16", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_virtual_model_without_backends_or_with_a_bad_window_is_refused()
    {
        var registry = new ModelRegistry();

        Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterVirtualModel("a", Virtual("auto", []))
        );
        Assert.Throws<InvalidOperationException>(() =>
            registry.RegisterVirtualModel("a", Virtual("auto", "deep") with { ContextWindow = 0 })
        );
    }

    [Fact]
    public void Unregister_removes_only_that_extensions_registrations()
    {
        var registry = new ModelRegistry();
        registry.RegisterDeclared("a", [Provider("first", "m-a")], new TestSecrets());
        registry.RegisterDeclared("b", [Provider("second", "m-b")], new TestSecrets());
        registry.RegisterVirtualModel("a", Virtual("auto", "m-a"));

        registry.Unregister("a");

        RegisteredModelProvider provider = Assert.Single(registry.List());
        Assert.Equal("ext/b/second", provider.NamespacedId);
        Assert.Null(registry.FindCapabilities("m-a"));
        Assert.Null(registry.FindCapabilities("ext/a/auto"));
    }

    internal static ModelProviderDescriptor Provider(string id, string modelId = "model") =>
        new(id, id, "http://localhost:11434/v1", "key", [modelId], 4096);

    internal static VirtualModelRegistration Virtual(string id, params string[] backends) =>
        new(id, backends, _ => backends[0]);

    internal static ModelCatalog Catalog(params string[] ids)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        string models = string.Join(
            ",",
            ids.Select(id =>
                $$"""{"id":"{{id}}","provider":"openai","contextWindow":128000,"supportsTools":true}"""
            )
        );
        File.WriteAllText(path, $$"""{"schemaVersion":1,"models":[{{models}}]}""");
        try
        {
            return ModelCatalog.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

internal sealed class TestSecrets : IExtensionSecrets
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public string this[string name]
    {
        set => _values[name] = value;
    }

    public bool TryGet(string name, [NotNullWhen(true)] out string? value) =>
        _values.TryGetValue(name, out value);
}
