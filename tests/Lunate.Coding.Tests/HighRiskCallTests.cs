using System.Text.Json;

namespace Lunate.Coding.Tests;

/// <summary>
/// The one place that decides which calls the approval panel must always show: the commands that
/// can approve or install code, and anything that writes the state root those approvals live in.
/// </summary>
public sealed class HighRiskCallTests
{
    private static readonly string Workspace = Path.Combine(
        Path.GetTempPath(),
        "high-risk-workspace"
    );
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "high-risk-home");
    private static readonly string Store = Path.Combine(Home, ".lunate");

    [Theory]
    [InlineData("lunate trust --yes")]
    [InlineData("lunate trust")]
    [InlineData("lunate install Acme.Toolkit")]
    [InlineData("lunate uninstall acme-toolkit")]
    [InlineData("env -u LUNATE_SESSION lunate trust --yes")]
    [InlineData("cd repo && /usr/local/bin/lunate install Acme.Toolkit")]
    [InlineData("bash -lc \"lunate trust --yes\"")]
    [InlineData("lunate --no-extensions trust")]
    public void A_bash_command_that_changes_trust_is_high_risk(string command) =>
        Assert.True(IsBash(command));

    [Theory]
    [InlineData("lunate --version")]
    [InlineData("git status --short")]
    [InlineData("echo lunate installs packages")]
    [InlineData("npm test")]
    [InlineData("cat ~/notes/todo.md")]
    public void An_ordinary_bash_command_is_not(string command) => Assert.False(IsBash(command));

    [Theory]
    [InlineData("cat ~/.lunate/trust.json", true)]
    [InlineData("echo x > ~/.lunate/trust.json", true)]
    [InlineData("rm -rf \"$LUNATE_HOME\"", true)]
    [InlineData("echo x > $LUNATE_HOME/trust.json", true)]
    [InlineData("cd ~/.lunate && ls", true)]
    [InlineData("cat ~/notes/todo.md", false)]
    [InlineData("cat .lunate/trust.json", false)]
    public void A_bash_command_that_names_the_state_root_is_high_risk(
        string command,
        bool expected
    ) => Assert.Equal(expected, IsBash(command));

    [Fact]
    public void A_write_of_a_path_inside_the_state_root_is_high_risk()
    {
        Assert.True(
            IsWrite(WriteTool.ToolName, Path.Combine(Store, "trust.json"), workspace: Workspace)
        );
        Assert.True(IsWrite(WriteTool.ToolName, "~/.lunate/settings.json", Workspace));
    }

    [Fact]
    public void A_relative_write_resolves_against_the_workspace_it_runs_in()
    {
        // The default location only when it is where the product's store actually resolves.
        Assert.True(IsWrite(WriteTool.ToolName, ".lunate/trust.json", workspace: Home));
        Assert.False(IsWrite(WriteTool.ToolName, ".lunate/trust.json", workspace: Workspace));
    }

    [Fact]
    public void An_edit_into_the_state_root_is_high_risk() =>
        Assert.True(IsWrite(EditTool.ToolName, Path.Combine(Store, "settings.json"), Workspace));

    [Fact]
    public void An_ordinary_write_is_not() =>
        Assert.False(IsWrite(WriteTool.ToolName, "src/app.cs", Workspace));

    [Fact]
    public void A_read_of_the_state_root_is_not_a_high_risk_write() =>
        Assert.False(IsWrite("read", Path.Combine(Store, "trust.json"), Workspace));

    [Fact]
    public void Another_tool_with_a_command_argument_is_not_classified() =>
        Assert.False(IsTool("codemode__run", Args("""{"command":"lunate trust --yes"}""")));

    [Fact]
    public void Arguments_that_are_not_an_object_are_not_classified() =>
        Assert.False(IsTool(BashTool.ToolName, Args("\"lunate trust --yes\"")));

    private static bool IsBash(string command) =>
        HighRiskCall.Is(
            BashTool.ToolName,
            Args(JsonSerializer.Serialize(new { command })),
            Workspace,
            Store,
            Home
        );

    private static bool IsWrite(string toolName, string path, string workspace) =>
        HighRiskCall.Is(
            toolName,
            Args(
                JsonSerializer.Serialize(
                    new
                    {
                        path,
                        content = "x",
                        old_text = "x",
                        new_text = "y",
                    }
                )
            ),
            workspace,
            Store,
            Home
        );

    private static bool IsTool(string toolName, JsonElement args) =>
        HighRiskCall.Is(toolName, args, Workspace, Store, Home);

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
