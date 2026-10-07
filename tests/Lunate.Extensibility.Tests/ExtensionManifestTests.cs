using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class ExtensionManifestTests
{
    private const string Minimal =
        """{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"HelloExtension.dll"}""";

    [Fact]
    public void Parses_the_required_fields_and_defaults_the_optional_arrays()
    {
        ExtensionManifest manifest = ExtensionManifest.Parse(Minimal, "extension.json");

        Assert.Equal("hello", manifest.Id);
        Assert.Equal("0.1.0", manifest.Version);
        Assert.Equal("^1.0.0", manifest.ApiVersion);
        Assert.Equal("HelloExtension.dll", manifest.EntryAssembly);
        Assert.Empty(manifest.Tools);
        Assert.Empty(manifest.Commands);
        Assert.Empty(manifest.Hooks);
        Assert.Empty(manifest.Services);
        Assert.Empty(manifest.Capabilities);
        Assert.Null(manifest.SettingsSchema);
    }

    [Fact]
    public void Parses_declarations_and_the_settings_schema()
    {
        const string json = """
            {"id":"hello","version":"0.1.0","apiVersion":"1.0.0","entryAssembly":"HelloExtension.dll",
             "tools":["read"],"commands":["greet"],"hooks":["turn-ended"],"services":["store"],
             "capabilities":["files"],"settingsSchema":{"required":["token"],"properties":{"token":{"type":"string"}}}}
            """;

        ExtensionManifest manifest = ExtensionManifest.Parse(json, "extension.json");

        Assert.Equal(["read"], manifest.Tools);
        Assert.Equal(["greet"], manifest.Commands);
        Assert.Equal(["turn-ended"], manifest.Hooks);
        Assert.Equal(["store"], manifest.Services);
        Assert.Equal(["files"], manifest.Capabilities);
        JsonElement schema = manifest.SettingsSchema!.Value;
        Assert.Equal(JsonValueKind.Object, schema.ValueKind);
        Assert.Equal("token", schema.GetProperty("required")[0].GetString());
    }

    [Fact]
    public void Unknown_top_level_fields_are_ignored()
    {
        const string json =
            """{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"HelloExtension.dll","future":{"x":1}}""";

        ExtensionManifest manifest = ExtensionManifest.Parse(json, "extension.json");

        Assert.Equal("hello", manifest.Id);
    }

    [Fact]
    public void Parses_an_incompatible_but_syntactically_valid_range()
    {
        const string json =
            """{"id":"hello","version":"0.1.0","apiVersion":"2.0.0","entryAssembly":"HelloExtension.dll"}""";

        ExtensionManifest manifest = ExtensionManifest.Parse(json, "extension.json");

        Assert.Equal("2.0.0", manifest.ApiVersion);
    }

    [Theory]
    [InlineData("""{"version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll"}""", "id")]
    [InlineData("""{"id":"hello","apiVersion":"^1.0.0","entryAssembly":"e.dll"}""", "version")]
    [InlineData("""{"id":"hello","version":"0.1.0","entryAssembly":"e.dll"}""", "apiVersion")]
    [InlineData("""{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0"}""", "entryAssembly")]
    public void Required_fields_fail_naming_the_file_and_field(string json, string field)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extensions/hello/extension.json")
        );

        Assert.Contains(
            "extensions/hello/extension.json",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains($"'{field}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """{"id":"Hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll"}"""
    )]
    [InlineData(
        """{"id":"he llo","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll"}"""
    )]
    [InlineData(
        """{"id":"he_llo","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll"}"""
    )]
    [InlineData("""{"id":"","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll"}""")]
    public void Malformed_ids_fail_naming_the_field(string json)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("'id'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData(">=1.0.0")]
    [InlineData("~1.0.0")]
    public void Unsupported_range_grammar_fails_naming_the_file_and_field(string range)
    {
        string json =
            $$"""{"id":"hello","version":"0.1.0","apiVersion":"{{range}}","entryAssembly":"e.dll"}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("extension.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'apiVersion'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "unsupported apiVersion range",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("tools")]
    [InlineData("commands")]
    [InlineData("hooks")]
    [InlineData("services")]
    public void Duplicate_declarations_are_rejected(string field)
    {
        string json =
            $$"""{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll","{{field}}":["x","x"]}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("extension.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"'{field}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'x'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        """{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll","tools":"read"}"""
    )]
    [InlineData(
        """{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll","tools":[1]}"""
    )]
    [InlineData(
        """{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll","capabilities":"x"}"""
    )]
    public void Declaration_arrays_must_contain_strings(string json)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("extension.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains("array of strings", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_schema_must_be_an_object()
    {
        const string json =
            """{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll","settingsSchema":[]}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("'settingsSchema'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../outside.dll")]
    [InlineData("sub/entry.dll")]
    [InlineData("sub\\entry.dll")]
    public void Entry_assembly_must_be_a_file_name(string entryAssembly)
    {
        string json =
            $$"""{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":{{JsonSerializer.Serialize(entryAssembly)}}}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("'entryAssembly'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("42")]
    public void Non_object_documents_fail_naming_the_file(string json)
    {
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("extension.json", exception.Message, StringComparison.Ordinal);
    }
}
