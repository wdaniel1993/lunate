using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Protocols;
using Lunate.TestSupport;

namespace Lunate.Coding.Tests;

/// <summary>
/// #449/#455 through the real composition: the values <see cref="CredentialRedactorSource.Collect"/>
/// knows reach <see cref="McpComposition.ComposeAsync"/>, which passes them to every configured
/// server host, so the spawned TestMcpServer child must not see a known credential value unless
/// its own declared <c>env</c> names the variable. The fixture variables are set on the real
/// process environment and restored afterwards.
/// </summary>
[Collection("mcp-processes")]
public sealed class McpCompositionCredentialTests
{
    private const string AnthropicVariable = "ANTHROPIC_API_KEY";
    private const string ProviderVariable = "MY_PROVIDER_TOKEN";
    private const string ParentFlagVariable = "LUNATE_TEST_PARENT_FLAG";

    private static readonly string AnthropKeyValue = "sk-ant-fixture-9876543210";
    private static readonly string ProviderKeyValue = "sk-provider-fixture-0123456789";
    private static readonly string ParentFlagValue = "parent-flag-visible";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Known_credential_values_do_not_reach_the_composed_child()
    {
        using var temp = new TempDirectory();
        IDisposable restore = SetTestEnvironment(
            (AnthropicVariable, AnthropKeyValue),
            (ProviderVariable, ProviderKeyValue),
            (ParentFlagVariable, ParentFlagValue)
        );
        try
        {
            string worktree = NewWorktree(temp);
            string marker = temp.File("marker.txt");
            string config = temp.File("user-mcp.json");
            string agents = temp.File("user-agents-mcp.json");
            WriteConfig(config, marker);
            File.WriteAllText(agents, "{}");
            CredentialRedactor redaction = CollectRedaction(temp, config);

            await using McpToolSet set = Compose(
                worktree,
                config,
                agents,
                temp,
                redaction: WithheldValuesOf(redaction)
            )!;

            string[] names = await NamesAsync(set);

            Assert.DoesNotContain(AnthropicVariable, names);
            Assert.DoesNotContain(ProviderVariable, names);
            Assert.Contains(ParentFlagVariable, names);
            Assert.Equal(ParentFlagValue, await ValueAsync(set, ParentFlagVariable));
            Assert.NotEqual("(unset)", await ValueAsync(set, "PATH"));
        }
        finally
        {
            restore.Dispose();
        }
    }

    [Fact]
    public async Task A_declared_variable_still_receives_its_value_through_the_composition()
    {
        using var temp = new TempDirectory();
        IDisposable restore = SetTestEnvironment(
            (AnthropicVariable, AnthropKeyValue),
            (ProviderVariable, ProviderKeyValue)
        );
        try
        {
            string worktree = NewWorktree(temp);
            string marker = temp.File("marker.txt");
            string config = temp.File("user-mcp.json");
            string agents = temp.File("user-agents-mcp.json");
            WriteConfig(
                config,
                marker,
                declared: new Dictionary<string, string>
                {
                    ["ANTHROPIC_API_KEY"] = "${ANTHROPIC_API_KEY}",
                }
            );
            File.WriteAllText(agents, "{}");
            CredentialRedactor redaction = CollectRedaction(temp, config);

            await using McpToolSet set = Compose(
                worktree,
                config,
                agents,
                temp,
                redaction: WithheldValuesOf(redaction)
            )!;

            // ${ANTHROPIC_API_KEY} expanded at config load, so the declared opt-in receives the
            // real value; the name the server did not declare stays withheld.
            Assert.Equal(AnthropKeyValue, await ValueAsync(set, AnthropicVariable));
            string[] names = await NamesAsync(set);
            Assert.DoesNotContain(ProviderVariable, names);
        }
        finally
        {
            restore.Dispose();
        }
    }

    [Fact]
    public async Task Without_a_withheld_set_the_composed_child_still_inherits_everything()
    {
        using var temp = new TempDirectory();
        IDisposable restore = SetTestEnvironment(
            (AnthropicVariable, AnthropKeyValue),
            (ParentFlagVariable, ParentFlagValue)
        );
        try
        {
            string worktree = NewWorktree(temp);
            string marker = temp.File("marker.txt");
            string config = temp.File("user-mcp.json");
            string agents = temp.File("user-agents-mcp.json");
            WriteConfig(config, marker);
            File.WriteAllText(agents, "{}");

            await using McpToolSet set = Compose(worktree, config, agents, temp, redaction: null)!;

            // The rule only tightens: a compose that passes no withheld values (even a caller
            // whose redactor was never built) keeps today's full inheritance.
            Assert.Contains(AnthropicVariable, await NamesAsync(set));
        }
        finally
        {
            restore.Dispose();
        }
    }

    private static string[] WithheldValuesOf(CredentialRedactor redaction) =>
        [.. redaction.Values.Select(pair => pair.Value)];

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

    private static CredentialRedactor CollectRedaction(TempDirectory temp, string configPath)
    {
        string auth = temp.File("auth.json");
        string store = temp.File("provider-store.json");
        // The preset path brings ANTHROPIC_API_KEY out of the real process environment; the
        // provider store's env: reference brings MY_PROVIDER_TOKEN. Both are session-known.
        File.WriteAllText(
            store,
            """{ "providers": { "custom": { "apiKey": "env:MY_PROVIDER_TOKEN" } } }"""
        );
        return CredentialRedactorSource.Collect(
            AuthStore.Load(auth),
            Environment.GetEnvironmentVariable,
            temp.Root,
            [configPath],
            store
        );
    }

    private static McpToolSet? Compose(
        string worktree,
        string configPath,
        string agentsPath,
        TempDirectory temp,
        IReadOnlyList<string>? redaction
    )
    {
        var log = new RecordingExtensionLog();
        return McpComposition
            .ComposeAsync(
                new Workspace(worktree),
                configPath,
                agentsPath,
                new ExtensionSeams { StorePath = temp.File("store") },
                new RefusingFilePrompt(),
                new RefusingServerPrompt(),
                log,
                _ => { },
                TestContext.Current.CancellationToken,
                withheldCredentialValues: redaction
            )
            .GetAwaiter()
            .GetResult();
    }

    private static void WriteConfig(
        string path,
        string marker,
        IReadOnlyDictionary<string, string>? declared = null
    )
    {
        var environment = new JsonObject
        {
            ["LUNATE_TEST_MCP_MARKER"] = marker,
            ["LUNATE_TEST_MCP_ENV_TOOL"] = "1",
        };
        foreach ((string name, string value) in declared ?? new Dictionary<string, string>())
        {
            environment[name] = value;
        }

        var document = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["server"] = new JsonObject
                {
                    ["command"] = TestMcpServer.DotnetPath,
                    ["args"] = new JsonArray("exec", TestMcpServer.ServerDll),
                    ["env"] = environment,
                },
            },
        };
        File.WriteAllText(path, document.ToJsonString());
    }

    private static string NewWorktree(TempDirectory temp)
    {
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        return Workspace.Canonicalize(worktree);
    }

    private static async Task<string[]> NamesAsync(McpToolSet set)
    {
        ToolResult result = await ExecuteAsync(set, "env_names");
        Assert.False(result.IsError);
        return JsonSerializer.Deserialize<JsonElement>(result.Output).Deserialize<string[]>() ?? [];
    }

    private static async Task<string> ValueAsync(McpToolSet set, string name) =>
        (await ExecuteAsync(set, "env_value", name)).Output;

    private static async Task<ToolResult> ExecuteAsync(
        McpToolSet set,
        string tool,
        string? argument = null
    )
    {
        IReadOnlyList<ITool> tools = await set.ListToolsAsync(Ct);
        JsonElement arguments = argument is null
            ? JsonSerializer.SerializeToElement(new { })
            : JsonSerializer.SerializeToElement(new { name = argument });
        return await tools
            .Single(candidate => candidate.Name == "server__" + tool)
            .ExecuteAsync(arguments, new ToolContext("run", new NullAgentEvents()), Ct);
    }

    private sealed class RefusingFilePrompt : IMcpConfigTrustPrompt
    {
        public ValueTask<bool> ApproveAsync(
            string configPath,
            IReadOnlyList<McpServerOptions> servers,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(false);
    }

    private sealed class RefusingServerPrompt : IMcpServerApprovalPrompt
    {
        public ValueTask<bool> ApproveAsync(
            McpServerOptions server,
            CancellationToken cancellationToken
        ) => ValueTask.FromResult(false);
    }
}
