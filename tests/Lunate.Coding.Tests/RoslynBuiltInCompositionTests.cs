using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;
using Lunate.Roslyn.BuiltIn;

namespace Lunate.Coding.Tests;

public sealed class RoslynBuiltInCompositionTests
{
    [Fact]
    public async Task The_builtin_registers_the_manifest_aliases_and_disposes_its_workspace_service()
    {
        using var temp = new TempDirectory();
        CancellationToken ct = TestContext.Current.CancellationToken;
        string root = temp.File("workspace");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project />");
        Workspace workspace = new(root);
        ProbeBackend? backend = null;
        BuiltInExtension descriptor = new(
            "host/roslyn/extension.json",
            RoslynBuiltInFactory.Manifest,
            () => new RoslynBuiltInFactory(_ => backend = new ProbeBackend())
        );

        Assert.True(descriptor.Manifest.BuiltIn);
        Assert.Equal(
            ["diagnostics", "find_symbol", "find_references", "outline", "rename"],
            descriptor.Manifest.Tools
        );
        Assert.Equal(
            ["cs_diagnostics", "cs_find_references", "cs_find_symbol", "cs_outline", "cs_rename"],
            descriptor.Manifest.ToolAliases.Values.Order(StringComparer.Ordinal)
        );
        Assert.Equal(2, Assert.Single(descriptor.Manifest.WorkspaceServices).MaxLoadedWorkspaces);

        await using (
            ExtensionSession host = await ExtensionSession.StartAsync(
                workspace.Identity,
                new ExtensionSeams
                {
                    StorePath = temp.File("store"),
                    BuiltIns = [descriptor],
                    NoExtensions = true,
                    Log = new RecordingExtensionLog(),
                },
                ct
            )
        )
        {
            ExtensionDescriptor loaded = Assert.Single(host.Descriptors);
            Assert.Equal(ExtensionScope.Builtin, loaded.Scope);
            Assert.Null(loaded.Directory);
            Assert.Equal(0, host.LoadContextCount);
            Assert.Equal(ExtensionLoadState.Loaded, Assert.Single(host.LoadResults).State);
            IReadOnlyList<ITool> tools = host.ComposeTools(workspace);
            Assert.Equal(
                [
                    "cs_diagnostics",
                    "cs_find_symbol",
                    "cs_find_references",
                    "cs_outline",
                    "cs_rename",
                ],
                tools.Select(tool => tool.Name)
            );
            Assert.Null(backend);

            using JsonDocument args = JsonDocument.Parse("{}");
            ToolResult result = await tools
                .Single(tool => tool.Name == "cs_outline")
                .ExecuteAsync(
                    args.RootElement.Clone(),
                    new ToolContext(root, new NullAgentEvents()),
                    ct
                );

            Assert.True(result.IsError);
            Assert.NotNull(backend);
            Assert.False(backend.Disposed);
        }

        Assert.True(backend!.Disposed);
    }

    private sealed class ProbeBackend : ICSharpBackend, IDisposable
    {
        public bool Disposed { get; private set; }

        public ValueTask<WorkspaceLoadResult> LoadAsync(CancellationToken ct) =>
            throw new NotSupportedException();

        public ValueTask<DiagnosticsResult> GetDiagnosticsAsync(
            DiagnosticsScope scope,
            CancellationToken ct
        ) => throw new NotSupportedException();

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
}
