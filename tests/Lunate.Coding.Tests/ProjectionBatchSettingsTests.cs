using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class ProjectionBatchSettingsTests
{
    private const string BatchTurnsVariable = "LUNATE_PROJECTION_BATCH_TURNS";
    private const string MaskTurnsVariable = "LUNATE_PROJECTION_MASK_TURNS";
    private const string MaskErrorTurnsVariable = "LUNATE_PROJECTION_MASK_ERROR_TURNS";

    [Fact]
    public void Projection_settings_have_their_documented_defaults()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Equal(4, settings.ProjectionBatchTurns);
        Assert.Equal(4, new AgentHarnessOptions().ProjectionBatchTurns);
        Assert.Equal(8, settings.ProjectionMaskTurns);
        Assert.Equal(8, new AgentHarnessOptions().ProjectionMaskTurns);
        Assert.Equal(32, settings.ProjectionMaskErrorTurns);
        Assert.Equal(32, new AgentHarnessOptions().ProjectionMaskErrorTurns);
    }

    [Fact]
    public void A_file_can_set_projection_mask_windows()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "projection": {"maskTurns": 10, "maskErrorTurns": 40}}"""
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(10, settings.ProjectionMaskTurns);
        Assert.Equal(40, settings.ProjectionMaskErrorTurns);
    }

    [Fact]
    public void The_environment_overrides_the_mask_windows()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("settings.json"), """{"schemaVersion": 2}""");

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("settings.json"),
            name =>
                name == MaskTurnsVariable
                    ? "12"
                    : name == MaskErrorTurnsVariable
                        ? "48"
                        : null
        );

        Assert.Equal(12, settings.ProjectionMaskTurns);
        Assert.Equal(48, settings.ProjectionMaskErrorTurns);
    }

    [Fact]
    public void An_invalid_mask_value_names_its_setting()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "projection": {"maskTurns": 0}}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("projection.maskTurns", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_window_below_the_success_window_is_a_problem()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "projection": {"maskTurns": 10, "maskErrorTurns": 3}}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            "projection.maskErrorTurns",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void An_unknown_projection_key_names_its_setting()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "projection": {"ghostTurns": 7}}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("projection.ghostTurns", exception.Message, StringComparison.Ordinal);
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
