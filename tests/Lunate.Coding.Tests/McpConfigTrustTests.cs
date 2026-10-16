using Lunate.Extensibility;
using Lunate.Protocols;

namespace Lunate.Coding.Tests;

public sealed class McpConfigTrustTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_untrusted_config_prompts_once_and_is_remembered()
    {
        using var temp = new TempDirectory();
        string worktree = Worktree(temp);
        string config = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, """{ "mcpServers": { "srv": { "command": "x" } } }""");
        var trust = new McpConfigTrust(Store(temp));
        var prompt = new ScriptedMcpConfigTrustPrompt(true, false);
        var log = new RecordingExtensionLog();

        Assert.True(await trust.EnsureTrustedAsync(worktree, config, [], prompt, log, Ct));
        Assert.Equal(1, prompt.Calls);
        Assert.True(await trust.EnsureTrustedAsync(worktree, config, [], prompt, log, Ct));
        Assert.Equal(1, prompt.Calls);
        Assert.Empty(log.Messages);
    }

    [Fact]
    public async Task A_changed_file_prompts_again()
    {
        using var temp = new TempDirectory();
        string worktree = Worktree(temp);
        string config = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, """{ "mcpServers": { "srv": { "command": "x" } } }""");
        var trust = new McpConfigTrust(Store(temp));
        var prompt = new ScriptedMcpConfigTrustPrompt(true, true);
        var log = new RecordingExtensionLog();

        Assert.True(await trust.EnsureTrustedAsync(worktree, config, [], prompt, log, Ct));
        File.WriteAllText(config, """{ "mcpServers": { "srv": { "command": "y" } } }""");
        Assert.True(await trust.EnsureTrustedAsync(worktree, config, [], prompt, log, Ct));

        Assert.Equal(2, prompt.Calls);
    }

    [Fact]
    public async Task A_formatting_only_edit_does_not_prompt_again()
    {
        using var temp = new TempDirectory();
        string worktree = Worktree(temp);
        string config = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, """{ "mcpServers": { "srv": { "command": "x" } } }""");
        var trust = new McpConfigTrust(Store(temp));
        var prompt = new ScriptedMcpConfigTrustPrompt(true);
        var log = new RecordingExtensionLog();

        Assert.True(await trust.EnsureTrustedAsync(worktree, config, [], prompt, log, Ct));
        File.WriteAllText(
            config,
            """

            {
              "mcpServers": { "srv": { "command": "x" } }
            }
            """
        );
        Assert.True(await trust.EnsureTrustedAsync(worktree, config, [], prompt, log, Ct));

        Assert.Equal(1, prompt.Calls);
    }

    [Fact]
    public async Task A_new_worktree_of_a_trusted_repository_does_not_prompt()
    {
        using var temp = new TempDirectory();
        string first = Worktree(temp, "one");
        string second = Worktree(temp, "two");
        var trust = new McpConfigTrust(Store(temp));
        var prompt = new ScriptedMcpConfigTrustPrompt(true, false);
        var log = new RecordingExtensionLog();

        Assert.True(await trust.EnsureTrustedAsync("repo-1", Config(first), [], prompt, log, Ct));
        Assert.True(await trust.EnsureTrustedAsync("repo-1", Config(second), [], prompt, log, Ct));

        Assert.Equal(1, prompt.Calls);
    }

    [Fact]
    public async Task A_denial_refuses_the_file_and_logs_how_to_approve()
    {
        using var temp = new TempDirectory();
        string worktree = Worktree(temp);
        string config = McpComposition.ProjectConfigPath(worktree);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        File.WriteAllText(config, """{ "mcpServers": { "srv": { "command": "x" } } }""");
        var trust = new McpConfigTrust(Store(temp));
        var prompt = new ScriptedMcpConfigTrustPrompt(false);
        var log = new RecordingExtensionLog();

        Assert.False(await trust.EnsureTrustedAsync(worktree, config, [], prompt, log, Ct));

        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("was not trusted", StringComparison.Ordinal)
                && message.Contains("interactive", StringComparison.Ordinal)
                && message.Contains("'lunate trust'", StringComparison.Ordinal)
        );
    }

    private static string Worktree(TempDirectory temp, string name = "worktree")
    {
        string path = temp.File(name);
        Directory.CreateDirectory(path);
        return Workspace.Canonicalize(path);
    }

    private static string Config(string worktree) => McpComposition.ProjectConfigPath(worktree);

    private static ExtensionTrustStore Store(TempDirectory temp) =>
        new(new ExtensionHostOptions { StorePath = temp.File("store") });

    private sealed class ScriptedMcpConfigTrustPrompt(params bool[] decisions)
        : IMcpConfigTrustPrompt
    {
        private readonly Queue<bool> _decisions = new(decisions);

        public int Calls { get; private set; }

        public ValueTask<bool> ApproveAsync(
            string configPath,
            IReadOnlyList<McpServerOptions> servers,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            if (!_decisions.TryDequeue(out bool decision))
            {
                throw new InvalidOperationException(
                    $"ScriptedMcpConfigTrustPrompt has no decision left (call {Calls})."
                );
            }

            return ValueTask.FromResult(decision);
        }
    }
}
