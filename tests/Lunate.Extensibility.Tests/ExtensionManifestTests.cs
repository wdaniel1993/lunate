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
    public void Parses_model_provider_declarations()
    {
        const string json = """
            {"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"HelloExtension.dll",
             "modelProviders":[{"id":"acme-local","displayName":"Acme Local",
              "endpoint":"http://localhost:11434/v1","secretName":"acme-key",
              "modelIds":["acme-7b","acme-13b"],"contextWindow":32768,
              "supportsTools":false,"future":"ignored"}]}
            """;

        ExtensionManifest manifest = ExtensionManifest.Parse(json, "extension.json");

        ModelProviderDescriptor provider = Assert.Single(manifest.ModelProviders);
        Assert.Equal("acme-local", provider.Id);
        Assert.Equal("Acme Local", provider.DisplayName);
        Assert.Equal("http://localhost:11434/v1", provider.Endpoint);
        Assert.Equal("acme-key", provider.SecretName);
        Assert.Equal(["acme-7b", "acme-13b"], provider.ModelIds);
        Assert.Equal(32768, provider.ContextWindow);
        Assert.False(provider.SupportsTools);
    }

    [Fact]
    public void A_provider_without_supportsTools_defaults_to_true()
    {
        const string json = """
            {"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"HelloExtension.dll",
             "modelProviders":[{"id":"acme-local","displayName":"Acme Local",
              "endpoint":"http://localhost:11434/v1","secretName":"acme-key",
              "modelIds":["acme-7b"],"contextWindow":32768}]}
            """;

        ExtensionManifest manifest = ExtensionManifest.Parse(json, "extension.json");

        Assert.True(Assert.Single(manifest.ModelProviders).SupportsTools);
    }

    [Fact]
    public void Model_providers_default_to_empty()
    {
        ExtensionManifest manifest = ExtensionManifest.Parse(Minimal, "extension.json");

        Assert.Empty(manifest.ModelProviders);
    }

    [Theory]
    [InlineData("""[1]""", "modelProviders")]
    [InlineData(
        """[{"displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":8}]""",
        "modelProviders[0].id"
    )]
    [InlineData(
        """[{"id":"Acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":8}]""",
        "modelProviders[0].id"
    )]
    [InlineData(
        """[{"id":"acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":8}]""",
        "modelProviders[0].displayName"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","secretName":"k","modelIds":["m"],"contextWindow":8}]""",
        "modelProviders[0].endpoint"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"ftp://x/v1","secretName":"k","modelIds":["m"],"contextWindow":8}]""",
        "modelProviders[0].endpoint"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"/v1","secretName":"k","modelIds":["m"],"contextWindow":8}]""",
        "modelProviders[0].endpoint"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"","modelIds":["m"],"contextWindow":8}]""",
        "modelProviders[0].secretName"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":[],"contextWindow":8}]""",
        "modelProviders[0].modelIds"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":[1],"contextWindow":8}]""",
        "modelProviders[0].modelIds"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"]}]""",
        "modelProviders[0].contextWindow"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":0}]""",
        "modelProviders[0].contextWindow"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":-8}]""",
        "modelProviders[0].contextWindow"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":1.5}]""",
        "modelProviders[0].contextWindow"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":"8k"}]""",
        "modelProviders[0].contextWindow"
    )]
    [InlineData(
        """[{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":8,"supportsTools":"yes"}]""",
        "modelProviders[0].supportsTools"
    )]
    public void Malformed_model_providers_fail_naming_the_file_and_field(
        string providers,
        string field
    )
    {
        string json =
            $$"""{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll","modelProviders":{{providers}}}""";

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

    [Fact]
    public void Duplicate_model_provider_ids_are_rejected()
    {
        const string providers = """
            [{"id":"acme","displayName":"Acme","endpoint":"http://x/v1","secretName":"k","modelIds":["m"],"contextWindow":8},
             {"id":"acme","displayName":"Other","endpoint":"http://y/v1","secretName":"k2","modelIds":["m2"],"contextWindow":8}]
            """;
        string json =
            $$"""{"id":"hello","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll","modelProviders":{{providers}}}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("'modelProviders'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'acme'", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void A_colon_in_the_id_is_refused_because_command_ids_are_colon_delimited()
    {
        const string json =
            """{"id":"acme:tools","version":"0.1.0","apiVersion":"^1.0.0","entryAssembly":"e.dll"}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("'id'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("must not contain ':'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ext:<id>:<name>", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void A_remote_manifest_parses_the_runtime()
    {
        const string json = """
            {"id":"remote","version":"0.1.0","apiVersion":"^1.0.0",
             "runtime":{"command":"node","args":["index.js"],"workingDirectory":"bin",
              "env":{"HELLO":"world"},"envPassthrough":["GITHUB_TOKEN"],"callTimeout":12.5,
              "startup":"background","future":"ignored"}}
            """;

        ExtensionManifest manifest = ExtensionManifest.Parse(json, "extension.json");

        Assert.Null(manifest.EntryAssembly);
        ExtensionRuntime runtime = manifest.Runtime!;
        Assert.Equal("node", runtime.Command);
        Assert.Equal(["index.js"], runtime.Arguments);
        Assert.Equal("bin", runtime.WorkingDirectory);
        Assert.Equal("world", runtime.Environment!["HELLO"]);
        Assert.Equal(["GITHUB_TOKEN"], runtime.EnvironmentPassthrough);
        Assert.Equal(TimeSpan.FromSeconds(12.5), runtime.CallTimeout);
        Assert.True(runtime.StartsInBackground);
    }

    [Fact]
    public void A_remote_manifest_defaults_the_optional_runtime_fields()
    {
        const string json =
            """{"id":"remote","version":"0.1.0","apiVersion":"^1.0.0","runtime":{"command":"node"}}""";

        ExtensionManifest manifest = ExtensionManifest.Parse(json, "extension.json");

        ExtensionRuntime runtime = manifest.Runtime!;
        Assert.Empty(runtime.Arguments);
        Assert.Null(runtime.WorkingDirectory);
        Assert.Null(runtime.Environment);
        Assert.Null(runtime.EnvironmentPassthrough);
        Assert.Null(runtime.CallTimeout);
        Assert.Null(runtime.Startup);
        Assert.False(runtime.StartsInBackground);
    }

    [Fact]
    public void Both_runtime_kinds_are_refused_naming_the_file_and_conflict()
    {
        const string json = """
            {"id":"remote","version":"0.1.0","apiVersion":"^1.0.0",
             "entryAssembly":"e.dll","runtime":{"command":"node"}}
            """;

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extensions/remote/extension.json")
        );

        Assert.Contains(
            "extensions/remote/extension.json",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("exactly one runtime kind", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Neither_runtime_kind_is_refused_naming_the_file_and_conflict()
    {
        const string json = """{"id":"remote","version":"0.1.0","apiVersion":"^1.0.0"}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extensions/remote/extension.json")
        );

        Assert.Contains(
            "extensions/remote/extension.json",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "neither 'entryAssembly' nor 'runtime'",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("""{"args":["a"]}""", "runtime.command")]
    [InlineData("""{"command":""}""", "runtime.command")]
    [InlineData("""{"command":"node","args":"index.js"}""", "runtime.args")]
    [InlineData("""{"command":"node","args":[1]}""", "runtime.args")]
    [InlineData("""{"command":"node","workingDirectory":""}""", "runtime.workingDirectory")]
    [InlineData("""{"command":"node","env":[]}""", "runtime.env")]
    [InlineData("""{"command":"node","env":{"A":1}}""", "runtime.env")]
    [InlineData("""{"command":"node","envPassthrough":"PATH"}""", "runtime.envPassthrough")]
    [InlineData(
        """{"command":"node","envPassthrough":["PATH","PATH"]}""",
        "runtime.envPassthrough"
    )]
    [InlineData("""{"command":"node","callTimeout":0}""", "runtime.callTimeout")]
    [InlineData("""{"command":"node","callTimeout":-2}""", "runtime.callTimeout")]
    [InlineData("""{"command":"node","callTimeout":"5s"}""", "runtime.callTimeout")]
    [InlineData("""{"command":"node","startup":"eager"}""", "runtime.startup")]
    public void Malformed_runtime_fields_fail_naming_the_file_and_field(
        string runtime,
        string field
    )
    {
        string json =
            $$"""{"id":"remote","version":"0.1.0","apiVersion":"^1.0.0","runtime":{{runtime}}}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extensions/remote/extension.json")
        );

        Assert.Contains(
            "extensions/remote/extension.json",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains($"'{field}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_runtime_that_is_not_an_object_is_refused()
    {
        const string json =
            """{"id":"remote","version":"0.1.0","apiVersion":"^1.0.0","runtime":"node"}""";

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            ExtensionManifest.Parse(json, "extension.json")
        );

        Assert.Contains("'runtime'", exception.Message, StringComparison.Ordinal);
    }
}
