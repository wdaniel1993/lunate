using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The host-composed built-in fitness proof: manifest aliases and activation, per-workspace service
/// leases, and the /extensions report all pass through the production extension session.
/// </summary>
public sealed class ExtensionContractPrimitivesFitnessTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_builtin_composes_aliases_activation_and_workspace_services_without_an_alc()
    {
        using var temp = new TempDirectory();
        string root = Path.Combine(temp.Root, "worktree");
        string store = Path.Combine(temp.Root, "store");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(store);
        FitnessState state = new();
        ExtensionManifest manifest = ExtensionManifest.Parse(
            """
            {
              "id": "fitness",
              "version": "0.1.0",
              "apiVersion": "^1.0.0",
              "builtin": true,
              "tools": ["visible", "dormant"],
              "toolAliases": { "visible": "friendly", "dormant": "sleep" },
              "toolActivation": {
                "dormant": {
                  "when": "workspace-root-has-file",
                  "patterns": ["activate.marker"]
                }
              },
              "workspaceServices": [{ "name": "cache", "maxLoadedWorkspaces": 1 }]
            }
            """,
            "host/fitness/extension.json"
        );
        BuiltInExtension builtIn = new(
            "host/fitness/extension.json",
            manifest,
            () => new FitnessFactory(state)
        );

        await using ExtensionSession session = await ExtensionSession.StartAsync(
            new WorkspaceInfo(root, null, null),
            new ExtensionSeams { StorePath = store, BuiltIns = [builtIn] },
            Ct
        );

        Assert.Equal(0, session.LoadContextCount);
        IReadOnlyList<ITool> active = session.ComposeTools(new Workspace(root));
        Assert.Equal(["friendly"], active.Select(tool => tool.Name));
        ToolRegistry registry = new();
        foreach (ITool tool in active)
        {
            registry.Add(tool);
        }
        Assert.Equal(["friendly"], registry.Declarations.Select(tool => tool.Name));
        ToolResult toolResult = await registry
            .Find("friendly")!
            .ExecuteAsync(
                JsonSerializer.SerializeToElement(new { }),
                new ToolContext(root, new NullAgentEvents()),
                Ct
            );
        Assert.Equal("fitness tool ran", toolResult.Output);

        (
            IReadOnlyList<ExtensionDescriptor> descriptors,
            IReadOnlyList<ExtensionLoadResult> results
        ) = (session.Descriptors, session.LoadResults);
        string segment = Assert.Single(
            ExtensionsView.Segments(
                descriptors,
                results,
                id => session.RegisteredCounts(id),
                inactiveTools: session.InactiveTools
            )
        );
        Assert.Contains("fitness 0.1.0 (builtin) loaded", segment, StringComparison.Ordinal);
        Assert.Contains("tools 2", segment, StringComparison.Ordinal);
        Assert.Contains(
            "inactive 'fitness__dormant' (workspace-root-has-file)",
            segment,
            StringComparison.Ordinal
        );

        IExtensionContext context = Assert.IsAssignableFrom<IExtensionContext>(state.Context);
        string firstRoot = Path.Combine(temp.Root, "first-root");
        Directory.CreateDirectory(firstRoot);
        IWorkspaceServiceLease<WorkspaceCache> firstLease =
            await context.AcquireWorkspaceServiceAsync<WorkspaceCache>("cache", firstRoot, Ct);
        WorkspaceCache first = firstLease.Value;
        firstLease.Dispose();

        string secondRoot = Path.Combine(temp.Root, "second-root");
        Directory.CreateDirectory(secondRoot);
        IWorkspaceServiceLease<WorkspaceCache> secondLease =
            await context.AcquireWorkspaceServiceAsync<WorkspaceCache>("cache", secondRoot, Ct);
        WorkspaceCache second = secondLease.Value;
        Assert.True(first.Disposed);
        Assert.Equal(new Workspace(secondRoot).WorktreeRoot, second.Root);

        secondLease.Dispose();
        await session.DisposeAsync();
        Assert.True(second.Disposed);
    }

    private sealed class FitnessState
    {
        public IExtensionContext? Context { get; set; }

        public List<WorkspaceCache> Caches { get; } = [];
    }

    private sealed class FitnessFactory(FitnessState state) : IExtensionFactory
    {
        public IExtension Create(IExtensionContext context)
        {
            state.Context = context;
            context.RegisterWorkspaceService("cache", new WorkspaceCacheFactory(state));
            context.RegisterTool(Registration("visible"));
            context.RegisterTool(Registration("dormant"));
            return new FitnessExtension();
        }

        private static ExtensionToolRegistration Registration(string name) =>
            new()
            {
                Name = name,
                Description = $"Fitness {name} tool.",
                ParametersSchema = JsonSerializer.SerializeToElement(new { type = "object" }),
                Annotations = new ToolAnnotations(ReadOnly: true),
                Handler = new FitnessToolHandler(),
            };
    }

    private sealed class FitnessExtension : IExtension;

    private sealed class FitnessToolHandler : IExtensionToolHandler
    {
        public Task<ToolResult> InvokeAsync(
            JsonElement arguments,
            ToolContext context,
            CancellationToken ct
        ) => Task.FromResult(new ToolResult("fitness tool ran", IsError: false));
    }

    private sealed class WorkspaceCacheFactory(FitnessState state)
        : IWorkspaceServiceFactory<WorkspaceCache>
    {
        public WorkspaceCache Create(string canonicalWorktreeRoot)
        {
            WorkspaceCache cache = new(canonicalWorktreeRoot);
            state.Caches.Add(cache);
            return cache;
        }
    }

    private sealed class WorkspaceCache(string root) : IDisposable
    {
        public string Root { get; } = root;

        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
