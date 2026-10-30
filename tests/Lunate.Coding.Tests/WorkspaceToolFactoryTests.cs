using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.AI;

namespace Lunate.Coding.Tests;

/// <summary>
/// The composition seams: the C# offering gate (no Roslyn before a call), the child workspace-tool
/// factory and the child tool set of a different-root subagent (cs_* bound to the child root,
/// project MCP tools excluded, workspace-independent ones kept).
/// </summary>
public sealed class WorkspaceToolFactoryTests
{
    private const string Secret = "sk-ant-abcdef-123456";
    private const string Marker = "[redacted:ANTHROPIC_API_KEY]";
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public void A_root_without_solution_files_offers_no_cs_tools()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("Program.cs"), "class C;\n");
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));

        ToolRegistry tools = RoslynTestHarness.CreateTools(
            new Workspace(temp.Root),
            services: services
        );

        Assert.Equal(["read", "write", "edit", "bash"], tools.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public void A_csharp_root_offers_the_five_cs_tools_without_creating_a_backend()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n"
        );
        var created = new List<string>();
        using var services = new WorkspaceServices(
            2,
            root =>
            {
                created.Add(root);
                return new FakeWorkspaceBackend(root);
            }
        );

        ToolRegistry tools = RoslynTestHarness.CreateTools(
            new Workspace(temp.Root),
            services: services
        );

        Assert.Equal(
            [
                "read",
                "write",
                "edit",
                "bash",
                "cs_diagnostics",
                "cs_find_symbol",
                "cs_find_references",
                "cs_outline",
                "cs_rename",
            ],
            tools.Tools.Select(tool => tool.Name)
        );
        Assert.Empty(created);
    }

    [Fact]
    public void The_workspace_factory_binds_every_root_and_keeps_the_inherited_tools()
    {
        using var temp = new TempDirectory();
        string csharpRoot = temp.File("csharp");
        Directory.CreateDirectory(csharpRoot);
        File.WriteAllText(
            Path.Combine(csharpRoot, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n"
        );
        string plainRoot = temp.File("plain");
        Directory.CreateDirectory(plainRoot);
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));
        var extensionTool = new NamedProbe("ext-tool");
        var independentMcpTool = new NamedProbe("global__echo");
        Func<string, ToolRegistry> factory = RoslynTestHarness.CreateWorkspaceTools(
            services,
            [extensionTool, independentMcpTool]
        );

        ToolRegistry plain = factory(plainRoot);
        Assert.Equal(
            ["read", "write", "edit", "bash", "ext-tool", "global__echo"],
            plain.Tools.Select(tool => tool.Name)
        );

        ToolRegistry csharp = factory(csharpRoot);
        Assert.Contains("cs_diagnostics", csharp.Tools.Select(tool => tool.Name));
        Assert.Contains("ext-tool", csharp.Tools.Select(tool => tool.Name));
    }

    [Fact]
    public async Task A_child_workspace_write_refuses_a_placeholder_over_the_real_value()
    {
        using var temp = new TempDirectory();
        string childRoot = temp.File("child");
        Directory.CreateDirectory(childRoot);
        string envPath = Path.Combine(childRoot, ".env");
        string original = $"ANTHROPIC_API_KEY={Secret}\n";
        File.WriteAllText(envPath, original);
        byte[] before = SHA256.HashData(Encoding.UTF8.GetBytes(original));
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));
        var redactor = new CredentialRedactor([("ANTHROPIC_API_KEY", Secret)]);
        // The child factory inherits the write/edit guard: a child session runs with the same
        // redactor, so its write tool refuses the placeholder over the value it stands for.
        Func<string, ToolRegistry> factory = RoslynTestHarness.CreateWorkspaceTools(
            services,
            [],
            redaction: redactor
        );
        var context = new ToolContext(childRoot, new NullAgentEvents());

        ToolResult result = await factory(childRoot)
            .Find("write")!
            .ExecuteAsync(
                JsonSerializer.SerializeToElement(
                    new { path = ".env", content = $"ANTHROPIC_API_KEY={Marker}\n" }
                ),
                context,
                Ct
            );

        // The refusal names the credential (through its marker the write would destroy).
        Assert.True(result.IsError);
        Assert.Contains(Marker, result.Output, StringComparison.Ordinal);
        Assert.Equal(
            "ANTHROPIC_API_KEY",
            Assert.Single(CredentialRedactor.PlaceholderNamesIn(result.Output))
        );
        // Byte-identical, compared as a hash: the refused child write is no write at all.
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(envPath)));
    }

    [Fact]
    public async Task A_different_root_child_gets_cs_tools_but_no_project_mcp_tools()
    {
        using var temp = new TempDirectory();
        var (main, worktree) = CreateLinkedWorktree(temp, "wt");
        string canonicalMain = GitIdentity.Canonicalize(main);
        File.WriteAllText(
            Path.Combine(worktree, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>\n"
        );
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));
        var extensionTool = new NamedProbe("ext-tool");
        var projectMcpTool = new NamedProbe("project__echo");
        var independentMcpTool = new NamedProbe("global__echo");
        var spawn = new SpawnProbe(worktree);
        ToolRegistry parentTools = RoslynTestHarness.CreateTools(
            new Workspace(canonicalMain),
            services: services
        );
        parentTools.Add(extensionTool);
        parentTools.Add(projectMcpTool);
        parentTools.Add(independentMcpTool);
        parentTools.Add(spawn);
        var client = new RecordingClient()
            .Enqueue(
                Scripts.Call("call_1", "spawn", Scripts.Args(("prompt", "child prompt"))),
                Scripts.ToolCalls()
            )
            .Enqueue(Scripts.Text("child done"), Scripts.Stop())
            .Enqueue(Scripts.Text("parent done"), Scripts.Stop());
        var harness = new AgentHarness(
            client,
            parentTools,
            new AgentHarnessOptions
            {
                WorkingDirectory = canonicalMain,
                WorkspaceTools = RoslynTestHarness.CreateWorkspaceTools(
                    services,
                    [extensionTool, independentMcpTool]
                ),
            }
        );

        await harness.RunAsync("go", Ct).ToListAsync(Ct);

        Assert.Equal(3, client.Options.Count);
        IReadOnlyList<string> parentOffered = Names(client.Options[0]);
        Assert.Contains("project__echo", parentOffered);
        IReadOnlyList<string> childOffered = Names(client.Options[1]);
        Assert.Contains("read", childOffered);
        Assert.Contains("cs_diagnostics", childOffered);
        Assert.Contains("ext-tool", childOffered);
        Assert.Contains("global__echo", childOffered);
        Assert.DoesNotContain("project__echo", childOffered);
    }

    private static IReadOnlyList<string> Names(ChatOptions? options) =>
        [.. (options?.Tools ?? []).Select(tool => tool.Name ?? string.Empty)];

    /// <summary>A write-risk spawn tool that starts one child run in the given workspace root.</summary>
    private sealed class SpawnProbe(string workspaceRoot) : ITool
    {
        public string Name => "spawn";

        public string Description => "Spawns a subagent in the worktree.";

        public JsonElement ParametersSchema { get; } =
            JsonSerializer.SerializeToElement(
                new { type = "object", properties = new { prompt = new { type = "string" } } }
            );

        public ToolRisk Risk => ToolRisk.Write;

        public async Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        )
        {
            SubagentResult result = await ctx.Subagents!.RunAsync(
                new SubagentRequest("child prompt", WorkspaceRoot: workspaceRoot),
                ct
            );
            return new ToolResult(result.Output, result.IsError);
        }
    }

    private static (string Main, string Worktree) CreateLinkedWorktree(
        TempDirectory temp,
        string name
    )
    {
        var main = temp.File("main");
        var worktree = temp.File(name);
        var gitDir = Path.Combine(main, ".git", "worktrees", name);
        Directory.CreateDirectory(gitDir);
        Directory.CreateDirectory(worktree);
        File.WriteAllText(
            Path.Combine(worktree, ".git"),
            $"gitdir: ../main/.git/worktrees/{name}\n"
        );
        File.WriteAllText(Path.Combine(gitDir, "commondir"), "../..\n");
        return (main, worktree);
    }

    /// <summary>A write-risk tool with a fixed name, for composition assertions.</summary>
    private sealed class NamedProbe(string name) : ITool
    {
        public string Name { get; } = name;

        public string Description => "A composition probe tool.";

        public JsonElement ParametersSchema { get; } =
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { } });

        public Task<ToolResult> ExecuteAsync(
            JsonElement args,
            ToolContext ctx,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("probe ran", IsError: false));
    }

    /// <summary>A scripted client that records the offered tools of every model call.</summary>
    private sealed class RecordingClient : IChatClient
    {
        private readonly Queue<ChatResponseUpdate[]> _scripts = new();

        public List<ChatOptions?> Options { get; } = [];

        public RecordingClient Enqueue(params ChatResponseUpdate[] updates)
        {
            _scripts.Enqueue(updates);
            return this;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "summary of older turns"))
            );

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            Options.Add(options);
            foreach (ChatResponseUpdate update in _scripts.Dequeue())
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
