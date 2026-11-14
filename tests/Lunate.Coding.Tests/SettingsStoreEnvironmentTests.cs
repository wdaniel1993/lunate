namespace Lunate.Coding.Tests;

/// <summary>
/// The user-scope <c>environment</c> key: it resolves through the same parser <c>--env</c> uses, an
/// invalid value fails the file, a project file may not carry it, and the flag wins for the run.
/// </summary>
public sealed class SettingsStoreEnvironmentTests
{
    [Fact]
    public void The_key_is_unset_by_default()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Null(settings.Environment);
    }

    [Theory]
    [InlineData("docker:alpine")]
    [InlineData("podman:ghcr.io/org/image:tag")]
    public void The_key_resolves_a_container_environment(string value)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            $$"""{"schemaVersion":2,"environment":"{{value}}"}"""
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(value, settings.Environment?.Description);
    }

    [Fact]
    public void An_invalid_value_fails_the_file_with_both_shapes()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion":2,"environment":"alpine"}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("'environment'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "docker:<image> or podman:<image>",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void A_project_file_may_not_carry_the_key()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("user.json"), """{"schemaVersion":2}""");
        File.WriteAllText(
            temp.File("project.json"),
            """{"schemaVersion":2,"environment":"docker:alpine"}"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.ResolveWithProject(
                temp.File("user.json"),
                temp.File("project.json"),
                [],
                _ => null
            )
        );

        Assert.Contains(
            "project settings key 'environment' is not allowed",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_flag_wins_over_the_settings_value()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion":2,"environment":"podman:alpine"}"""
        );
        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.True(
            LaunchOptions.TryParse(["--env", "docker:debian"], out LaunchOptions launch, out _)
        );
        AgentSettings applied = SettingsStore.ApplyLaunch(settings, launch);

        Assert.Equal("docker:debian", applied.Environment?.Description);

        AgentSettings kept = SettingsStore.ApplyLaunch(settings, new LaunchOptions());
        Assert.Equal("podman:alpine", kept.Environment?.Description);
    }
}
