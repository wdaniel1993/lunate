using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;
using Lunate.Roslyn.BuiltIn;

namespace Lunate.Coding.Tests;

[Collection("roslyn-workspaces")]
public sealed class RoslynBuiltInWorkspaceServiceTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("cs_diagnostics", "{\"scope\":\"solution\"}", "diagnostics")]
    [InlineData("cs_find_symbol", "{\"name\":\"Widget\"}", "find-symbol")]
    [InlineData("cs_find_references", "{\"name\":\"Widget\"}", "find-references")]
    [InlineData("cs_outline", "{\"file\":\"Source.cs\"}", "outline")]
    [InlineData("cs_rename", "{\"name\":\"Widget\",\"newName\":\"NewWidget\"}", "rename")]
    public async Task Each_handler_acquires_a_backend_for_the_call_working_directory(
        string toolName,
        string arguments,
        string backendOperation
    )
    {
        using var temp = new TempDirectory();
        Workspace composedWorkspace = new(CreateWorkspace(temp, "composed"));
        string callRoot = CreateWorkspace(temp, "call");
        Dictionary<string, TrackingBackend> backends = new(StringComparer.Ordinal);
        BuiltInExtension builtIn = RoslynBuiltIn(root =>
        {
            var backend = new TrackingBackend();
            backends.Add(root, backend);
            return backend;
        });
        await using ExtensionSession host = await StartHost(temp, composedWorkspace, builtIn);
        ITool tool = host.ComposeTools(composedWorkspace).Single(item => item.Name == toolName);

        ToolResult result = await tool.ExecuteAsync(
            Parse(arguments),
            new ToolContext(callRoot, new NullAgentEvents()),
            Ct
        );

        Assert.False(result.IsError, result.Output);
        string canonicalCallRoot = GitIdentity.Canonicalize(callRoot);
        TrackingBackend backend = Assert.Single(backends).Value;
        Assert.Equal(canonicalCallRoot, Assert.Single(backends.Keys));
        Assert.Contains(backendOperation, backend.Operations);
    }

    [Fact]
    public async Task Completed_calls_release_leases_and_the_builtin_cap_is_two_per_root()
    {
        using var temp = new TempDirectory();
        Workspace composedWorkspace = new(CreateWorkspace(temp, "composed"));
        string[] roots =
        [
            CreateWorkspace(temp, "first"),
            CreateWorkspace(temp, "second"),
            CreateWorkspace(temp, "third"),
        ];
        Dictionary<string, TrackingBackend> backends = new(StringComparer.Ordinal);
        BuiltInExtension builtIn = RoslynBuiltIn(root =>
        {
            var backend = new TrackingBackend();
            backends.Add(root, backend);
            return backend;
        });
        await using ExtensionSession host = await StartHost(temp, composedWorkspace, builtIn);
        ITool outline = host.ComposeTools(composedWorkspace)
            .Single(tool => tool.Name == "cs_outline");

        foreach (string root in roots)
        {
            ToolResult result = await outline.ExecuteAsync(
                Parse("{\"file\":\"Source.cs\"}"),
                new ToolContext(root, new NullAgentEvents()),
                Ct
            );
            Assert.False(result.IsError, result.Output);
        }

        TrackingBackend first = backends[GitIdentity.Canonicalize(roots[0])];
        TrackingBackend second = backends[GitIdentity.Canonicalize(roots[1])];
        TrackingBackend third = backends[GitIdentity.Canonicalize(roots[2])];
        Assert.NotSame(first, second);
        Assert.NotSame(second, third);
        Assert.Contains("outline", first.Operations);
        Assert.Contains("outline", second.Operations);
        Assert.Contains("outline", third.Operations);
        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        Assert.False(third.Disposed);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 2)]
    public async Task Roslyn_setting_is_a_host_ceiling_not_a_manifest_cap_increase(
        int configuredCap,
        int effectiveCap
    )
    {
        using var temp = new TempDirectory();
        Workspace composedWorkspace = new(CreateWorkspace(temp, "composed"));
        string[] roots = Enumerable
            .Range(0, effectiveCap + 1)
            .Select(index =>
                CreateWorkspace(temp, "root-" + index.ToString(CultureInfo.InvariantCulture))
            )
            .ToArray();
        string settingsPath = temp.File("settings.json");
        File.WriteAllText(
            settingsPath,
            JsonSerializer.Serialize(
                new { schemaVersion = 1, roslyn = new { maxLoadedWorkspaces = configuredCap } }
            )
        );
        AgentSettings settings = SettingsStore.Resolve(settingsPath, _ => null);
        Dictionary<string, TrackingBackend> backends = new(StringComparer.Ordinal);
        BuiltInExtension builtIn = RoslynBuiltIn(root =>
        {
            var backend = new TrackingBackend();
            backends.Add(root, backend);
            return backend;
        });
        await using ExtensionSession host = await StartHost(
            temp,
            composedWorkspace,
            builtIn,
            settings.Roslyn.MaxLoadedWorkspaces
        );
        ITool outline = host.ComposeTools(composedWorkspace)
            .Single(tool => tool.Name == "cs_outline");

        foreach (string root in roots)
        {
            ToolResult result = await outline.ExecuteAsync(
                Parse("{\"file\":\"Source.cs\"}"),
                new ToolContext(root, new NullAgentEvents()),
                Ct
            );
            Assert.False(result.IsError, result.Output);
        }

        Assert.Equal(effectiveCap + 1, backends.Count);
        Assert.Equal(effectiveCap, backends.Values.Count(backend => !backend.Disposed));
        Assert.True(backends[GitIdentity.Canonicalize(roots[0])].Disposed);
    }

    private static string CreateWorkspace(TempDirectory temp, string name)
    {
        string root = temp.File(name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project />");
        return root;
    }

    private static BuiltInExtension RoslynBuiltIn(Func<string, TrackingBackend> createBackend) =>
        new(
            "host/roslyn/extension.json",
            RoslynBuiltInFactory.Manifest,
            () => new RoslynBuiltInFactory(root => createBackend(root))
        );

    private static Task<ExtensionSession> StartHost(
        TempDirectory temp,
        Workspace workspace,
        BuiltInExtension builtIn,
        int? capLimit = null
    ) =>
        ExtensionSession.StartAsync(
            workspace.Identity,
            new ExtensionSeams
            {
                StorePath = temp.File("store"),
                BuiltIns = [builtIn],
                WorkspaceServiceCapLimit = capLimit,
                Log = new RecordingExtensionLog(),
            },
            Ct
        );

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed class TrackingBackend : ICSharpBackend, IDisposable
    {
        public List<string> Operations { get; } = [];

        public bool Disposed { get; private set; }

        public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct)
        {
            Operations.Add("load");
            return ValueTask.FromResult(
                new WorkspaceLoadResult(WorkspaceStatus.Loaded, "loaded", null, 0, 0, [])
            );
        }

        public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
            DiagnosticsScope scope,
            CancellationToken ct
        )
        {
            Operations.Add("diagnostics");
            return ValueTask.FromResult(new DiagnosticsResult([], 0, 0, false, "the solution"));
        }

        public ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct)
        {
            Operations.Add("find-symbol");
            return ValueTask.FromResult(
                new SymbolSearchResult(SymbolSearchStatus.Loaded, "searched", [], 0)
            );
        }

        public ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct)
        {
            Operations.Add("find-references");
            return ValueTask.FromResult(
                new ReferencesResult(SymbolSearchStatus.Loaded, "searched", null, [], 0)
            );
        }

        public ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct)
        {
            Operations.Add("outline");
            return ValueTask.FromResult(new OutlineResult("outlined", [], 0));
        }

        public ValueTask<RenamePlanResult> PlanRenameAsync(
            string name,
            string newName,
            CancellationToken ct
        )
        {
            Operations.Add("rename");
            return ValueTask.FromResult(
                new RenamePlanResult(SymbolSearchStatus.Loaded, "nothing was changed", [], 0, 0)
            );
        }

        public void NotifyFileChanged(string absolutePath) { }

        public void NotifyProjectChanged() { }

        public void Dispose() => Disposed = true;
    }

    private sealed class NullAgentEvents : IAgentEvents
    {
        public void Emit(AgentEvent agentEvent) { }
    }
}
