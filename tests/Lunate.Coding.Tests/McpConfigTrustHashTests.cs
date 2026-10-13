using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

/// <summary>
/// The project mcp.json trust hash is over the raw server definitions: stable across formatting,
/// key order and server order edits, changing when a definition changes, and never influenced by
/// environment values.
/// </summary>
public sealed class McpConfigTrustHashTests
{
    [Fact]
    public void Formatting_key_order_and_server_order_edits_do_not_change_the_hash()
    {
        using var temp = new TempDirectory();
        string first = temp.File("first.json");
        File.WriteAllText(
            first,
            """
            {
              "mcpServers": {
                "alpha": { "command": "run-a", "args": ["one", "two"], "env": { "A": "1", "B": "2" } },
                "beta": { "command": "run-b", "cwd": "/work", "callTimeoutSeconds": 12.5 }
              }
            }
            """
        );
        string second = temp.File("second.json");
        File.WriteAllText(
            second,
            """
            {"mcpServers":{"beta":{"callTimeoutSeconds":12.5,"cwd":"/work","command":"run-b"},"alpha":{"env":{"B":"2","A":"1"},"args":["one","two"],"command":"run-a"}}}
            """
        );

        Assert.Equal(McpConfigTrustHash.ComputeFile(first), McpConfigTrustHash.ComputeFile(second));
    }

    [Fact]
    public void A_definition_change_changes_the_hash()
    {
        using var temp = new TempDirectory();
        string first = ConfigDefinition(temp, "first.json", "run-a");
        string second = ConfigDefinition(temp, "second.json", "run-b");

        Assert.NotEqual(
            McpConfigTrustHash.ComputeFile(first),
            McpConfigTrustHash.ComputeFile(second)
        );
    }

    [Fact]
    public void Environment_values_never_influence_the_hash()
    {
        using var temp = new TempDirectory();
        string config = temp.File("mcp.json");
        File.WriteAllText(
            config,
            """{ "mcpServers": { "srv": { "command": "x", "env": { "TOKEN": "${LUNATE_TEST_TOKEN}" } } } }"""
        );

        string without = McpConfigTrustHash.ComputeFile(config);
        Environment.SetEnvironmentVariable("LUNATE_TEST_TOKEN", "the test value");
        try
        {
            Assert.Equal(without, McpConfigTrustHash.ComputeFile(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("LUNATE_TEST_TOKEN", null);
        }
    }

    [Fact]
    public void No_secret_value_appears_in_the_trust_file()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        string config = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(
            config,
            """{ "mcpServers": { "srv": { "command": "x", "env": { "TOKEN": "the test value" } } } }"""
        );
        string storePath = temp.File("store");
        var store = new ExtensionTrustStore(new ExtensionHostOptions { StorePath = storePath });
        store.RecordMcpConfigTrusted(
            worktree,
            worktree,
            McpConfigTrustHash.ComputeFile(config),
            DateTimeOffset.UtcNow
        );

        string trustFile = File.ReadAllText(Path.Combine(storePath, "trust.json"));

        Assert.DoesNotContain("the test value", trustFile, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_file_hashes_like_an_empty_definition_set()
    {
        using var temp = new TempDirectory();
        string missing = temp.File("missing.json");
        string empty = temp.File("empty.json");
        File.WriteAllText(empty, """{ "mcpServers": {} }""");

        Assert.Equal(
            McpConfigTrustHash.ComputeFile(missing),
            McpConfigTrustHash.ComputeFile(empty)
        );
        Assert.Equal(McpConfigTrustHash.Compute([]), McpConfigTrustHash.ComputeFile(missing));
    }

    private static string ConfigDefinition(TempDirectory temp, string name, string command)
    {
        string path = temp.File(name);
        File.WriteAllText(path, $$"""{ "mcpServers": { "srv": { "command": "{{command}}" } } }""");
        return path;
    }
}
