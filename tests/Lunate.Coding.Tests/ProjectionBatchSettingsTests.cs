using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class ProjectionBatchSettingsTests
{
    private const string BatchTurnsVariable = "LUNATE_PROJECTION_BATCH_TURNS";

    [Fact]
    public void Projection_batch_turns_default_to_four()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Equal(4, settings.ProjectionBatchTurns);
        Assert.Equal(4, new AgentHarnessOptions().ProjectionBatchTurns);
    }

    [Fact]
    public void A_file_can_set_projection_batch_turns()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "projection": {"batchTurns": 7}}"""
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(7, settings.ProjectionBatchTurns);
    }

    [Fact]
    public void The_environment_overrides_projection_batch_turns()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "projection": {"batchTurns": 7}}"""
        );

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("settings.json"),
            name => name == BatchTurnsVariable ? "3" : null
        );

        Assert.Equal(3, settings.ProjectionBatchTurns);
    }

    [Fact]
    public void An_invalid_batch_turns_value_names_its_setting()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "projection": {"batchTurns": 0}}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("projection.batchTurns", exception.Message, StringComparison.Ordinal);
    }
}
