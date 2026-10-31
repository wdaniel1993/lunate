using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Protocols.Acp;

namespace Lunate.Coding.Tests;

/// <summary>
/// ACP startup: a composition failure ends the process with exit code 1 instead of serving frames
/// from a half-built host, and the per-session harness carries the extension host's model registry
/// so an editor session resolves the models the loaded extensions provide.
/// </summary>
public sealed class AcpStartupFailureTests
{
    private const string WorktreeVariable = "LUNATE_TEST_ACP_WORKTREE";

    /// <summary>
    /// The state root inside the workspace is exactly the guard a swallowed composition failure
    /// hid: the session cannot be built, so the process must say so and exit rather than answer an
    /// editor with an agent that has no seam. <c>LUNATE_HOME</c> is process-wide, so the half that
    /// sets it runs in its own process.
    /// </summary>
    [Fact]
    public async Task A_startup_composition_failure_exits_1_instead_of_serving()
    {
        const string Method = nameof(A_startup_composition_failure_exits_1_instead_of_serving);
        if (TestChildProcess.IsChild(Method))
        {
            string childWorktree = Environment.GetEnvironmentVariable(WorktreeVariable)!;
            Environment.CurrentDirectory = childWorktree;
            using var childInput = new MemoryStream();
            using var childOutput = new MemoryStream();
            using var childErrors = new StringWriter();

            int childExit = AcpMode.Run(
                childErrors,
                childInput,
                childOutput,
                CancellationToken.None
            );

            // The protocol stream carries nothing: no session was served.
            Assert.Empty(childOutput.ToArray());
            Assert.Contains(
                "the session composition failed",
                childErrors.ToString(),
                StringComparison.Ordinal
            );
            Assert.Contains(
                "LUNATE_HOME points inside the workspace",
                childErrors.ToString(),
                StringComparison.Ordinal
            );
            Assert.Equal(1, childExit);
            return;
        }

        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);

        (int exitCode, string output) = await TestChildProcess.RunAsync(
            typeof(AcpStartupFailureTests),
            Method,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["LUNATE_HOME"] = Path.Combine(worktree, "state"),
                [WorktreeVariable] = worktree,
            }
        );

        Assert.True(exitCode == 0, $"the ACP startup check failed (exit {exitCode}):{output}");
    }

    [Fact]
    public async Task The_session_harness_carries_the_extension_hosts_model_registry()
    {
        using var temp = new TempDirectory();
        string worktree = temp.File("worktree");
        Directory.CreateDirectory(worktree);
        var workspace = new Workspace(worktree);
        await using ExtensionSession host = await ExtensionSession.StartAsync(
            workspace.Identity,
            new ExtensionSeams
            {
                StorePath = temp.File("store"),
                Log = new RecordingExtensionLog(),
            },
            TestContext.Current.CancellationToken
        );

        // The harness resolves a model before it is built; a test must not borrow the machine's
        // settings or catalog, or it passes where a developer has a model configured and fails on
        // a clean runner.
        string catalogPath = temp.File("models.json");
        File.WriteAllText(
            catalogPath,
            """{"schemaVersion":1,"models":[{"id":"acp-test","provider":"openai","contextWindow":1000,"supportsTools":true}]}"""
        );
        AgentHarness harness = AcpMode.CreateHarness(
            new AcpSessionContext(worktree, null, null),
            host,
            [],
            [],
            launch: null,
            factory: new FakeChatClientFactory(new FakeChatClient()),
            settings: new AgentSettings { Model = "acp-test" },
            catalog: Lunate.Ai.ModelCatalog.Load(catalogPath)
        );

        // Without this the session resolves only catalog models: anything a loaded extension
        // provides (a provider, a virtual model) is invisible to an editor session.
        Assert.Same(host.ModelRegistry, harness.Options.ModelRegistry);
    }

    private sealed class FakeChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new Microsoft.Extensions.AI.ChatResponse(
                    new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        "ok"
                    )
                )
            );

        public async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield return new Microsoft.Extensions.AI.ChatResponseUpdate(
                Microsoft.Extensions.AI.ChatRole.Assistant,
                "ok"
            );
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
