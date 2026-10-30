using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

public sealed class SettingsStoreTests
{
    private const string ModelVariable = "LUNATE_MODEL";
    private const string ApprovalVariable = "LUNATE_APPROVAL";
    private const string LimitVariable = "LUNATE_TOOL_OUTPUT_LIMIT";
    private const string SubagentConcurrencyVariable = "LUNATE_SUBAGENTS_CONCURRENCY";
    private const string SubagentTokenBudgetVariable = "LUNATE_SUBAGENTS_TOKEN_BUDGET";
    private const string SubagentTreeTokenBudgetVariable = "LUNATE_SUBAGENTS_TREE_TOKEN_BUDGET";
    private const string StartupHeaderVariable = "LUNATE_STARTUP_HEADER";
    private const string ToolDetailsVariable = "LUNATE_TOOL_DETAILS";
    private const string UpdateCheckVariable = "LUNATE_UPDATE_CHECK";

    [Fact]
    public void Missing_file_yields_defaults()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Null(settings.Model);
        Assert.Equal(ApprovalPolicy.Ask, settings.Approval);
        Assert.Equal(30_000, settings.ToolOutputLimit);
        Assert.Equal(new SubagentLimits(), settings.Subagents);
        Assert.True(settings.StartupHeader);
        Assert.True(settings.UpdateCheck);
        Assert.False(settings.Tui.Fullscreen);
        Assert.Empty(settings.Favorites);
    }

    [Fact]
    public void A_tui_section_can_opt_into_full_screen()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{"schemaVersion": 2, "tui": {"fullscreen": true}}"""
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.True(settings.Tui.Fullscreen);
    }

    [Theory]
    [InlineData(
        """{"schemaVersion": 2, "tui": {"fullscreen": "yes"}}""",
        "'tui.fullscreen' must be true or false"
    )]
    [InlineData("""{"schemaVersion": 2, "tui": {"width": 80}}""", "unknown key 'tui.width'")]
    [InlineData("""{"schemaVersion": 2, "tui": true}""", "'tui' must be an object")]
    public void An_invalid_tui_section_refuses_the_file(string json, string expected)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("settings.json"), json);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_schema_2_file_can_set_ordered_favorites()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "favorites": ["openrouter/anthropic/claude-sonnet-5-5", "gpt-4o-mini"]
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(["openrouter/anthropic/claude-sonnet-5-5", "gpt-4o-mini"], settings.Favorites);
    }

    [Fact]
    public void Favorites_are_trimmed_when_read()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 2,
              "favorites": [" gpt-4o ", "\tclaude-sonnet-5-5\n"]
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(["gpt-4o", "claude-sonnet-5-5"], settings.Favorites);
    }

    [Fact]
    public void A_schema_1_file_loads_with_empty_favorites()
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

        Assert.Equal("gpt-4o-mini", settings.Model);
        Assert.Empty(settings.Favorites);
    }

    [Theory]
    [InlineData("""["a", "a"]""", "unique")]
    [InlineData("""["a", ""]""", "non-empty")]
    [InlineData("""["a", " "]""", "non-empty")]
    [InlineData("""["a", 2]""", "non-empty")]
    [InlineData("""["a", "b", "c", "d", "e", "f", "g", "h", "i"]""", "8 entries")]
    public void Invalid_favorites_are_refused(string favorites, string expected)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            $$"""
            {
              "schemaVersion": 2,
              "favorites": {{favorites}}
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("'favorites'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_array_favorites_value_is_refused()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """{ "schemaVersion": 2, "favorites": "gpt-4o-mini" }"""
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("'favorites'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("array", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unsupported_schema_version_is_refused()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("settings.json"), """{ "schemaVersion": 3 }""");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("schemaVersion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("1 or 2", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_can_disable_the_startup_header()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "startupHeader": false
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.False(settings.StartupHeader);
    }

    [Fact]
    public void The_startup_header_environment_override_enables_it_again()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "startupHeader": false
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("settings.json"),
            name => name == StartupHeaderVariable ? "1" : null
        );

        Assert.True(settings.StartupHeader);
    }

    [Fact]
    public void The_startup_header_environment_override_disables_it()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("missing.json"),
            name => name == StartupHeaderVariable ? "0" : null
        );

        Assert.False(settings.StartupHeader);
    }

    [Fact]
    public void A_non_boolean_startup_header_in_the_file_is_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "startupHeader": "yes"
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("'startupHeader'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_startup_header_environment_value_names_its_variable()
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == StartupHeaderVariable ? "maybe" : null
            )
        );

        Assert.Contains(StartupHeaderVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("maybe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_can_disable_the_update_check()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "updateCheck": false
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.False(settings.UpdateCheck);
    }

    [Fact]
    public void The_update_check_environment_override_disables_it()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("missing.json"),
            name => name == UpdateCheckVariable ? "0" : null
        );

        Assert.False(settings.UpdateCheck);
    }

    [Fact]
    public void A_non_boolean_update_check_in_the_file_is_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "updateCheck": "yes"
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("'updateCheck' must be true or false", exception.Message);
    }

    [Fact]
    public void An_invalid_update_check_environment_value_names_its_variable()
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == UpdateCheckVariable ? "maybe" : null
            )
        );

        Assert.Contains(UpdateCheckVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("maybe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_can_set_every_field()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "model": "gpt-4o-mini",
              "approval": "auto-edit",
              "output": { "toolResultLimit": 1234 }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal("gpt-4o-mini", settings.Model);
        Assert.Equal(ApprovalPolicy.AutoEdit, settings.Approval);
        Assert.Equal(1234, settings.ToolOutputLimit);
    }

    [Fact]
    public void Environment_overrides_the_file()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "model": "file-model",
              "approval": "auto-edit",
              "output": { "toolResultLimit": 1234 }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("settings.json"),
            name =>
                name switch
                {
                    ModelVariable => "env-model",
                    ApprovalVariable => "ask",
                    LimitVariable => "999",
                    _ => null,
                }
        );

        Assert.Equal("env-model", settings.Model);
        Assert.Equal(ApprovalPolicy.Ask, settings.Approval);
        Assert.Equal(999, settings.ToolOutputLimit);
    }

    [Fact]
    public void The_subagents_section_sets_all_three_limits()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "subagents": { "concurrency": 2, "tokenBudget": 500, "treeTokenBudget": 2000 }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(
            new SubagentLimits(Concurrency: 2, TokenBudget: 500, TreeTokenBudget: 2000),
            settings.Subagents
        );
    }

    [Fact]
    public void Unlimited_is_the_explicit_literal()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "subagents": {
                "concurrency": "unlimited",
                "tokenBudget": "unlimited",
                "treeTokenBudget": "unlimited"
              }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(new SubagentLimits(null, null, null), settings.Subagents);
    }

    [Fact]
    public void Subagent_environment_overrides_apply()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("missing.json"),
            name =>
                name switch
                {
                    SubagentConcurrencyVariable => "7",
                    SubagentTokenBudgetVariable => "12345",
                    SubagentTreeTokenBudgetVariable => "unlimited",
                    _ => null,
                }
        );

        Assert.Equal(
            new SubagentLimits(Concurrency: 7, TokenBudget: 12345, TreeTokenBudget: null),
            settings.Subagents
        );
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("many")]
    [InlineData("Unlimited")]
    public void An_invalid_subagent_environment_value_names_its_variable(string value)
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == SubagentConcurrencyVariable ? value : null
            )
        );

        Assert.Contains(SubagentConcurrencyVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("unlimited", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_subagent_file_values_are_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "subagents": { "concurrency": 0, "tokenBudget": "lots", "other": 1 }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("subagents.concurrency", exception.Message, StringComparison.Ordinal);
        Assert.Contains("subagents.tokenBudget", exception.Message, StringComparison.Ordinal);
        Assert.Contains("subagents.other", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_file_reports_every_problem_once_and_applies_nothing()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "unknown": true,
              "output": { "toolResultLimit": 0, "other": 1 }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("unknown", exception.Message, StringComparison.Ordinal);
        Assert.Contains("toolResultLimit", exception.Message, StringComparison.Ordinal);
        Assert.Contains("other", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_schema_version_is_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("settings.json"), """{ "model": "gpt-4o-mini" }""");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("schemaVersion", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("Ask")]
    [InlineData("auto")]
    public void An_invalid_approval_environment_value_names_its_variable(string value)
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == ApprovalVariable ? value : null
            )
        );

        Assert.Contains(ApprovalVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Yolo_in_the_file_is_rejected_as_a_saved_setting()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "approval": "yolo"
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("yolo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("per-run", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Yolo_in_the_environment_is_rejected_as_a_saved_setting()
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == ApprovalVariable ? "yolo" : null
            )
        );

        Assert.Contains(ApprovalVariable, exception.Message, StringComparison.Ordinal);
        Assert.Contains("yolo", exception.Message, StringComparison.Ordinal);
        Assert.Contains("per-run", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.234")]
    [InlineData("0")]
    [InlineData("-5")]
    public void An_invalid_limit_environment_value_names_its_variable(string value)
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == LimitVariable ? value : null
            )
        );

        Assert.Contains(LimitVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_environment_values_are_ignored()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => "");

        Assert.Null(settings.Model);
        Assert.Equal(ApprovalPolicy.Ask, settings.Approval);
        Assert.Equal(30_000, settings.ToolOutputLimit);
        Assert.Equal(ToolDetails.Collapsed, settings.ToolDetails);
    }

    [Fact]
    public void The_default_tool_details_are_collapsed()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Equal(ToolDetails.Collapsed, settings.ToolDetails);
    }

    [Fact]
    public void A_file_can_expand_the_tool_details()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "toolDetails": "expanded"
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(ToolDetails.Expanded, settings.ToolDetails);
    }

    [Fact]
    public void An_invalid_tool_details_in_the_file_is_reported()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "toolDetails": "Expanded"
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            "'toolDetails' must be 'collapsed' or 'expanded', but was 'Expanded'",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("No settings were applied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_tool_details_environment_override_collapses_the_file_value()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "toolDetails": "expanded"
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("settings.json"),
            name => name == ToolDetailsVariable ? "collapsed" : null
        );

        Assert.Equal(ToolDetails.Collapsed, settings.ToolDetails);
    }

    [Fact]
    public void The_tool_details_environment_override_expands_the_default()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(
            temp.File("missing.json"),
            name => name == ToolDetailsVariable ? "expanded" : null
        );

        Assert.Equal(ToolDetails.Expanded, settings.ToolDetails);
    }

    [Fact]
    public void An_invalid_tool_details_environment_value_names_its_variable()
    {
        using var temp = new TempDirectory();

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(
                temp.File("missing.json"),
                name => name == ToolDetailsVariable ? "full" : null
            )
        );

        Assert.Contains(
            $"{ToolDetailsVariable} must be 'collapsed' or 'expanded', but was 'full'",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void The_roslyn_section_sets_the_loaded_workspace_cap()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "roslyn": { "maxLoadedWorkspaces": 3 }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(3, settings.Roslyn.MaxLoadedWorkspaces);
    }

    [Fact]
    public void The_roslyn_default_cap_is_two()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Equal(2, settings.Roslyn.MaxLoadedWorkspaces);
    }

    [Fact]
    public void Invalid_roslyn_values_are_refused_naming_their_key()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            """
            {
              "schemaVersion": 1,
              "roslyn": { "maxLoadedWorkspaces": 0, "extra": true }
            }
            """
        );

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains(
            "'roslyn.maxLoadedWorkspaces' must be a positive integer",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains("unknown key 'roslyn.extra'", exception.Message, StringComparison.Ordinal);
    }
}
