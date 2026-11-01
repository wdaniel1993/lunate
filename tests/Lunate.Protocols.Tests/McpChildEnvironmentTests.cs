using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.TestSupport;

namespace Lunate.Protocols.Tests;

/// <summary>
/// #449/#455 — a stdio MCP server inherits Lunate's whole environment, provider keys included.
/// These tests spawn the shared TestMcpServer for real and read the child's own environment
/// report: the fixture variables and the child process prove the rule, never a stubbed dictionary.
/// The variables are set on the real process environment and restored afterwards.
/// </summary>
public sealed class McpChildEnvironmentTests
{
    private const string AnthropicVariable = "ANTHROPIC_API_KEY";
    private const string ProviderVariable = "MY_PROVIDER_TOKEN";
    private const string ParentFlagVariable = "LUNATE_TEST_PARENT_FLAG";

    private static readonly string AnthropKeyValue = "sk-ant-fixture-9876543210";
    private static readonly string ProviderKeyValue = "sk-provider-fixture-0123456789";
    private static readonly string ParentFlagValue = "parent-flag-visible";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Known_credential_values_do_not_reach_the_child_unless_declared()
    {
        using var temp = new TempDirectory();
        IDisposable restore = SetTestEnvironment(
            (AnthropicVariable, AnthropKeyValue),
            (ProviderVariable, ProviderKeyValue),
            (ParentFlagVariable, ParentFlagValue)
        );
        try
        {
            await using var host = new McpServerHost(
                EnvServerOptions(
                    temp.File("marker.txt"),
                    withheld: [AnthropKeyValue, ProviderKeyValue]
                )
            );

            string[] names = await NamesAsync(host);

            Assert.DoesNotContain(AnthropicVariable, names);
            Assert.DoesNotContain(ProviderVariable, names);

            // Everything Lunate does not know stays inherited, exactly as before.
            Assert.Contains(ParentFlagVariable, names);
            Assert.Equal(ParentFlagValue, await ValueAsync(host, ParentFlagVariable));
            Assert.NotEqual("(unset)", await ValueAsync(host, "PATH"));
        }
        finally
        {
            restore.Dispose();
        }
    }

    [Fact]
    public async Task A_declared_variable_still_receives_its_value()
    {
        using var temp = new TempDirectory();
        IDisposable restore = SetTestEnvironment(
            (AnthropicVariable, AnthropKeyValue),
            (ProviderVariable, ProviderKeyValue)
        );
        try
        {
            await using var host = new McpServerHost(
                EnvServerOptions(
                    temp.File("marker.txt"),
                    withheld: [AnthropKeyValue, ProviderKeyValue],
                    declaredEnvironment: new Dictionary<string, string>
                    {
                        [AnthropicVariable] = AnthropKeyValue,
                    }
                )
            );

            string[] names = await NamesAsync(host);

            Assert.Contains(AnthropicVariable, names);
            Assert.Equal(AnthropKeyValue, await ValueAsync(host, AnthropicVariable));

            // The name the server did not declare is still withheld.
            Assert.DoesNotContain(ProviderVariable, names);
        }
        finally
        {
            restore.Dispose();
        }
    }

    [Fact]
    public async Task Without_a_withheld_set_the_child_keeps_full_inheritance()
    {
        using var temp = new TempDirectory();
        IDisposable restore = SetTestEnvironment(
            (AnthropicVariable, AnthropKeyValue),
            (ParentFlagVariable, ParentFlagValue)
        );
        try
        {
            await using var host = new McpServerHost(EnvServerOptions(temp.File("marker.txt")));

            string[] names = await NamesAsync(host);

            // Today's behaviour: today the host passes no withheld values, so the child receives
            // the parent environment as is. The rule only tightens what Lunate knows.
            Assert.Contains(AnthropicVariable, names);
            Assert.Contains(ParentFlagVariable, names);
        }
        finally
        {
            restore.Dispose();
        }
    }

    /// <summary>The env-reporting options: the marker and the tool gate are declared.</summary>
    private static McpServerOptions EnvServerOptions(
        string markerPath,
        IReadOnlyList<string>? withheld = null,
        IReadOnlyDictionary<string, string>? declaredEnvironment = null
    )
    {
        Dictionary<string, string> environment = new(StringComparer.Ordinal)
        {
            ["LUNATE_TEST_MCP_MARKER"] = markerPath,
            ["LUNATE_TEST_MCP_ENV_TOOL"] = "1",
        };
        foreach (
            (string name, string value) in declaredEnvironment ?? new Dictionary<string, string>()
        )
        {
            environment[name] = value;
        }

        return new McpServerOptions(
            Name: "server",
            Command: TestMcpServer.DotnetPath,
            Arguments: ["exec", TestMcpServer.ServerDll],
            Environment: environment
        )
        {
            WithheldCredentialValues = withheld,
        };
    }

    private static async Task<string[]> NamesAsync(McpServerHost host)
    {
        ToolResult result = await ExecuteAsync(host, "env_names");
        Assert.False(result.IsError);
        return (
            JsonSerializer.Deserialize<JsonElement>(result.Output).Deserialize<string[]>()
            ?? throw new InvalidOperationException("env_names returned no array")
        );
    }

    private static async Task<string> ValueAsync(McpServerHost host, string name) =>
        (await ExecuteAsync(host, "env_value", name)).Output;

    private static async Task<ToolResult> ExecuteAsync(
        McpServerHost host,
        string tool,
        string? argument = null
    )
    {
        IReadOnlyList<ITool> tools = await host.GetToolsAsync(Ct);
        JsonElement arguments = argument is null
            ? JsonSerializer.SerializeToElement(new { })
            : JsonSerializer.SerializeToElement(new { name = argument });
        return await tools
            .Single(candidate => candidate.Name == "server__" + tool)
            .ExecuteAsync(arguments, McpTestEnvironment.Context, Ct);
    }

    private static IDisposable SetTestEnvironment(params (string Name, string? Value)[] variables)
    {
        (string Name, string? Previous)[] original = variables
            .Select(entry => (entry.Name, Environment.GetEnvironmentVariable(entry.Name)))
            .ToArray();
        foreach ((string name, string? value) in variables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        return new Restorer(original);
    }

    private sealed class Restorer((string Name, string? Previous)[] original) : IDisposable
    {
        public void Dispose()
        {
            foreach ((string name, string? previous) in original)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }
}
