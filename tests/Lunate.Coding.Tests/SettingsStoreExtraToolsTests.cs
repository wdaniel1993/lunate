using Lunate.Agent;

namespace Lunate.Coding.Tests;

/// <summary>
/// The optional search tools setting (<c>tools.extra</c>) and its precedence: CLI over environment
/// over the settings file over the empty default.
/// </summary>
public sealed class SettingsStoreExtraToolsTests
{
    private const string ExtraToolsVariable = "LUNATE_EXTRA_TOOLS";

    [Fact]
    public void Missing_file_defaults_the_extra_tools_to_empty()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Empty(settings.Tools.Extra);
    }

    [Fact]
    public void A_settings_file_reads_the_extra_tools()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "tools": { "extra": ["grep", "ls"] }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(["grep", "ls"], settings.Tools.Extra);
    }

    [Fact]
    public void An_unknown_extra_tool_names_the_accepted_values()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "tools": { "extra": ["grep", "wat"] }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("'grep', 'find' or 'ls'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'wat'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_core_tool_name_is_rejected()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "tools": { "extra": ["bash"] }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("core tool 'bash'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'grep', 'find' or 'ls'", exception.Message, StringComparison.Ordinal);
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
              "tools": { "extra": ["ls"] }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("settings.json"),
            name => name == ExtraToolsVariable ? "grep,find" : null
        );

        Assert.Equal(["grep", "find"], settings.Tools.Extra);
    }

    [Fact]
    public void An_unknown_environment_name_names_the_variable()
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == ExtraToolsVariable ? "grep,wat" : null
            )
        );

        Assert.Contains(ExtraToolsVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("'grep', 'find' or 'ls'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tools_flag_extracts_the_optional_names()
    {
        Assert.True(
            LaunchOptions.TryParse(
                ["--tools", "grep,find"],
                out LaunchOptions options,
                out string? error
            ),
            error
        );

        Assert.Equal(["grep", "find"], options.ExtraTools);
        Assert.Null(options.Tools);
    }

    [Fact]
    public void The_tools_flag_keeps_core_names_as_the_filter()
    {
        Assert.True(
            LaunchOptions.TryParse(
                ["--tools", "read,grep"],
                out LaunchOptions options,
                out string? error
            ),
            error
        );

        Assert.Equal(["read"], options.Tools);
        Assert.Equal(["grep"], options.ExtraTools);
    }

    [Fact]
    public void A_tools_flag_without_optional_names_clears_the_extras()
    {
        Assert.True(
            LaunchOptions.TryParse(
                ["--tools", "read"],
                out LaunchOptions options,
                out string? error
            ),
            error
        );

        Assert.NotNull(options.ExtraTools);
        Assert.Empty(options.ExtraTools);
        Assert.Equal(["read"], options.Tools);
    }

    [Fact]
    public void The_tools_flag_with_only_extras_conflicts_with_no_tools()
    {
        Assert.False(
            LaunchOptions.TryParse(["--tools", "grep", "--no-tools"], out _, out string? error)
        );

        Assert.Contains("--tools and --no-tools", error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_launch_tools_override_the_environment()
    {
        using var temp = new TempDirectory();
        AgentSettings settings = SettingsStore.Resolve(
            temp.File("missing.json"),
            name => name == ExtraToolsVariable ? "grep,find" : null
        );

        AgentSettings resolved = SettingsStore.ApplyLaunch(
            settings,
            new LaunchOptions { ExtraTools = ["ls"] }
        );

        Assert.Equal(["ls"], resolved.Tools.Extra);
    }

    [Fact]
    public void A_launch_without_the_tools_flag_keeps_the_environment()
    {
        using var temp = new TempDirectory();
        AgentSettings settings = SettingsStore.Resolve(
            temp.File("missing.json"),
            name => name == ExtraToolsVariable ? "grep,find" : null
        );

        AgentSettings resolved = SettingsStore.ApplyLaunch(settings, new LaunchOptions());

        Assert.Equal(["grep", "find"], resolved.Tools.Extra);
    }

    [Fact]
    public void An_unknown_key_inside_tools_is_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "tools": { "extra": ["ls"], "builtin": [] }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("unknown key 'tools.builtin'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tools_section_must_be_an_object()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "tools": ["grep"]
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("'tools' must be an object", exception.Message, StringComparison.Ordinal);
    }
}
