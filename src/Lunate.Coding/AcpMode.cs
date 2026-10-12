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
        string? mcpConfigPath = null
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
        try
        {
            var workspace = new Workspace(Environment.CurrentDirectory);
            IExtensionLog log = extensions?.Log ?? new StderrExtensionLog(errors);

            // The config set composes first so a same-named extension registration is skipped
            // before listing (the configured server wins); nothing starts before listing.
            mcp = McpComposition
                .ComposeAsync(
                    workspace,
                    mcpConfigPath,
                    extensions,
                    new NonInteractiveMcpConfigTrustPrompt(),
                    log,
                    notice => interaction.Notify(notice),
                    ct
                )
                .GetAwaiter()
                .GetResult();
            if (extensions?.NoExtensions is not true)
            {
                host = ComposeExtensions(
                    extensions,
                    workspace,
                    errors,
                    McpNameCollisions.ConfiguredServers(mcp),
                    ct
                );
            }

            if (host is not null)
            {
                mcpTools.AddRange(host.McpServers.ListToolsAsync(ct).GetAwaiter().GetResult());
            }

            if (mcp is not null)
            {
                mcpTools.AddRange(mcp.ListToolsAsync(ct).GetAwaiter().GetResult());
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
                .RunAsync(input, output, context => CreateHarness(context, host, mcpTools), ct)
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
        IReadOnlyDictionary<string, string?>? configuredMcpServers,
        CancellationToken ct
    )
    {
        ExtensionSeams resolved = (extensions ?? new ExtensionSeams()) with
        {
            Log = extensions?.Log ?? new StderrExtensionLog(errors),
        };
        return ExtensionSession
            .StartAsync(workspace.Identity, resolved, ct, configuredMcpServers)
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

    private static AgentHarness CreateHarness(
        AcpSessionContext context,
        ExtensionSession? host,
        IReadOnlyList<ITool> mcpTools
    )
    {
        AgentSettings settings = SettingsStore.Resolve(null, null);
        ModelCatalog catalog = ModelCatalog.Load(null);
        ModelInfo model = HarnessFactory.ResolveModel(settings.Model, catalog);
        IChatClientFactory factory = HarnessFactory.CreateFactory(null, null);

        var workspace = new Workspace(context.Cwd);
        ToolRegistry tools = HarnessFactory.CreateTools(workspace, context.FileAccess);
        foreach (ITool tool in mcpTools)
        {
            tools.Add(tool);
        }

        Session session = HarnessFactory.CreateSession(
            HarnessFactory.DefaultSessionDirectory(workspace),
            workspace
        );
        var options = new AgentHarnessOptions
        {
            SystemPrompt = SystemPrompt.Compose(
                workspace,
                [.. tools.Tools.Select(tool => tool.Name)]
            ),
            WorkingDirectory = workspace.WorktreeRoot,
            ToolOutputLimit = settings.ToolOutputLimit,
            Approver = context.ClientApprover is { } clientApprover
                ? new AcpApprover(settings.Approval, clientApprover)
                : new NonInteractiveApprover(settings.Approval, yolo: false),
            Hooks = host?.Hooks,
            Session = session,
            ModelCatalog = catalog,
            ModelId = model.Id,
        };

        return new AgentHarness(factory.Create(model), tools, options);
    }
}
