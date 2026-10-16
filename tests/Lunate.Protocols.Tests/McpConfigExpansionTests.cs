namespace Lunate.Protocols.Tests;

/// <summary>
/// The Claude Code-compatible <c>${VAR}</c> / <c>${VAR:-default}</c> expansion at load: values
/// only, defaults, literal-keeping warnings for unset variables, and the raw definitions kept for
/// hashing.
/// </summary>
public sealed class McpConfigExpansionTests
{
    [Fact]
    public void A_set_variable_expands_in_command_args_and_env_values()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(
            temp,
            """
            {
              "command": "${CMD}",
              "args": ["--token", "${TOKEN}"],
              "env": { "K": "${VALUE}" }
            }
            """,
            name =>
                name switch
                {
                    "CMD" => "run",
                    "TOKEN" => "tok",
                    "VALUE" => "v",
                    _ => null,
                }
        );

        McpServerOptions server = Assert.Single(result.Servers);
        Assert.Equal("run", server.Command);
        Assert.Equal(["--token", "tok"], server.Arguments);
        Assert.Equal("v", server.Environment!["K"]);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_default_fills_in_when_the_variable_is_unset()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(temp, """{ "command": "${CMD:-fallback}" }""", _ => null);

        Assert.Equal("fallback", Assert.Single(result.Servers).Command);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_set_variable_wins_over_its_default()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(temp, """{ "command": "${CMD:-fallback}" }""", _ => "actual");

        Assert.Equal("actual", Assert.Single(result.Servers).Command);
    }

    [Fact]
    public void An_empty_default_fills_in()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(
            temp,
            """{ "command": "run", "args": ["${CMD:-}", "next"] }""",
            _ => null
        );

        Assert.Equal(["", "next"], Assert.Single(result.Servers).Arguments);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_variable_set_to_empty_expands_to_empty_without_a_warning()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(temp, """{ "command": "${CMD}" }""", _ => "");

        Assert.Equal("", Assert.Single(result.Servers).Command);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void An_unset_variable_without_a_default_warns_and_stays_literal()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(
            temp,
            """{ "command": "run", "args": ["${MISSING}"] }""",
            _ => null
        );

        Assert.Equal(["${MISSING}"], Assert.Single(result.Servers).Arguments);
        McpServerWarning warning = Assert.Single(result.Warnings);
        Assert.Equal("srv", warning.Server);
        Assert.Equal("MISSING", warning.Variable);
        Assert.Contains("'srv'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'MISSING'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("${MISSING}", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_unset_variable_warns_once_per_server()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(
            temp,
            """{ "command": "${MISSING}", "args": ["${MISSING}"] }""",
            _ => null
        );

        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Env_keys_are_never_expanded()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(
            temp,
            """{ "command": "run", "env": { "${KEY}": "${VALUE}" } }""",
            name => name == "VALUE" ? "v" : null
        );

        IReadOnlyDictionary<string, string> env = Assert.Single(result.Servers).Environment!;
        Assert.Equal("v", env["${KEY}"]);
    }

    [Fact]
    public void Raw_definitions_stay_as_written()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(
            temp,
            """{ "command": "${CMD}", "env": { "K": "${VALUE}" } }""",
            name => name == "CMD" ? "run" : "v"
        );

        Assert.Equal("run", Assert.Single(result.Servers).Command);
        McpServerOptions raw = Assert.Single(result.RawServers);
        Assert.Equal("${CMD}", raw.Command);
        Assert.Equal("${VALUE}", raw.Environment!["K"]);
    }

    [Theory]
    [InlineData("$VAR")]
    [InlineData("${}")]
    [InlineData("${1BAD}")]
    [InlineData("${UNCLOSED")]
    [InlineData("plain text")]
    public void Text_that_is_not_a_reference_stays_literal(string argument)
    {
        using var temp = new TempDirectory();

        McpConfigResult result = Load(
            temp,
            $$"""{ "command": "run", "args": ["{{argument}}"] }""",
            _ => null
        );

        Assert.Equal([argument], Assert.Single(result.Servers).Arguments);
        Assert.Empty(result.Warnings);
    }

    private static McpConfigResult Load(
        TempDirectory temp,
        string entry,
        Func<string, string?> environment
    )
    {
        string path = temp.File("mcp.json");
        File.WriteAllText(path, $$"""{ "mcpServers": { "srv": {{entry}} } }""");
        return McpConfig.Load(path, environment);
    }
}
