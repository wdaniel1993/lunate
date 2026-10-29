using System.Runtime.CompilerServices;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;
using Microsoft.Extensions.Logging.Abstractions;
using AgentToolExposure = Lunate.Agent.ToolExposure;

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
        IExtensionLog? log = null,
        IReadOnlyList<string>? extraTools = null,
        CredentialRedactor? redaction = null
    )
    {
        var tools = new ToolRegistry();
        tools.Add(new ReadTool(workspace, files, skillReadAllowance));
        tools.Add(
            redaction is null
                ? new WriteTool(workspace, files: files)
                : new WriteTool(workspace, null, files, redaction)
        );
        tools.Add(
            redaction is null
                ? new EditTool(workspace, files: files)
                : new EditTool(workspace, null, files, redaction)
        );
        tools.Add(new BashTool(workspace, log: log));
        AddOptionalTools(tools, workspace, extraTools);
        if (services is not null && LooksLikeCSharpWorkspace(workspace.WorktreeRoot))
        {
            AddCSharpTools(tools, services, workspace.WorktreeRoot);
        }

        return tools;
    }

    /// <summary>
    /// Adds the configured optional search tools. Registration is additive and skips any name the
    /// registry already provides, so a core tool can never be shadowed; the settings validator
    /// rejects core names before this point, and this guard keeps the factory safe on its own.
    /// </summary>
    private static void AddOptionalTools(
        ToolRegistry tools,
        Workspace workspace,
        IReadOnlyList<string>? names
    )
    {
        if (names is not { Count: > 0 })
        {
            return;
        }

        foreach (string name in names)
        {
            if (!OptionalTools.IsName(name) || tools.Find(name) is not null)
            {
                continue;
            }

            switch (name)
            {
                case "grep":
                    tools.Add(new GrepTool(workspace));
                    break;
                case "find":
                    tools.Add(new FindTool(workspace));
                    break;
                case "ls":
                    tools.Add(new LsTool(workspace));
                    break;
            }
        }
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
        IExtensionLog? log = null,
        IReadOnlyList<string>? extraTools = null
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
                log: log,
                extraTools: extraTools
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

    /// <summary>
    /// Narrows the composed registry to the exact model-facing names the launch selected. A null
    /// list keeps the registry as composed; <c>--no-tools</c> is the empty list. An unknown name is
    /// reported with the available names in registration order and leaves the registry untouched.
    /// </summary>
    internal static bool TryFilterTools(
        ToolRegistry tools,
        IList<ITool>? inherited,
        LaunchOptions launch,
        out string? error
    )
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(launch);

        error = null;
        IReadOnlyList<string>? requested = launch.NoTools ? [] : launch.Tools;
        if (requested is not null && launch.ExtraTools is { Count: > 0 } extra)
        {
            // The optional names in --tools were extracted from the filter list; they are part
            // of the selection, so a mixed list such as "read,grep" keeps both.
            requested = [.. requested, .. extra];
        }

        if (requested is null)
        {
            return true;
        }

        List<string> available = [];
        foreach (ITool tool in tools.Tools)
        {
            if (tool.Exposure is AgentToolExposure.Direct or AgentToolExposure.ModelOnly)
            {
                available.Add(tool.Name);
            }
        }

        List<string> unknown = [];
        foreach (string name in requested)
        {
            if (!available.Contains(name, StringComparer.Ordinal))
            {
                unknown.Add(name);
            }
        }

        if (unknown.Count > 0)
        {
            string names = string.Join(", ", unknown.Select(name => $"'{name}'"));
            error =
                $"--tools names unknown tools: {names}; available: {string.Join(", ", available)}";
            return false;
        }

        HashSet<string> requestedNames = new(requested, StringComparer.Ordinal);
        foreach (ITool tool in tools.Tools.ToList())
        {
            if (!requestedNames.Contains(tool.Name))
            {
                tools.Remove(tool.Name);
            }
        }

        if (inherited is not null)
        {
            for (int index = inherited.Count - 1; index >= 0; index--)
            {
                if (!requestedNames.Contains(inherited[index].Name))
                {
                    inherited.RemoveAt(index);
                }
            }
        }

        return true;
    }

    internal static string DefaultSessionDirectory(Workspace workspace) =>
        workspace.GitCommonDir is { } identity
            ? SessionPaths.ForRepository(identity, workspace.WorktreeRoot)
            : SessionPaths.ForProject(workspace.WorktreeRoot);

    /// <summary>
    /// The values the session's persistence boundary needs: the auth store, the provider
    /// environment, the extension secret files and the MCP config files. Collected exactly once and
    /// then handed to both the tools (the write/edit guards) and the session (persistence), so both
    /// see the same values.
    /// </summary>
    internal static CredentialRedactor CollectRedaction(
        string? authPath,
        Func<string, string?>? environment,
        string? storePath,
        string worktreeRoot,
        string? mcpConfigPath = null,
        string? mcpAgentsConfigPath = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worktreeRoot);
        List<string> configPaths =
        [
            mcpAgentsConfigPath ?? McpComposition.DefaultAgentsConfigPath,
            mcpConfigPath ?? McpComposition.DefaultConfigPath,
            .. McpComposition.ProjectConfigPaths(worktreeRoot),
        ];
        return CredentialRedactorSource.Collect(
            AuthStore.Load(authPath, environment),
            environment ?? Environment.GetEnvironmentVariable,
            storePath ?? ExtensionHostOptions.Default.StorePath,
            configPaths
        );
    }

    /// <summary>
    /// Gives a session the redactor its persistence boundary uses: every line the session file and
    /// spill files receive has the credentials this host knows replaced. The same redactor backs
    /// the write/edit guards, so the placeholder the context holds is the one the guards refuse.
    /// </summary>
    internal static void AttachRedaction(Session session, CredentialRedactor redactor)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Redactor = redactor;
    }

    /// <summary>
    /// Creates the session file under the directory with its id as the file name (the store names
    /// sessions <c>&lt;sessionId&gt;.jsonl</c>): create, rename to the store-assigned id, reload.
    /// </summary>
    internal static Session CreateSession(string directory, Workspace workspace) =>
        SessionProvisioning.Create(
            directory,
            provisional =>
                Session.Create(
                    provisional,
                    workspace.WorktreeRoot,
                    workspace.GitCommonDir,
                    workspace.WorktreeRoot
                )
        );

    /// <summary>
    /// Loads the model catalog with the stored custom providers layered over the files, so
    /// <c>/model</c> and model resolution see them through the same lookup as a preset entry.
    /// </summary>
    internal static ModelCatalog LoadCatalog(AgentSettings settings, string? modelsPath)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ModelCatalog catalog = ModelCatalog.Load(modelsPath);
        return settings.Providers.Count == 0
            ? catalog
            : catalog.WithAdditionalModels([
                .. settings.Providers.Select(provider => provider.ToModelInfo()),
            ]);
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

        try
        {
            return catalog.Find(modelId)
                ?? throw new UnknownModelException(
                    $"Model '{modelId}' is not in the catalog. Run lunate --discover <name-or-url> "
                        + $"to draft a models.json entry or add '{modelId}' to ~/.lunate/models.json."
                );
        }
        catch (InvalidOperationException exception)
        {
            // An ambiguous bare id already names every candidate and the qualified-id fix; the
            // configuration-error path owns the message and the exit code.
            throw new UnknownModelException(exception.Message);
        }
    }

    internal static IChatClientFactory CreateFactory(
        string? authPath,
        Func<string, string?>? environment
    ) => CreateFactory(AuthStore.Load(authPath, environment));

    /// <summary>
    /// The factory over an already loaded auth store: the interactive session resolves the store
    /// once, for the client and for the welcome header's first-run check.
    /// </summary>
    internal static IChatClientFactory CreateFactory(AuthStore auth)
    {
        ArgumentNullException.ThrowIfNull(auth);
        return new ChatClientFactory(NullLoggerFactory.Instance, namedKeySource: auth.TryGet);
    }
}
