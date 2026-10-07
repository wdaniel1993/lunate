using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionSettingsTests
{
    [Fact]
    public void Missing_settings_file_yields_empty_settings()
    {
        using var temp = new TempDirectory();
        var options = TestExtensions.Options(temp.Subdirectory("store"));

        ExtensionSettingsStore settings = ExtensionSettingsStore.Load(options, Descriptor());

        Assert.False(settings.TryGet("anything", out _));
    }

    [Fact]
    public void TryGet_navigates_dot_separated_paths()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        Directory.CreateDirectory(Path.Combine(store, "extensions-settings"));
        File.WriteAllText(
            Path.Combine(store, "extensions-settings", "hello.json"),
            """{"a":{"b":"c"},"count":3,"flag":true}"""
        );
        var options = TestExtensions.Options(store);

        ExtensionSettingsStore settings = ExtensionSettingsStore.Load(options, Descriptor());

        Assert.True(settings.TryGet("a.b", out JsonElement value));
        Assert.Equal("c", value.GetString());
        Assert.True(settings.TryGet("count", out value));
        Assert.Equal(3, value.GetInt32());
        Assert.True(settings.TryGet("a", out value));
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        Assert.False(settings.TryGet("missing", out _));
        Assert.False(settings.TryGet("a.missing", out _));
        Assert.False(settings.TryGet("", out _));
    }

    [Theory]
    [InlineData("string", "\"x\"", true)]
    [InlineData("string", "1", false)]
    [InlineData("number", "1.5", true)]
    [InlineData("number", "\"x\"", false)]
    [InlineData("integer", "3", true)]
    [InlineData("integer", "1.5", false)]
    [InlineData("boolean", "true", true)]
    [InlineData("boolean", "false", true)]
    [InlineData("object", """{"k":1}""", true)]
    [InlineData("object", "[]", false)]
    [InlineData("array", "[1]", true)]
    [InlineData("array", "{}", false)]
    public void Schema_types_are_checked_one_level_deep(string type, string json, bool valid)
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        Directory.CreateDirectory(Path.Combine(store, "extensions-settings"));
        File.WriteAllText(
            Path.Combine(store, "extensions-settings", "hello.json"),
            "{\"value\":" + json + "}"
        );
        var options = TestExtensions.Options(store);
        ExtensionDescriptor descriptor = Descriptor(
            "{\"properties\":{\"value\":{\"type\":\"" + type + "\"}}}"
        );

        if (valid)
        {
            ExtensionSettingsStore settings = ExtensionSettingsStore.Load(options, descriptor);
            Assert.True(settings.TryGet("value", out _));
        }
        else
        {
            ExtensionLoadException exception = Assert.Throws<ExtensionLoadException>(() =>
                ExtensionSettingsStore.Load(options, descriptor)
            );
            Assert.Contains("hello", exception.Message);
            Assert.Contains("value", exception.Message);
        }
    }

    [Fact]
    public void Unknown_schema_types_and_missing_settings_are_not_failures()
    {
        using var temp = new TempDirectory();
        var options = TestExtensions.Options(temp.Subdirectory("store"));
        ExtensionDescriptor descriptor = Descriptor("""{"properties":{"value":{"type":"null"}}}""");

        ExtensionSettingsStore settings = ExtensionSettingsStore.Load(options, descriptor);

        Assert.False(settings.TryGet("value", out _));
    }

    [Fact]
    public async Task Settings_violating_the_schema_fail_the_load_naming_extension_and_field()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string directory = Path.Combine(store, "extensions", "hello");
        TestExtensions.CopyFixture(directory, "HelloExtension.dll", "HelloExtension.Support.dll");
        TestExtensions.WriteManifest(
            directory,
            "hello",
            settingsSchema: """{"required":["token"],"properties":{"token":{"type":"string"}}}"""
        );
        Directory.CreateDirectory(Path.Combine(store, "extensions-settings"));
        File.WriteAllText(
            Path.Combine(store, "extensions-settings", "hello.json"),
            """{"other":1}"""
        );
        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(() =>
            loader.Load(
                "hello",
                temp.Root,
                "repo",
                new FakeExtensionTrustPrompt(approve: true),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("hello", exception.Message);
        Assert.Contains("token", exception.Message);
    }

    [Fact]
    public async Task Valid_settings_load_and_are_exposed()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        string directory = Path.Combine(store, "extensions", "hello");
        TestExtensions.CopyFixture(directory, "HelloExtension.dll", "HelloExtension.Support.dll");
        TestExtensions.WriteManifest(
            directory,
            "hello",
            settingsSchema: """{"required":["token"],"properties":{"token":{"type":"string"}}}"""
        );
        Directory.CreateDirectory(Path.Combine(store, "extensions-settings"));
        File.WriteAllText(
            Path.Combine(store, "extensions-settings", "hello.json"),
            """{"token":"abc","extra":true}"""
        );
        var loader = new ExtensionLoader(TestExtensions.Options(store));
        loader.Discover(temp.Root, "repo");

        LoadedExtension loaded = await loader.Load(
            "hello",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: true),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("hello", loaded.Id);
    }

    [Fact]
    public void A_settings_file_that_is_not_an_object_fails_naming_the_extension()
    {
        using var temp = new TempDirectory();
        string store = temp.Subdirectory("store");
        Directory.CreateDirectory(Path.Combine(store, "extensions-settings"));
        File.WriteAllText(Path.Combine(store, "extensions-settings", "hello.json"), "[1]");
        var options = TestExtensions.Options(store);

        ExtensionLoadException exception = Assert.Throws<ExtensionLoadException>(() =>
            ExtensionSettingsStore.Load(options, Descriptor())
        );

        Assert.Contains("hello", exception.Message);
    }

    private static ExtensionDescriptor Descriptor(string? settingsSchema = null)
    {
        using JsonDocument schema = JsonDocument.Parse(settingsSchema ?? "{}");
        ExtensionManifest manifest = new()
        {
            Id = "hello",
            Version = "0.1.0",
            ApiVersion = "^1.0.0",
            EntryAssembly = "HelloExtension.dll",
            SettingsSchema = schema.RootElement.Clone(),
        };
        return new ExtensionDescriptor("hello", "0.1.0", ExtensionScope.Global, ".", manifest);
    }
}
