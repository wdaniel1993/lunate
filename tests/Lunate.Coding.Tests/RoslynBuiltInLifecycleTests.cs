using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;
using Lunate.Roslyn.BuiltIn;

namespace Lunate.Coding.Tests;

[Collection("roslyn-workspaces")]
public sealed class RoslynBuiltInLifecycleTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Host_shutdown_disposes_each_loaded_builtin_backend()
    {
        using var temp = new TempDirectory();
        Workspace composedWorkspace = new(CreateWorkspace(temp, "composed"));
        Workspace firstWorkspace = new(CreateWorkspace(temp, "first"));
        Workspace secondWorkspace = new(CreateWorkspace(temp, "second"));
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
            capLimit: 2
        );

        ITool diagnostics = host.ComposeTools(composedWorkspace)
            .Single(tool => tool.Name == "cs_diagnostics");
        Assert.False((await RunDiagnosticsAsync(diagnostics, firstWorkspace)).IsError);
        Assert.False((await RunDiagnosticsAsync(diagnostics, secondWorkspace)).IsError);
        Assert.All(backends.Values, backend => Assert.False(backend.Disposed));

        await host.DisposeAsync();

        Assert.Equal(2, backends.Count);
        Assert.All(backends.Values, backend => Assert.True(backend.Disposed));
    }

    [Fact]
    public async Task A_pinned_backend_survives_eviction_until_the_tool_call_completes()
    {
        using var temp = new TempDirectory();
        Workspace composedWorkspace = new(CreateWorkspace(temp, "composed"));
        Workspace inFlightWorkspace = new(CreateWorkspace(temp, "in-flight"));
        Workspace evictableWorkspace = new(CreateWorkspace(temp, "evictable"));
        Workspace nextWorkspace = new(CreateWorkspace(temp, "next"));
        string pinnedRoot = GitIdentity.Canonicalize(inFlightWorkspace.WorktreeRoot);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Dictionary<string, TrackingBackend> backends = new(StringComparer.Ordinal);
        await using ExtensionSession host = await StartHost(
            temp,
            composedWorkspace,
            RoslynBuiltIn(root =>
            {
                var backend = new TrackingBackend(
                    root == pinnedRoot ? entered : null,
                    root == pinnedRoot ? release : null
                );
                backends.Add(root, backend);
                return backend;
            }),
            capLimit: 2
        );
        ITool diagnostics = host.ComposeTools(composedWorkspace)
            .Single(tool => tool.Name == "cs_diagnostics");

        Task<ToolResult> inFlight = RunDiagnosticsAsync(diagnostics, inFlightWorkspace);
        try
        {
            await entered.Task.WaitAsync(WaitBudgets.InProcess, Ct);
            Assert.False((await RunDiagnosticsAsync(diagnostics, evictableWorkspace)).IsError);
            Assert.False((await RunDiagnosticsAsync(diagnostics, nextWorkspace)).IsError);

            Assert.False(backends[pinnedRoot].Disposed);
            Assert.True(
                backends[GitIdentity.Canonicalize(evictableWorkspace.WorktreeRoot)].Disposed
            );
            Assert.False(backends[GitIdentity.Canonicalize(nextWorkspace.WorktreeRoot)].Disposed);
        }
        finally
        {
            release.SetResult();
            await inFlight.WaitAsync(WaitBudgets.InProcess, Ct);
        }

        Assert.False((await inFlight).IsError);
        Assert.False(backends[pinnedRoot].Disposed);
    }

    [Fact]
    public async Task Child_workspace_tools_acquire_from_the_hosts_shared_service_cap()
    {
        using var temp = new TempDirectory();
        Workspace parentWorkspace = new(CreateWorkspace(temp, "parent"));
        Workspace firstChildWorkspace = new(CreateWorkspace(temp, "first-child"));
        Workspace secondChildWorkspace = new(CreateWorkspace(temp, "second-child"));
        Dictionary<string, TrackingBackend> backends = new(StringComparer.Ordinal);
        await using ExtensionSession host = await StartHost(
            temp,
            parentWorkspace,
            RoslynBuiltIn(root =>
            {
                var backend = new TrackingBackend();
                backends.Add(root, backend);
                return backend;
            }),
            capLimit: 2
        );
        ITool parentDiagnostics = host.ComposeTools(parentWorkspace)
            .Single(tool => tool.Name == "cs_diagnostics");
        Func<string, ToolRegistry> childTools = HarnessFactory.CreateWorkspaceTools(
            root => host.ComposeTools(new Workspace(root)),
            Array.Empty<ITool>()
        );

        Assert.False((await RunDiagnosticsAsync(parentDiagnostics, parentWorkspace)).IsError);
        foreach (Workspace childWorkspace in new[] { firstChildWorkspace, secondChildWorkspace })
        {
            ITool childDiagnostics = childTools(childWorkspace.WorktreeRoot)
                .Find("cs_diagnostics")!;
            Assert.False((await RunDiagnosticsAsync(childDiagnostics, childWorkspace)).IsError);
        }

        Assert.Equal(3, backends.Count);
        Assert.True(backends[GitIdentity.Canonicalize(parentWorkspace.WorktreeRoot)].Disposed);
        Assert.False(backends[GitIdentity.Canonicalize(firstChildWorkspace.WorktreeRoot)].Disposed);
        Assert.False(
            backends[GitIdentity.Canonicalize(secondChildWorkspace.WorktreeRoot)].Disposed
        );
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

    private static Task<ToolResult> RunDiagnosticsAsync(ITool diagnostics, Workspace workspace) =>
        diagnostics.ExecuteAsync(
            JsonDocument.Parse("{}").RootElement.Clone(),
            new ToolContext(workspace.WorktreeRoot, new NullAgentEvents()),
            Ct
        );

    private sealed class TrackingBackend(
        TaskCompletionSource? diagnosticsEntered = null,
        TaskCompletionSource? diagnosticsRelease = null
    ) : ICSharpBackend, IDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct) =>
            ValueTask.FromResult(
                new WorkspaceLoadResult(WorkspaceStatus.Loaded, "loaded", null, 0, 0, [])
            );

        public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
            DiagnosticsScope scope,
            CancellationToken ct
        ) => GetDiagnosticsAsync(ct);

        private async ValueTask<DiagnosticsResult> GetDiagnosticsAsync(CancellationToken ct)
        {
            if (diagnosticsEntered is not null && diagnosticsRelease is not null)
            {
                diagnosticsEntered.SetResult();
                await diagnosticsRelease.Task.WaitAsync(ct);
            }

            return new DiagnosticsResult([], 0, 0, false, "the solution");
        }

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

        public void NotifyFileChanged(string absolutePath) { }

        public void NotifyProjectChanged() { }

        public void Dispose() => Disposed = true;
    }

    private sealed class NullAgentEvents : IAgentEvents
    {
        public void Emit(AgentEvent agentEvent) { }
    }
}
