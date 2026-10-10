using Lunate.Agent;
using Lunate.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// Composition shared by the coding frontends: the workspace tool set, the session file, the model
/// resolution and the client factory. Both print mode and the interactive session build the same
/// harness around these.
/// </summary>
internal static class HarnessFactory
{
    internal static ToolRegistry CreateTools(Workspace workspace)
    {
        var tools = new ToolRegistry();
        tools.Add(new ReadTool(workspace));
        tools.Add(new WriteTool(workspace));
        tools.Add(new EditTool(workspace));
        tools.Add(new BashTool(workspace));
        return tools;
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
