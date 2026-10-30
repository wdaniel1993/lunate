using System.Runtime.CompilerServices;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;

namespace Lunate.Roslyn.BuiltIn;

internal sealed class RoslynBuiltInFactory : IExtensionFactory
{
    private const string ManifestPath = "host/roslyn/extension.json";
    private const string ExtensionId = "roslyn";
    private const string WorkspaceServiceName = "roslyn";

    private readonly Func<string, ICSharpBackend>? _createBackend;

    internal RoslynBuiltInFactory(Func<string, ICSharpBackend>? createBackend = null) =>
        _createBackend = createBackend;

    internal static ExtensionManifest Manifest { get; } =
        ExtensionManifest.Parse(
            """
            {
              "id": "roslyn",
              "version": "1.0.0",
              "apiVersion": "^1.12.0",
              "builtin": true,
              "tools": ["diagnostics", "find_symbol", "find_references", "outline", "rename"],
              "workspaceServices": [{ "name": "roslyn", "maxLoadedWorkspaces": 2 }],
              "toolActivation": {
                "*": { "when": "workspace-root-has-file", "patterns": ["*.sln", "*.slnx", "*.csproj"] }
              },
              "toolAliases": {
                "diagnostics": "cs_diagnostics",
                "find_symbol": "cs_find_symbol",
                "find_references": "cs_find_references",
                "outline": "cs_outline",
                "rename": "cs_rename"
              }
            }
            """,
            ManifestPath
        );

    public IExtension Create(IExtensionContext context)
    {
        var services = new RoslynWorkspaceServiceFactory(_createBackend);
        context.RegisterWorkspaceService(WorkspaceServiceName, services);
        context.SubscribeFileChanged(services);

        Register(
            context,
            "diagnostics",
            static backend => new CsDiagnosticsTool(() => backend),
            static () => new CsDiagnosticsTool(UnusedBackend)
        );
        Register(
            context,
            "find_symbol",
            static backend => new CsFindSymbolTool(() => backend),
            static () => new CsFindSymbolTool(UnusedBackend)
        );
        Register(
            context,
            "find_references",
            static backend => new CsFindReferencesTool(() => backend),
            static () => new CsFindReferencesTool(UnusedBackend)
        );
        Register(
            context,
            "outline",
            static backend => new CsOutlineTool(() => backend),
            static () => new CsOutlineTool(UnusedBackend)
        );
        Register(
            context,
            "rename",
            static backend => new CsRenameTool(() => backend),
            static () => new CsRenameTool(UnusedBackend)
        );
        return EmptyExtension.Instance;
    }

    private static Func<ICSharpBackend> UnusedBackend =>
        static () =>
            throw new InvalidOperationException("the C# tool backend is acquired per call");

    private static void Register<TTool>(
        IExtensionContext context,
        string name,
        Func<ICSharpBackend, TTool> create,
        Func<TTool> metadata
    )
        where TTool : ITool
    {
        TTool tool = metadata();
        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = name,
                Description = tool.Description,
                ParametersSchema = tool.ParametersSchema,
                OutputSchema = tool.OutputSchema,
                Annotations = tool.Annotations,
                Exposure = tool.Exposure,
                Risk = tool.Risk,
                Handler = new WorkspaceToolHandler<TTool>(context, create),
            }
        );
    }

    private sealed class WorkspaceToolHandler<TTool>(
        IExtensionContext context,
        Func<ICSharpBackend, TTool> create
    ) : IExtensionToolHandler
        where TTool : ITool
    {
        public async Task<ToolResult> InvokeAsync(
            JsonElement arguments,
            ToolContext toolContext,
            CancellationToken ct
        )
        {
            using IWorkspaceServiceLease<RoslynWorkspaceService> lease = await context
                .AcquireWorkspaceServiceAsync<RoslynWorkspaceService>(
                    WorkspaceServiceName,
                    toolContext.WorkingDirectory,
                    ct
                )
                .ConfigureAwait(false);
            ITool tool = create(lease.Value.Backend);
            return await tool.ExecuteAsync(arguments, toolContext, ct).ConfigureAwait(false);
        }
    }

    private sealed class EmptyExtension : IExtension
    {
        internal static EmptyExtension Instance { get; } = new();
    }
}
