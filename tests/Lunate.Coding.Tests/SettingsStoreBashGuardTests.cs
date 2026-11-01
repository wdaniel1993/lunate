using System.Reflection;

namespace Lunate.Coding.Tests;

public sealed class SettingsStoreBashGuardTests
{
    [Fact]
    public void The_guard_defaults_to_on()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.True(ReadBashGuard(settings));
    }

    [Fact]
    public void The_guard_accepts_false()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("settings.json"), """{"schemaVersion":2,"bashGuard":false}""");

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.False(ReadBashGuard(settings));
    }

    [Fact]
    public void A_non_boolean_guard_setting_is_rejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion":2,"bashGuard":"false"}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            "'bashGuard' must be true or false",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_guard_setting_is_not_a_public_dotnet_member()
    {
        PropertyInfo? property = typeof(AgentSettings).GetProperty(
            "BashGuard",
            BindingFlags.Instance | BindingFlags.Public
        );

        Assert.Null(property);
    }

    private static bool ReadBashGuard(AgentSettings settings) =>
        (bool)
            typeof(AgentSettings)
                .GetProperty("BashGuard", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(settings)!;
}
