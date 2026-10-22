using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The busy send key setting (<c>prompt.busyEnter</c>): queue by default, steer on request, and
/// the environment override.
/// </summary>
public sealed class SettingsStorePromptTests
{
    private const string BusyEnterVariable = "LUNATE_BUSY_ENTER";

    [Fact]
    public void Missing_file_defaults_the_busy_enter_key_to_queue()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Equal(BusyEnterMode.Queue, settings.Prompt.BusyEnter);
    }

    [Fact]
    public void A_settings_file_reads_the_busy_enter_key()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "prompt": { "busyEnter": "steer" }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(BusyEnterMode.Steer, settings.Prompt.BusyEnter);
    }

    [Fact]
    public void The_environment_overrides_the_file()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "prompt": { "busyEnter": "steer" }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("settings.json"),
            name => name == BusyEnterVariable ? "queue" : null
        );

        Assert.Equal(BusyEnterMode.Queue, settings.Prompt.BusyEnter);
    }

    [Fact]
    public void An_invalid_file_value_names_the_accepted_values()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "prompt": { "busyEnter": "interrupt" }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            "'prompt.busyEnter' must be 'queue' or 'steer'",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("'interrupt'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_environment_value_names_the_variable_and_the_accepted_values()
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == BusyEnterVariable ? "interrupt" : null
            )
        );

        Assert.Contains(
            $"{BusyEnterVariable} must be 'queue' or 'steer'",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("'interrupt'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_key_inside_prompt_is_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "prompt": { "busyEnter": "queue", "interrupt": true }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            "unknown key 'prompt.interrupt'",
            exception.Message,
            StringComparison.Ordinal
        );
    }
}
