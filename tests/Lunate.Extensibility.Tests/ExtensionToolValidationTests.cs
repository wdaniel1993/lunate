using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using AgentToolExposure = Lunate.Agent.ToolExposure;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Registration validation: every refusal names the extension and the field, names follow the
/// prefixed grammar, duplicates in the model-facing namespace are refused naming both sources and
/// the 32-per-extension bound holds (design D3, D4).
/// </summary>
public sealed class ExtensionToolValidationTests
{
    [Fact]
    public void A_valid_registration_becomes_the_prefixed_model_facing_name()
    {
        var registry = new ExtensionToolRegistry();

        registry.Register("spawner", Valid("spawn"));

        ExtensionToolAdapter tool = Assert.Single(registry.List());
        Assert.Equal("spawner__spawn", tool.Name);
        Assert.Equal("spawner", tool.Namespace?.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad name")]
    [InlineData("bad:name")]
    [InlineData("bad.name")]
    [InlineData("bad/name")]
    public void Invalid_names_are_refused_naming_the_extension_and_the_field(string name)
    {
        var registry = new ExtensionToolRegistry();
        ExtensionToolRegistration registration = Valid(name);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("spawner", registration)
        );

        Assert.Contains("extension 'spawner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'name'", exception.Message, StringComparison.Ordinal);
        Assert.Empty(registry.List());
    }

    [Fact]
    public void A_model_facing_name_longer_than_64_characters_is_refused_naming_the_field()
    {
        var registry = new ExtensionToolRegistry();
        string name = new('a', 60);
        string extensionId = new('e', 20);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register(extensionId, Valid(name))
        );

        Assert.Contains($"extension '{extensionId}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'name'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("64", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_64_character_model_facing_name_is_accepted()
    {
        var registry = new ExtensionToolRegistry();
        string name = new('a', 64 - "tool".Length - 2);

        registry.Register("tool", Valid(name));

        ExtensionToolAdapter tool = Assert.Single(registry.List());
        Assert.Equal(64, tool.Name.Length);
    }

    [Fact]
    public void A_blank_description_is_refused_naming_the_field()
    {
        var registry = new ExtensionToolRegistry();
        var registration = new ExtensionToolRegistration
        {
            Name = "spawn",
            Description = " ",
            ParametersSchema = Schema(),
            Handler = Handler(),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("spawner", registration)
        );

        Assert.Contains("extension 'spawner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'description'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(""" "text" """)]
    [InlineData(""" [1,2] """)]
    [InlineData(""" null """)]
    public void A_non_object_parameters_schema_is_refused_naming_the_field(string schema)
    {
        var registry = new ExtensionToolRegistry();
        var registration = new ExtensionToolRegistration
        {
            Name = "spawn",
            Description = "Spawns.",
            ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
            Handler = Handler(),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("spawner", registration)
        );

        Assert.Contains("extension 'spawner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'parametersSchema'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_object_output_schema_is_refused_naming_the_field()
    {
        var registry = new ExtensionToolRegistry();
        var registration = new ExtensionToolRegistration
        {
            Name = "spawn",
            Description = "Spawns.",
            ParametersSchema = Schema(),
            OutputSchema = JsonDocument.Parse("\"text\"").RootElement.Clone(),
            Handler = Handler(),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("spawner", registration)
        );

        Assert.Contains("extension 'spawner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'outputSchema'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_handler_is_refused_naming_the_field()
    {
        var registry = new ExtensionToolRegistry();
        var registration = new ExtensionToolRegistration
        {
            Name = "spawn",
            Description = "Spawns.",
            ParametersSchema = Schema(),
            Handler = null!,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("spawner", registration)
        );

        Assert.Contains("extension 'spawner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'handler'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_exposure_is_refused_naming_the_field()
    {
        var registry = new ExtensionToolRegistry();
        var registration = new ExtensionToolRegistration
        {
            Name = "spawn",
            Description = "Spawns.",
            ParametersSchema = Schema(),
            Exposure = (AgentToolExposure)999,
            Handler = Handler(),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("spawner", registration)
        );

        Assert.Contains("extension 'spawner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("field 'exposure'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_name_in_one_extension_is_refused_naming_both_sources()
    {
        var registry = new ExtensionToolRegistry();
        registry.Register("spawner", Valid("spawn"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("spawner", Valid("spawn"))
        );

        Assert.Contains("extension 'spawner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'spawner__spawn'", exception.Message, StringComparison.Ordinal);
        Assert.Single(registry.List());
    }

    [Fact]
    public void A_duplicate_alias_is_refused_naming_both_extensions()
    {
        var registry = new ExtensionToolRegistry();
        registry.Register("first", Valid("tool"), activationRule: null, alias: "shared");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("second", Valid("other"), activationRule: null, alias: "shared")
        );

        Assert.Contains("extension 'second'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("extension 'first'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'shared'", exception.Message, StringComparison.Ordinal);
        Assert.Single(registry.List());
    }

    [Fact]
    public void An_alias_cannot_shadow_a_later_extensions_canonical_name()
    {
        var registry = new ExtensionToolRegistry();
        registry.Register("first", Valid("owner"), activationRule: null, alias: "second__tool");

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("second", Valid("tool"))
        );

        Assert.Contains("extension 'second'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("extension 'first'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("second__tool", exception.Message, StringComparison.Ordinal);
        Assert.Single(registry.List());
    }

    [Fact]
    public void An_alias_cannot_shadow_a_reserved_core_tool_name()
    {
        var registry = new ExtensionToolRegistry(["read"]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("tools", Valid("echo"), activationRule: null, alias: "read")
        );

        Assert.Contains("extension 'tools'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("core tool 'read'", exception.Message, StringComparison.Ordinal);
        Assert.Empty(registry.List());
    }

    [Fact]
    public void The_32_tool_bound_is_per_extension_and_the_33rd_names_the_limit()
    {
        var registry = new ExtensionToolRegistry();
        for (var index = 0; index < 32; index++)
        {
            registry.Register("spawner", Valid($"tool{index}"));
        }

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Register("spawner", Valid("overflow"))
        );

        Assert.Contains("extension 'spawner'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("32", exception.Message, StringComparison.Ordinal);
        Assert.Equal(32, registry.List().Count);

        registry.Register("other", Valid("tool0"));
        Assert.Equal(33, registry.List().Count);
    }

    private static ExtensionToolRegistration Valid(string name) =>
        new()
        {
            Name = name,
            Description = "Does a thing.",
            ParametersSchema = Schema(),
            Handler = Handler(),
        };

    private static IExtensionToolHandler Handler() =>
        new DelegateToolHandler((_, _, _) => Task.FromResult(new ToolResult("ok", false)));

    private static JsonElement Schema() =>
        JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();
}
