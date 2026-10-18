using Lunate.Agent;

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
