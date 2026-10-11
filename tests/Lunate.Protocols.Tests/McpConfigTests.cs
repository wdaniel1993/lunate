using Lunate.Agent;

namespace Lunate.Protocols.Tests;

public sealed class McpConfigTests
{
    [Fact]
    public void A_missing_file_loads_zero_servers_and_no_errors()
    {
        using var temp = new TempDirectory();

        McpConfigResult result = McpConfig.Load(temp.File("mcp.json"));

        Assert.Empty(result.Servers);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void A_valid_file_maps_every_field()
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(
            path,
            """
            {
              "mcpServers": {
                "files": {
                  "command": "npx",
                  "args": ["-y", "@modelcontextprotocol/server-filesystem", "/tmp"],
                  "env": { "K": "V" },
                  "cwd": "/optional/dir",
                  "callTimeoutSeconds": 30
                },
                "plain": { "command": "lunate-test-server" }
              }
            }
            """
        );

        McpConfigResult result = McpConfig.Load(path);

        Assert.Empty(result.Errors);
        Assert.Equal(["files", "plain"], result.Servers.Select(server => server.Name));
        McpServerOptions files = result.Servers[0];
        Assert.Equal("npx", files.Command);
        Assert.Equal(["-y", "@modelcontextprotocol/server-filesystem", "/tmp"], files.Arguments);
        Assert.Equal("V", files.Environment!["K"]);
        Assert.Equal("/optional/dir", files.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(30), files.CallTimeout);
        Assert.Equal("files", files.NamespaceName);
        Assert.Equal(ToolExposure.Direct, files.Exposure);
        McpServerOptions plain = result.Servers[1];
        Assert.Equal("plain", plain.NamespaceName);
        Assert.Null(plain.CallTimeout);
        Assert.Null(plain.Environment);
        Assert.Null(plain.WorkingDirectory);
        Assert.Empty(plain.Arguments);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(
            path,
            """
            {
              "mcpServers": {
                "files": {
                  "command": "npx",
                  "disabled": true,
                  "futureField": { "nested": 1 }
                }
              },
              "globalTimeout": 10
            }
            """
        );

        McpConfigResult result = McpConfig.Load(path);

        Assert.Empty(result.Errors);
        Assert.Single(result.Servers);
        Assert.Equal("npx", result.Servers[0].Command);
    }

    [Fact]
    public void A_malformed_entry_is_reported_naming_the_server_and_field_and_skipped()
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(
            path,
            """
            {
              "mcpServers": {
                "good": { "command": "lunate-test-server" },
                "broken": { "args": ["-y"] }
              }
            }
            """
        );

        McpConfigResult result = McpConfig.Load(path);

        McpServerOptions server = Assert.Single(result.Servers);
        Assert.Equal("good", server.Name);
        string error = Assert.Single(result.Errors);
        Assert.Contains("'broken'", error, StringComparison.Ordinal);
        Assert.Contains("'command'", error, StringComparison.Ordinal);
        Assert.Contains(path, error, StringComparison.Ordinal);
        Assert.Contains("skipped", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "command": "x", "args": "-y" }""", "'args'")]
    [InlineData("""{ "command": "x", "env": { "K": 1 } }""", "'env'")]
    [InlineData("""{ "command": "x", "env": "K=V" }""", "'env'")]
    [InlineData("""{ "command": "x", "cwd": 3 }""", "'cwd'")]
    [InlineData("""{ "command": "x", "callTimeoutSeconds": "30" }""", "'callTimeoutSeconds'")]
    [InlineData("""{ "command": "x", "callTimeoutSeconds": 0 }""", "'callTimeoutSeconds'")]
    [InlineData("""{ "command": "" }""", "'command'")]
    public void A_malformed_field_is_reported_and_the_server_skipped(string entry, string field)
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(path, $$"""{ "mcpServers": { "srv": {{entry}} } }""");

        McpConfigResult result = McpConfig.Load(path);

        Assert.Empty(result.Servers);
        string error = Assert.Single(result.Errors);
        Assert.Contains("'srv'", error, StringComparison.Ordinal);
        Assert.Contains(field, error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_file_reports_its_path_and_continues_without_servers()
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(path, """{ "mcpServers": { "files": """);

        McpConfigResult result = McpConfig.Load(path);

        Assert.Empty(result.Servers);
        string error = Assert.Single(result.Errors);
        Assert.Contains(path, error, StringComparison.Ordinal);
        Assert.Contains("JSON", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrong_mcpServers_type_is_a_file_error()
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(path, """{ "mcpServers": ["files"] }""");

        McpConfigResult result = McpConfig.Load(path);

        Assert.Empty(result.Servers);
        string error = Assert.Single(result.Errors);
        Assert.Contains(path, error, StringComparison.Ordinal);
        Assert.Contains("mcpServers", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_object_entry_is_reported_naming_the_server()
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(path, """{ "mcpServers": { "files": 3 } }""");

        McpConfigResult result = McpConfig.Load(path);

        Assert.Empty(result.Servers);
        string error = Assert.Single(result.Errors);
        Assert.Contains("'files'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_without_mcpServers_loads_zero_servers_and_no_errors()
    {
        using var temp = new TempDirectory();
        string path = temp.File("mcp.json");
        File.WriteAllText(path, "{}");

        McpConfigResult result = McpConfig.Load(path);

        Assert.Empty(result.Servers);
        Assert.Empty(result.Errors);
    }
}
