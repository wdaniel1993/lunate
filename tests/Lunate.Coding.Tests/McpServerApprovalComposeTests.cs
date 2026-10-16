using Lunate.Extensibility;
using Lunate.Protocols;

namespace Lunate.Coding.Tests;

/// <summary>
/// The per-server approval at compose time: a project definition prompts once per repository and
/// definition hash, a changed definition re-prompts, another repository with the same definition
/// still asks, and a refused server is skipped with the approval hint while the rest continues.
/// </summary>
public sealed class McpServerApprovalComposeTests
{
    [Fact]
    public async Task An_unapproved_project_server_prompts_once_and_is_recorded_for_the_repository()
    {
        using var temp = new TempDirectory();
        string worktree = NewWorktree(temp);
        string config = WriteProjectConfig(worktree, ("srv", "run-one"));
        string store = temp.File("store");
        TrustFile(store, worktree, config);
        var prompt = new ScriptedApprovalPrompt(true);

        McpToolSet first = (await Compose(worktree, store, prompt))!;

        Assert.Equal("srv", Assert.Single(first.Statuses).Name);
        Assert.Equal(["srv"], prompt.Asked);
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            Store(store).EvaluateMcpServer(worktree, DefinitionHash(config))
        );

        McpToolSet second = (await Compose(worktree, store, prompt))!;

        Assert.Single(second.Statuses);
        Assert.Single(prompt.Asked);
    }

    [Fact]
    public async Task A_changed_definition_prompts_again()
    {
        using var temp = new TempDirectory();
        string worktree = NewWorktree(temp);
        string config = WriteProjectConfig(worktree, ("srv", "run-one"));
        string store = temp.File("store");
        TrustFile(store, worktree, config);
        var prompt = new ScriptedApprovalPrompt(true, true);

        await Compose(worktree, store, prompt);

        WriteProjectConfig(worktree, ("srv", "run-two"));
        TrustFile(store, worktree, config);
        await Compose(worktree, store, prompt);

        Assert.Equal(2, prompt.Asked.Count);
    }

    [Fact]
    public async Task Another_repository_with_the_same_definition_still_asks()
    {
        using var temp = new TempDirectory();
        string first = NewWorktree(temp, "first");
        string second = NewWorktree(temp, "second");
        string store = temp.File("store");
        string firstConfig = WriteProjectConfig(first, ("srv", "run-one"));
        string secondConfig = WriteProjectConfig(second, ("srv", "run-one"));
        TrustFile(store, first, firstConfig);
        TrustFile(store, second, secondConfig);
        var prompt = new ScriptedApprovalPrompt(true, true);

        await Compose(first, store, prompt);
        await Compose(second, store, prompt);

        Assert.Equal(2, prompt.Asked.Count);
        Assert.Equal(
            ExtensionTrustDecision.Trusted,
            Store(store).EvaluateMcpServer(second, DefinitionHash(secondConfig))
        );
    }

    [Fact]
    public async Task A_refused_server_is_skipped_with_the_hint_and_the_rest_continues()
    {
        using var temp = new TempDirectory();
        string worktree = NewWorktree(temp);
        string config = WriteProjectConfig(worktree, ("good", "run-good"), ("bad", "run-bad"));
        string store = temp.File("store");
        TrustFile(store, worktree, config);
        var log = new RecordingExtensionLog();
        var prompt = new ScriptedApprovalPrompt(true, false);

        McpToolSet set = (await Compose(worktree, store, prompt, log))!;

        Assert.Equal(["good"], set.Statuses.Select(status => status.Name));
        Assert.True(
            log.Contains(
                $"server 'bad' from project MCP config '{config}' is not approved; it was not started."
            )
                && log.Contains("interactive")
                && log.Contains("'lunate trust'"),
            "the refusal hint must name the server and both approval paths; log:\n"
                + string.Join("\n", log.Messages)
        );
        Assert.Equal(
            ExtensionTrustDecision.Prompt,
            Store(store).EvaluateMcpServer(worktree, DefinitionHash(config, "bad"))
        );
    }

    [Fact]
    public async Task A_refused_project_server_leaves_a_same_named_user_definition_running()
    {
        using var temp = new TempDirectory();
        string worktree = NewWorktree(temp);
        string lunate = temp.File("user-lunate-mcp.json");
        File.WriteAllText(lunate, """{ "mcpServers": { "srv": { "command": "user-run" } } }""");
        string config = WriteProjectConfig(worktree, ("srv", "project-run"));
        string store = temp.File("store");
        TrustFile(store, worktree, config);
        var prompt = new ScriptedApprovalPrompt(false);

        McpToolSet set = (await Compose(worktree, store, prompt, lunatePath: lunate))!;

        McpServerStatus status = Assert.Single(set.Statuses);
        Assert.Equal("srv", status.Name);
        Assert.Equal(lunate, status.Source);
    }

    private static async Task<McpToolSet?> Compose(
        string worktree,
        string store,
        ScriptedApprovalPrompt prompt,
        RecordingExtensionLog? log = null,
        string? lunatePath = null
    )
    {
        log ??= new RecordingExtensionLog();
        return await McpComposition.ComposeAsync(
            new Workspace(worktree),
            lunatePath ?? Path.Combine(worktree, "no-lunate-mcp.json"),
            Path.Combine(worktree, "no-agents-mcp.json"),
            new ExtensionSeams { StorePath = store, Log = log },
            new ThrowingFilePrompt(),
            prompt,
            log,
            _ => { },
            TestContext.Current.CancellationToken
        );
    }

    private static string NewWorktree(TempDirectory temp, string name = "worktree")
    {
        string worktree = temp.File(name);
        Directory.CreateDirectory(worktree);
        return Workspace.Canonicalize(worktree);
    }

    private static string WriteProjectConfig(
        string worktree,
        params (string Name, string Command)[] servers
    )
    {
        string path = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string entries = string.Join(
            ", ",
            servers.Select(server => $"\"{server.Name}\": {{ \"command\": \"{server.Command}\" }}")
        );
        File.WriteAllText(path, $$"""{ "mcpServers": { {{entries}} } }""");
        return path;
    }

    private static void TrustFile(string storePath, string worktree, string configPath) =>
        Store(storePath)
            .RecordMcpConfigTrusted(
                worktree,
                worktree,
                McpConfigTrustHash.ComputeFile(configPath),
                DateTimeOffset.UtcNow
            );

    private static string DefinitionHash(string configPath, string name = "srv") =>
        McpConfigTrustHash.ComputeDefinitions(configPath)[name];

    private static ExtensionTrustStore Store(string storePath) =>
        new(new ExtensionHostOptions { StorePath = storePath });

    private sealed class ScriptedApprovalPrompt(params bool[] decisions) : IMcpServerApprovalPrompt
    {
        private readonly Queue<bool> _decisions = new(decisions);

        public List<string> Asked { get; } = [];

        public ValueTask<bool> ApproveAsync(
            McpServerOptions server,
            CancellationToken cancellationToken
        )
        {
            Asked.Add(server.Name);
            if (!_decisions.TryDequeue(out bool decision))
            {
                throw new InvalidOperationException(
                    $"ScriptedApprovalPrompt has no decision left for '{server.Name}' (ask {Asked.Count})."
                );
            }

            return ValueTask.FromResult(decision);
        }
    }

    private sealed class ThrowingFilePrompt : IMcpConfigTrustPrompt
    {
        public ValueTask<bool> ApproveAsync(
            string configPath,
            IReadOnlyList<McpServerOptions> servers,
            CancellationToken cancellationToken
        ) =>
            throw new InvalidOperationException("the file trust must be recorded before composing");
    }
}
