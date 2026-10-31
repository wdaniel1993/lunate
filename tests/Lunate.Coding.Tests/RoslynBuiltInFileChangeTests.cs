using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;
using Lunate.Roslyn.BuiltIn;

namespace Lunate.Coding.Tests;

[Collection("roslyn-workspaces")]
public sealed class RoslynBuiltInFileChangeTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task File_change_bus_marks_a_created_builtin_backend_for_a_csharp_edit()
    {
        using var temp = new TempDirectory();
        Workspace workspace = new(CreateWorkspace(temp, "workspace"));
        TrackingBackend? backend = null;
        await using ExtensionSession host = await StartHost(
            temp,
            workspace,
            RoslynBuiltIn(_ => backend = new TrackingBackend())
        );
        ITool diagnostics = host.ComposeTools(workspace)
            .Single(tool => tool.Name == "cs_diagnostics");
        ToolResult result = await RunDiagnosticsAsync(diagnostics, workspace);
        Assert.False(result.IsError, result.Output);

        string changedPath = Path.Combine(workspace.WorktreeRoot, "Source.cs");
        host.FileChanges.Notify(
            new FileChangeEvent(
                changedPath,
                "Source.cs",
                workspace.WorktreeRoot,
                null,
                FileChangeKind.Modified
            )
        );
        await host.FileChanges.FlushAsync(Ct);

        Assert.Equal([changedPath], Assert.IsType<TrackingBackend>(backend).ChangedFiles);
    }

    [Fact]
    public async Task File_change_bus_does_not_create_a_builtin_backend_for_an_unloaded_root()
    {
        using var temp = new TempDirectory();
        Workspace workspace = new(CreateWorkspace(temp, "workspace"));
        TrackingBackend? backend = null;
        await using ExtensionSession host = await StartHost(
            temp,
            workspace,
            RoslynBuiltIn(_ => backend = new TrackingBackend())
        );

        string changedPath = Path.Combine(workspace.WorktreeRoot, "Source.cs");
        host.FileChanges.Notify(
            new FileChangeEvent(
                changedPath,
                "Source.cs",
                workspace.WorktreeRoot,
                null,
                FileChangeKind.Modified
            )
        );
        await host.FileChanges.FlushAsync(Ct);

        Assert.Null(backend);
    }

    [Fact]
    public async Task File_change_event_routes_only_to_its_matching_live_backend()
    {
        using var temp = new TempDirectory();
        Workspace composedWorkspace = new(CreateWorkspace(temp, "composed"));
        Workspace firstWorkspace = new(CreateWorkspace(temp, "first"));
        Workspace secondWorkspace = new(CreateWorkspace(temp, "second"));
        Dictionary<string, TrackingBackend> backends = new(StringComparer.Ordinal);
        await using ExtensionSession host = await StartHost(
            temp,
            composedWorkspace,
            RoslynBuiltIn(root => backends[root] = new TrackingBackend()),
            capLimit: 2
        );
        ITool diagnostics = host.ComposeTools(composedWorkspace)
            .Single(tool => tool.Name == "cs_diagnostics");
        Assert.False((await RunDiagnosticsAsync(diagnostics, firstWorkspace)).IsError);
        Assert.False((await RunDiagnosticsAsync(diagnostics, secondWorkspace)).IsError);

        string changedPath = Path.Combine(firstWorkspace.WorktreeRoot, "Source.cs");
        host.FileChanges.Notify(
            new FileChangeEvent(
                changedPath,
                "Source.cs",
                firstWorkspace.WorktreeRoot,
                null,
                FileChangeKind.Modified
            )
        );
        await host.FileChanges.FlushAsync(Ct);

        Assert.Equal([changedPath], backends[firstWorkspace.WorktreeRoot].ChangedFiles);
        Assert.Empty(backends[secondWorkspace.WorktreeRoot].ChangedFiles);
    }

    [Theory]
    [InlineData("App.csproj")]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("Directory.Packages.props")]
    [InlineData("global.json")]
    public async Task Build_configuration_change_requests_a_builtin_reload(string fileName)
    {
        using var temp = new TempDirectory();
        Workspace workspace = new(CreateWorkspace(temp, "workspace"));
        TrackingBackend? backend = null;
        await using ExtensionSession host = await StartHost(
            temp,
            workspace,
            RoslynBuiltIn(_ => backend = new TrackingBackend())
        );
        ITool diagnostics = host.ComposeTools(workspace)
            .Single(tool => tool.Name == "cs_diagnostics");
        ToolResult result = await RunDiagnosticsAsync(diagnostics, workspace);
        Assert.False(result.IsError, result.Output);

        string changedPath = Path.Combine(workspace.WorktreeRoot, fileName);
        host.FileChanges.Notify(
            new FileChangeEvent(
                changedPath,
                fileName,
                workspace.WorktreeRoot,
                null,
                FileChangeKind.Modified
            )
        );
        await host.FileChanges.FlushAsync(Ct);

        Assert.Equal(1, Assert.IsType<TrackingBackend>(backend).ProjectChanges);
    }

    [Fact]
    public async Task Eviction_removes_the_old_backend_from_file_change_routing()
    {
        using var temp = new TempDirectory();
        Workspace composedWorkspace = new(CreateWorkspace(temp, "composed"));
        Workspace evictedWorkspace = new(CreateWorkspace(temp, "evicted"));
        Workspace liveWorkspace = new(CreateWorkspace(temp, "live"));
        Dictionary<string, TrackingBackend> backends = new(StringComparer.Ordinal);
        await using ExtensionSession host = await StartHost(
            temp,
            composedWorkspace,
            RoslynBuiltIn(root =>
            {
                var backend = new TrackingBackend();
                backends.Add(root, backend);
                return backend;
            }),
            capLimit: 1
        );
        ITool diagnostics = host.ComposeTools(composedWorkspace)
            .Single(tool => tool.Name == "cs_diagnostics");

        foreach (Workspace workspace in new[] { evictedWorkspace, liveWorkspace })
        {
            ToolResult result = await diagnostics.ExecuteAsync(
                Parse("{}"),
                new ToolContext(workspace.WorktreeRoot, new NullAgentEvents()),
                Ct
            );
            Assert.False(result.IsError, result.Output);
        }

        TrackingBackend evicted = backends[evictedWorkspace.WorktreeRoot];
        TrackingBackend live = backends[liveWorkspace.WorktreeRoot];
        Assert.True(evicted.Disposed);
        Assert.False(live.Disposed);

        string evictedPath = Path.Combine(evictedWorkspace.WorktreeRoot, "Source.cs");
        string livePath = Path.Combine(liveWorkspace.WorktreeRoot, "Source.cs");
        host.FileChanges.Notify(
            new FileChangeEvent(
                evictedPath,
                "Source.cs",
                evictedWorkspace.WorktreeRoot,
                null,
                FileChangeKind.Modified
            )
        );
        host.FileChanges.Notify(
            new FileChangeEvent(
                livePath,
                "Source.cs",
                liveWorkspace.WorktreeRoot,
                null,
                FileChangeKind.Modified
            )
        );
        await host.FileChanges.FlushAsync(Ct);

        Assert.Empty(evicted.ChangedFiles);
        Assert.Equal([livePath], live.ChangedFiles);
        Assert.Equal(2, backends.Count);
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

    private static Task<ToolResult> RunDiagnosticsAsync(ITool diagnostics, Workspace workspace) =>
        diagnostics.ExecuteAsync(
            Parse("{}"),
            new ToolContext(workspace.WorktreeRoot, new NullAgentEvents()),
            Ct
        );

    private sealed class TrackingBackend : ICSharpBackend, IDisposable
    {
        public List<string> ChangedFiles { get; } = [];

        public int ProjectChanges { get; private set; }

        public bool Disposed { get; private set; }

        public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct) =>
            ValueTask.FromResult(
                new WorkspaceLoadResult(WorkspaceStatus.Loaded, "loaded", null, 0, 0, [])
            );

        public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
            DiagnosticsScope scope,
            CancellationToken ct
        ) => ValueTask.FromResult(new DiagnosticsResult([], 0, 0, false, "the solution"));

        public ValueTask<SymbolSearchResult> FindSymbolAsync(string name, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<ReferencesResult> FindReferencesAsync(string name, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<OutlineResult> OutlineAsync(string file, CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<RenamePlanResult> PlanRenameAsync(
            string name,
            string newName,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public void NotifyFileChanged(string absolutePath) => ChangedFiles.Add(absolutePath);

        public void NotifyProjectChanged() => ProjectChanges++;

        public void Dispose() => Disposed = true;
    }

    private sealed class NullAgentEvents : IAgentEvents
    {
        public void Emit(AgentEvent agentEvent) { }
    }
}
