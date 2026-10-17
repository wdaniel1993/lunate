using System.Runtime.CompilerServices;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// Composition shared by the coding frontends: the workspace tool set, the session file, the model
/// resolution and the client factory. Both print mode and the interactive session build the same
/// harness around these. The C# tools compose through the session's per-workspace
/// <see cref="WorkspaceServices"/> and are offered only where the workspace looks like a C#
/// workspace (a cheap root check, no Roslyn); nothing loads before the first <c>cs_*</c> call.
/// </summary>
internal static class HarnessFactory
{
    internal static ToolRegistry CreateTools(
        Workspace workspace,
        ITextFileAccess? files = null,
        WorkspaceServices? services = null,
        SkillReadAllowance? skillReadAllowance = null,
        IExtensionLog? log = null
    )
    {
        var tools = new ToolRegistry();
        tools.Add(new ReadTool(workspace, files, skillReadAllowance));
        tools.Add(new WriteTool(workspace, files: files));
        tools.Add(new EditTool(workspace, files: files));
        tools.Add(new BashTool(workspace, log: log));
        if (services is not null && LooksLikeCSharpWorkspace(workspace.WorktreeRoot))
        {
            AddCSharpTools(tools, services, workspace.WorktreeRoot);
        }

        return tools;
    }

    /// <summary>
    /// The workspace-tool factory of a run: the child's own base tool set and <c>cs_*</c> tools
    /// bound to the child root, plus the inherited tools that are safe in any root (the parent
    /// session's extension tools as-is and the MCP tools whose server is marked
    /// workspace-independent). Project MCP tools and their servers stay in the parent's directory
    /// and are not offered.
    /// </summary>
    internal static Func<string, ToolRegistry> CreateWorkspaceTools(
        WorkspaceServices services,
        IReadOnlyList<ITool> inheritedTools,
        Func<string, IReadOnlyList<string>>? skillReadRoots = null,
        IExtensionLog? log = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(inheritedTools);
        return root =>
        {
            ToolRegistry tools = CreateTools(
                new Workspace(root),
                services: services,
                skillReadAllowance: skillReadRoots is null
                    ? null
                    : new SkillReadAllowance(skillReadRoots(root)),
                log: log
            );
            foreach (ITool tool in inheritedTools)
            {
                if (tools.Find(tool.Name) is null)
                {
                    tools.Add(tool);
                }
            }

            return tools;
        };
    }

    /// <summary>
    /// Whether the workspace root looks like a C# workspace: a <c>.sln</c>, <c>.slnx</c> or
    /// <c>.csproj</c> file at the root. A cheap bounded enumeration; no Roslyn and no MSBuild is
    /// touched, so the tools can be composed before anything loads.
    /// </summary>
    internal static bool LooksLikeCSharpWorkspace(string worktreeRoot)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(worktreeRoot))
            {
                if (Path.GetExtension(file) is ".sln" or ".slnx" or ".csproj")
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable root offers no C# tools; the call fails as it would outside a solution.
        }

        return false;
    }

    /// <summary>
    /// Composes the five <c>cs_*</c> tools, each bound to the workspace root through the registry.
    /// Isolated and never inlined so no Roslyn assembly loads before the first call.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddCSharpTools(ToolRegistry tools, WorkspaceServices services, string root)
    {
        Func<ICSharpBackend> backend = () => services.BackendFor(root);
        tools.Add(new CsDiagnosticsTool(backend));
        tools.Add(new CsFindSymbolTool(backend));
        tools.Add(new CsFindReferencesTool(backend));
        tools.Add(new CsOutlineTool(backend));
        tools.Add(new CsRenameTool(backend));
    }

    internal static string DefaultSessionDirectory(Workspace workspace) =>
        workspace.GitCommonDir is { } identity
            ? SessionPaths.ForRepository(identity, workspace.WorktreeRoot)
            : SessionPaths.ForProject(workspace.WorktreeRoot);

    /// <summary>
    /// Creates the session file under the directory with its id as the file name (the store names
    /// sessions <c>&lt;sessionId&gt;.jsonl</c>): create, rename to the store-assigned id, reload.
    /// </summary>
    internal static Session CreateSession(string directory, Workspace workspace)
    {
        Directory.CreateDirectory(directory);
        string provisional = Path.Combine(directory, Path.GetRandomFileName());
        Session created = Session.Create(
            provisional,
            workspace.WorktreeRoot,
            workspace.GitCommonDir,
            workspace.WorktreeRoot
        );
        string path = Path.Combine(directory, SessionPaths.SessionFileName(created.SessionId));
        File.Move(provisional, path);
        return Session.Load(path);
    }

    internal static ModelInfo ResolveModel(string? modelId, ModelCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new InvalidOperationException(
                "No model is configured. Set 'model' in ~/.lunate/settings.json, set LUNATE_MODEL, "
                    + "or run lunate --discover <name-or-url> to draft a catalog entry."
            );
        }

        return catalog.Find(modelId)
            ?? throw new InvalidOperationException(
                $"Model '{modelId}' is not in the catalog. Run lunate --discover <name-or-url> "
                    + $"to draft a models.json entry or add '{modelId}' to ~/.lunate/models.json."
            );
    }

    internal static IChatClientFactory CreateFactory(
        string? authPath,
        Func<string, string?>? environment
    )
    {
        AuthStore auth = AuthStore.Load(authPath, environment);
        return new ChatClientFactory(NullLoggerFactory.Instance, namedKeySource: auth.TryGet);
    }
}
