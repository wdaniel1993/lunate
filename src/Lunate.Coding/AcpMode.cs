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
        LaunchOptions? launch = null,
        string? hardlinkScanRoot = null,
        IHardlinkScanFileSystem? hardlinkScanFileSystem = null
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
        try
        {
            var workspace = new Workspace(Environment.CurrentDirectory);
            LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
            IExtensionLog log = extensions?.Log ?? new StderrExtensionLog(errors);
            AgentSettings settings = SettingsStore.Resolve(null, null);
            // The redaction set exists before the MCP composition: composing with it withholds the
            // values Lunate knows at serve time from every configured server's child environment
            // (#449/#455). The per-session harness later collects its own set with the session's
            // cwd (CreateHarness), which is a different workspace's project configs.
            CredentialRedactor redaction = HarnessFactory.CollectRedaction(
                authPath: null,
                environment: null,
                storePath: null,
                workspace.WorktreeRoot
            );
            if (settings.BashGuard)
            {
                StateRootHardlinkScan.Report(
                    hardlinkScanRoot,
                    hardlinkScanFileSystem,
                    warning => errors.WriteLine("lunate: " + warning)
                );
            }

            // The host composes in every mode: with --no-extensions it composes nothing at all —
            // built-ins included — so no store scan, trust question or third-party load runs
            // (design §1).
            host = ComposeExtensions(
                (extensions ?? new ExtensionSeams()) with
                {
                    WorkspaceServiceCapLimit = settings.Roslyn.MaxLoadedWorkspaces,
                },
                workspace,
                errors,
                ct
            );

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
                    ct,
                    withheldCredentialValues: [.. redaction.Values.Select(pair => pair.Value)]
                )
                .GetAwaiter()
                .GetResult();

            if (host is not null)
            {
                // Extension tools first, in registration order, then the MCP tools (design D7);
                // the activation filter runs once per composition, so an inactive tool never
                // enters the served lists, the model declarations or the inherited snapshot, and
                // the withheld ones are recorded for the /extensions view.
                IReadOnlyList<ITool> extensionTools = host.ComposeTools(workspace);
                mcpTools.AddRange(extensionTools);
                inheritedTools.AddRange(
                    extensionTools.Where(tool => !host.IsWorkspaceBoundTool(tool))
                );
                // The configured names are the collision baseline: a same-named registration is
                // skipped before listing (the configured server wins); nothing starts before.
                ExtensionMcpServers servers = host.ComposeMcpServers(
                    McpNameCollisions.ConfiguredServers(mcp),
                    withheldCredentialValues: [.. redaction.Values.Select(pair => pair.Value)]
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
            // A composition failure is not a session: serving frames against a half-built host
            // (no extension seam, no MCP tools, a workspace the state root guard refused) would
            // answer every editor request with an agent that is missing what the user configured.
            errors.WriteLine($"lunate: the session composition failed: {exception.Message}");
            DisposeComposition(host, mcp, errors);
            return 1;
        }

        try
        {
            server
                .RunAsync(
                    input,
                    output,
                    // The decision profile degrades to no gate on resolve failure; the degrade goes
                    // to the host log, never stdout (only protocol frames own it).
                    context =>
                        CreateHarness(
                            context,
                            host,
                            mcpTools,
                            inheritedTools,
                            launch,
                            warn: notice => interaction.Notify(notice)
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
        LaunchOptions? launch,
        IChatClientFactory? factory = null,
        string? sessionDirectory = null,
        AgentSettings? settings = null,
        ModelCatalog? catalog = null,
        string? authPath = null,
        Action<UserNotice>? warn = null
    )
    {
        AgentSettings effective = SettingsStore.ApplyLaunch(
            settings ?? SettingsStore.Resolve(null, null),
            launch ?? new LaunchOptions()
        );
        ModelCatalog models = catalog ?? HarnessFactory.LoadCatalog(effective, null);
        ModelInfo model = HarnessFactory.ResolveModel(effective.Model, models);
        IChatClientFactory clientFactory = factory ?? HarnessFactory.CreateFactory(null, null);

        var workspace = new Workspace(context.Cwd);
        // Every editor session may name its own root, so the refusal is re-asked here rather than
        // trusted from serve start.
        LunateHome.StoreOutsideWorkspace(workspace.WorktreeRoot, workspace.RepoRoot);
        CredentialRedactor redaction = HarnessFactory.CollectRedaction(
            authPath: null,
            environment: null,
            storePath: null,
            workspace.WorktreeRoot,
            providerStorePath: AuthStore.ProviderStoreFilePath(null)
        );
        var refusalTracker = new WriteVerificationRefusalTracker();
        ToolRegistry tools = HarnessFactory.CreateTools(
            workspace,
            files: context.FileAccess,
            log: host?.Log,
            extraTools: effective.Tools.Extra,
            redaction: redaction,
            writeVerifiers: host?.WriteVerifiers,
            verificationMode: effective.Verify,
            refusalTracker: refusalTracker,
            bashGuardEnabled: effective.BashGuard
        );
        tools.Add(new AskUserTool());
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
        HarnessFactory.AttachRedaction(session, redaction);
        LaunchOptions directives = launch ?? new LaunchOptions();
        SystemPrompt.Record(session, directives);
        // The decision model resolves once, before the harness options: the decision usage
        // client, the decide-tool declaration and the gate's settings all come from one
        // resolution. The decision gate is wired in ACP like in the interactive session: with a
        // client approver an enforce-mode deny above the threshold asks the editor (the escalation
        // flag is honored), without one the call is denied fail-closed. A profile that cannot
        // resolve degrades to no gate with a visible warning, not a composition error.
        UsageModelClient? decisionModel = DecisionUsageModels.ResolveDecisionModel(
            effective,
            LoadSources,
            models,
            name => Environment.GetEnvironmentVariable(name),
            clientFactory,
            warn ?? (_ => { }),
            []
        );
        IReadOnlyDictionary<string, UsageModelClient>? usageModels = decisionModel is null
            ? null
            : new Dictionary<string, UsageModelClient>(StringComparer.Ordinal)
            {
                ["decision"] = decisionModel,
            };

        var options = new AgentHarnessOptions
        {
            SystemPrompt = SystemPrompt.Compose(
                workspace,
                ToolNames(),
                SystemPrompt.Directives(directives, session)
            ),
            WorkingDirectory = workspace.WorktreeRoot,
            ToolOutputLimit = effective.ToolOutputLimit,
            SpillThresholdTokens = effective.SpillThresholdTokens,
            ProjectionBatchTurns = effective.ProjectionBatchTurns,
            ProjectionMaskTurns = effective.ProjectionMaskTurns,
            ProjectionMaskErrorTurns = effective.ProjectionMaskErrorTurns,
            // The same redactor masks the model-facing text for ACP runs, which always have a session.
            ToolOutputMask = redaction.Redact,
            SubagentLimits = effective.Subagents,
            ThinkingLevel = effective.Thinking,
            DecisionMode = effective.Decisions.Approvals,
            ApprovalsThreshold = effective.Decisions.ApprovalsThreshold,
            DecideTool = effective.Decisions.Tool,
            UsageModels = usageModels,
            Approver = context.ClientApprover is { } clientApprover
                ? new AcpApprover(effective.Approval, clientApprover)
                : new NonInteractiveApprover(effective.Approval, yolo: false),
            Hooks = host?.Hooks,
            Session = session,
            FileChanges = (IFileChangeSink?)host?.FileChanges ?? NullFileChangeSink.Instance,
            Model = model,
            ModelCatalog = catalog,
            ModelRegistry = host?.ModelRegistry,
            ModelId = model.Id,
            WorkspaceTools = HarnessFactory.CreateWorkspaceTools(
                root => host?.ComposeTools(new Workspace(root)) ?? [],
                [.. inheritedTools.Where(tool => host?.IsWorkspaceBoundTool(tool) != true)],
                log: host?.Log,
                extraTools: effective.Tools.Extra,
                redaction: redaction,
                writeVerifiers: host?.WriteVerifiers,
                verificationMode: effective.Verify,
                refusalTracker: refusalTracker,
                bashGuardEnabled: effective.BashGuard
            ),
        };

        return new AgentHarness(clientFactory.Create(model), tools, options);

        (AuthStore, IReadOnlyList<ProviderEntry>) LoadSources()
        {
            AuthStore auth = AuthStore.Load(
                authPath,
                static name => Environment.GetEnvironmentVariable(name)
            );
            ProviderStore providers = ProviderStore.Load(
                AuthStore.ProviderStoreFilePath(authPath),
                auth,
                effective.Providers
            );
            return (auth, providers.Entries);
        }

        // The decide tool is declared to the model only when it will be registered: the harness
        // constructor adds it when the decision profile resolved and the setting asked for it.
        IReadOnlyList<string> ToolNames()
        {
            List<string> names = [.. tools.Tools.Select(tool => tool.Name)];
            if (decisionModel is not null && effective.Decisions.Tool && !names.Contains("decide"))
            {
                names.Add("decide");
            }

            return names;
        }
    }
}
