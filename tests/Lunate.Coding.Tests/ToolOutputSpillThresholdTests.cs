using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class ToolOutputSpillThresholdTests
{
    private const string SpillThresholdVariable = "LUNATE_TOOL_SPILL_THRESHOLD";

    [Fact]
    public void Spill_threshold_defaults_to_two_thousand_tokens()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Equal(2_000, settings.SpillThresholdTokens);
        Assert.Equal(2_000, new AgentHarnessOptions().SpillThresholdTokens);
    }

    [Fact]
    public void A_file_can_set_the_spill_threshold()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "output": {"spillThresholdTokens": 1250}}"""
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(1_250, settings.SpillThresholdTokens);
    }

    [Fact]
    public void The_environment_overrides_the_file_spill_threshold()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "output": {"spillThresholdTokens": 1250}}"""
        );

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("settings.json"),
            name => name == SpillThresholdVariable ? "800" : null
        );

        Assert.Equal(800, settings.SpillThresholdTokens);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    public void An_invalid_environment_threshold_names_its_variable(string value)
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == SpillThresholdVariable ? value : null
            )
        );

        Assert.Contains(SpillThresholdVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_file_threshold_names_its_setting()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "output": {"spillThresholdTokens": 0}}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("output.spillThresholdTokens", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_output_key_names_its_setting()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "output": {"unknown": 1}}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("output.unknown", exception.Message, StringComparison.Ordinal);
    }
}
