using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

/// <summary>The <c>thinking</c> and <c>thinkingFull</c> settings (D7 of reasoning-end-to-end).</summary>
public sealed class SettingsStoreThinkingTests
{
    [Fact]
    public void A_missing_file_yields_the_documented_thinking_defaults()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Equal(ThinkingLevel.Off, settings.Thinking);
        Assert.False(settings.ThinkingFull);
    }

    [Theory]
    [InlineData("off", ThinkingLevel.Off)]
    [InlineData("low", ThinkingLevel.Low)]
    [InlineData("medium", ThinkingLevel.Medium)]
    [InlineData("high", ThinkingLevel.High)]
    public void A_file_can_set_the_thinking_level(string literal, ThinkingLevel expected)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            $$"""
            {
              "schemaVersion": 1,
              "thinking": "{{literal}}"
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(expected, settings.Thinking);
    }

    [Fact]
    public void A_schema_2_file_keeps_the_thinking_settings()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "thinking": "high",
              "thinkingFull": true,
              "favorites": ["gpt-4o-mini"]
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(ThinkingLevel.High, settings.Thinking);
        Assert.True(settings.ThinkingFull);
        Assert.Equal(["gpt-4o-mini"], settings.Favorites);
    }

    [Fact]
    public void A_file_without_the_thinking_keys_keeps_the_defaults()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "model": "gpt-4o-mini"
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(ThinkingLevel.Off, settings.Thinking);
        Assert.False(settings.ThinkingFull);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("High")]
    [InlineData("")]
    public void An_invalid_thinking_literal_is_reported_naming_key_and_value(string literal)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            $$"""
            {
              "schemaVersion": 1,
              "thinking": "{{literal}}"
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            $"'thinking' must be 'off', 'low', 'medium' or 'high', but was '{literal}'",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_string_thinking_value_is_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "thinking": 3
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            "'thinking' must be 'off', 'low', 'medium' or 'high'",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_can_commit_the_full_thinking_text()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "thinkingFull": true
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.True(settings.ThinkingFull);
    }

    [Fact]
    public void A_file_can_commit_the_collapsed_thinking_text()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "thinkingFull": false
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.False(settings.ThinkingFull);
    }

    [Fact]
    public void A_non_boolean_thinking_full_value_is_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "thinkingFull": "yes"
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            "'thinkingFull' must be true or false",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }
}
