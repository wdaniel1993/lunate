namespace Lunate.Coding.Tests;

public sealed class SettingsStoreTests
{
    private const string ModelVariable = "LUNATE_MODEL";
    private const string ApprovalVariable = "LUNATE_APPROVAL";
    private const string LimitVariable = "LUNATE_TOOL_OUTPUT_LIMIT";

    [Fact]
    public void Missing_file_yields_defaults()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Null(settings.Model);
        Assert.Equal(ApprovalPolicy.Ask, settings.Approval);
        Assert.Equal(30_000, settings.ToolOutputLimit);
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
              "approval": "auto",
              "output": { "toolResultLimit": 1234 }
            }
            """
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal("gpt-4o-mini", settings.Model);
        Assert.Equal(ApprovalPolicy.Auto, settings.Approval);
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
              "approval": "auto",
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
}
