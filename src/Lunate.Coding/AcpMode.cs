using Lunate.Agent;
using Lunate.Ai;
using Lunate.Protocols.Acp;

namespace Lunate.Coding;

/// <summary>
/// ACP mode: <c>lunate --acp</c> serves the Agent Client Protocol over stdio so editors drive
/// Lunate as their agent. Nothing but protocol frames may reach stdout; every diagnostic goes to
/// stderr. Approvals and the editor file system follow in T-28; v1 runs tools through the
/// configured approval policy without prompting.
/// </summary>
internal static class AcpMode
{
    internal static int Run(TextWriter errors, Stream input, Stream output, CancellationToken ct)
    {
        IAcpServer server = new LibAcpServer(line => errors.WriteLine($"lunate: {line}"));
        try
        {
            server.RunAsync(input, output, CreateHarness, ct).GetAwaiter().GetResult();
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
    }

    private static AgentHarness CreateHarness(string cwd)
    {
        AgentSettings settings = SettingsStore.Resolve(null, null);
        ModelCatalog catalog = ModelCatalog.Load(null);
        ModelInfo model = HarnessFactory.ResolveModel(settings.Model, catalog);
        IChatClientFactory factory = HarnessFactory.CreateFactory(null, null);

        var workspace = new Workspace(cwd);
        ToolRegistry tools = HarnessFactory.CreateTools(workspace);
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
            Approver = new NonInteractiveApprover(settings.Approval, yolo: false),
            Session = session,
            ModelCatalog = catalog,
            ModelId = model.Id,
        };

        return new AgentHarness(factory.Create(model), tools, options);
    }
}
