using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
using Lunate.Protocols.Acp;

namespace Lunate.Coding;

/// <summary>
/// ACP mode: <c>lunate --acp</c> serves the Agent Client Protocol over stdio so editors drive
/// Lunate as their agent. Nothing but protocol frames may reach stdout; every diagnostic goes to
/// stderr. Approvals compose the configured policy with the client's permission request, and the
/// read/write/edit tools use the editor's file system when the client offered it at initialize;
/// there is no yolo in ACP mode.
/// </summary>
internal static class AcpMode
{
    internal static int Run(
        TextWriter errors,
        Stream input,
        Stream output,
        CancellationToken ct,
        ExtensionSeams? extensions = null,
        string? mcpConfigPath = null,
        string? mcpAgentsConfigPath = null,
        LaunchOptions? launch = null
    )
    {
        var interaction = new AcpUserInteraction(line => errors.WriteLine($"lunate: {line}"));
        IAcpServer server = new LibAcpServer(line =>
            interaction.Notify(new UserNotice(NoticeLevel.Info, line))
        );

        // The host composes once at serve start (the session's first use) and every session/new
        // reuses the same hook seam and the MCP tools listed once here.
        ExtensionSession? host = null;
        McpToolSet? mcp = null;
        List<ITool> mcpTools = [];
        List<ITool> inheritedTools = [];
        var workspaceServices = new WorkspaceServices(RoslynSettings.DefaultMaxLoadedWorkspaces);
        try
        {
            var workspace = new Workspace(Environment.CurrentDirectory);
            LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
            IExtensionLog log = extensions?.Log ?? new StderrExtensionLog(errors);
            AgentSettings settings = SettingsStore.Resolve(null, null);
            WorkspaceServices configured = new(settings.Roslyn.MaxLoadedWorkspaces);
            workspaceServices.Dispose();
            workspaceServices = configured;

            if (extensions?.NoExtensions is not true)
            {
                host = ComposeExtensions(extensions, workspace, errors, ct);
            }

            // The bus exists only when the extension host composed; scan-only without extensions.
            if (host is not null)
            {
                workspaceServices.Connect(host.FileChanges);
            }

            mcp = McpComposition
                .ComposeAsync(
                    workspace,
                    mcpConfigPath,
                    mcpAgentsConfigPath,
                    extensions,
                    new NonInteractiveMcpConfigTrustPrompt(),
                    new NonInteractiveMcpServerApprovalPrompt(),
                    log,
                    notice => interaction.Notify(notice),
                    ct
                )
                .GetAwaiter()
                .GetResult();

            if (host is not null)
            {
                // Extension tools first, in registration order, then the MCP tools (design D7).
                mcpTools.AddRange(host.Tools);
                inheritedTools.AddRange(host.Tools);
                // The configured names are the collision baseline: a same-named registration is
                // skipped before listing (the configured server wins); nothing starts before.
                ExtensionMcpServers servers = host.ComposeMcpServers(
                    McpNameCollisions.ConfiguredServers(mcp)
                );
                ToolComposition.AddSkippingTaken(
                    mcpTools,
                    servers.ListToolsAsync(ct).GetAwaiter().GetResult(),
                    "an extension-registered MCP server",
                    notice => interaction.Notify(new UserNotice(NoticeLevel.Warning, notice))
                );
            }

            if (mcp is not null)
            {
                ToolComposition.AddSkippingTaken(
                    mcpTools,
                    mcp.ListToolsAsync(ct).GetAwaiter().GetResult(),
                    "mcp.json",
                    notice => interaction.Notify(new UserNotice(NoticeLevel.Warning, notice))
                );
                // A child run in another worktree may be offered the tools of servers explicitly
                // marked workspace-independent (and only those); skipped collisions stay skipped.
                foreach (ITool tool in mcp.ListedToolsFrom(options => options.WorkspaceIndependent))
                {
                    if (mcpTools.Contains(tool))
                    {
                        inheritedTools.Add(tool);
                    }
                }

                McpComposition.NotifyFailures(mcp, notice => interaction.Notify(notice));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            DisposeComposition(host, mcp, errors);
            return 130;
        }
        catch (Exception exception)
        {
            errors.WriteLine($"lunate: the session composition failed: {exception.Message}");
        }

        try
        {
            server
                .RunAsync(
                    input,
                    output,
                    context =>
                        CreateHarness(
                            context,
                            host,
                            mcpTools,
                            inheritedTools,
                            workspaceServices,
                            launch
                        ),
                    ct
                )
                .GetAwaiter()
                .GetResult();
            return 0;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception)
        {
            errors.WriteLine($"lunate: {exception.Message}");
            return 1;
        }
        finally
        {
            DisposeComposition(host, mcp, errors);
            workspaceServices.Dispose();
        }
    }

    /// <summary>
    /// Composes the ACP host before the first frame is served: the process working directory is the
    /// serve-time workspace (session cwds follow the editor's per-session roots); the trust prompt
    /// defaults to the non-interactive refusal.
    /// </summary>
    private static ExtensionSession ComposeExtensions(
        ExtensionSeams? extensions,
        Workspace workspace,
        TextWriter errors,
        CancellationToken ct
    )
    {
        ExtensionSeams resolved = (extensions ?? new ExtensionSeams()) with
        {
            Log = extensions?.Log ?? new StderrExtensionLog(errors),
        };
        return ExtensionSession
            .StartAsync(workspace.Identity, resolved, ct)
            .GetAwaiter()
            .GetResult();
    }

    private static void DisposeComposition(
        ExtensionSession? host,
        McpToolSet? mcp,
        TextWriter errors
    )
    {
        if (mcp is not null)
        {
            try
            {
                mcp.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                errors.WriteLine(
                    $"lunate: the MCP servers failed to stop cleanly: {exception.Message}"
                );
            }
        }

        if (host is null)
        {
            return;
        }

        try
        {
            host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            errors.WriteLine(
                $"lunate: the extension host failed to stop cleanly: {exception.Message}"
            );
        }
    }

    /// <summary>
    /// The ACP session/new harness: a fresh session per editor session, with the launch prompt
    /// directives recorded on it. Tests inject the client factory and the session directory.
    /// </summary>
    internal static AgentHarness CreateHarness(
        AcpSessionContext context,
        ExtensionSession? host,
        IReadOnlyList<ITool> mcpTools,
        List<ITool> inheritedTools,
        WorkspaceServices workspaceServices,
        LaunchOptions? launch,
        IChatClientFactory? factory = null,
        string? sessionDirectory = null
    )
    {
        AgentSettings settings = SettingsStore.Resolve(null, null);
        settings = SettingsStore.ApplyLaunch(settings, launch ?? new LaunchOptions());
        ModelCatalog catalog = ModelCatalog.Load(null);
        ModelInfo model = HarnessFactory.ResolveModel(settings.Model, catalog);
        IChatClientFactory clientFactory = factory ?? HarnessFactory.CreateFactory(null, null);

        var workspace = new Workspace(context.Cwd);
        // Every editor session may name its own root, so the refusal is re-asked here rather than
        // trusted from serve start.
        LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
        ToolRegistry tools = HarnessFactory.CreateTools(
            workspace,
            context.FileAccess,
            workspaceServices,
            log: host?.Log,
            extraTools: settings.Tools.Extra
        );
        foreach (ITool tool in mcpTools)
        {
            tools.Add(tool);
        }

        if (
            !HarnessFactory.TryFilterTools(
                tools,
                inheritedTools,
                launch ?? new LaunchOptions(),
                out string? toolError
            )
        )
        {
            throw new ToolFilterException(toolError!);
        }

        Session session = HarnessFactory.CreateSession(
            sessionDirectory ?? HarnessFactory.DefaultSessionDirectory(workspace),
            workspace
        );
        HarnessFactory.AttachRedaction(
            session,
            authPath: null,
            environment: null,
            storePath: null,
            workspace.WorktreeRoot
        );
        LaunchOptions directives = launch ?? new LaunchOptions();
        SystemPrompt.Record(session, directives);
        var options = new AgentHarnessOptions
        {
            SystemPrompt = SystemPrompt.Compose(
                workspace,
                [.. tools.Tools.Select(tool => tool.Name)],
                SystemPrompt.Directives(directives, session)
            ),
            WorkingDirectory = workspace.WorktreeRoot,
            ToolOutputLimit = settings.ToolOutputLimit,
            SubagentLimits = settings.Subagents,
            ThinkingLevel = settings.Thinking,
            Approver = context.ClientApprover is { } clientApprover
                ? new AcpApprover(settings.Approval, clientApprover)
                : new NonInteractiveApprover(settings.Approval, yolo: false),
            Hooks = host?.Hooks,
            Session = session,
            FileChanges = (IFileChangeSink?)host?.FileChanges ?? NullFileChangeSink.Instance,
            Model = model,
            ModelCatalog = catalog,
            ModelId = model.Id,
            WorkspaceTools = HarnessFactory.CreateWorkspaceTools(
                workspaceServices,
                inheritedTools,
                log: host?.Log,
                extraTools: settings.Tools.Extra
            ),
        };

        return new AgentHarness(clientFactory.Create(model), tools, options);
    }
}
