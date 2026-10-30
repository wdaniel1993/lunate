using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;

namespace Lunate.Coding.Tests;

internal static class RoslynTestHarness
{
    internal static ToolRegistry CreateTools(
        Workspace workspace,
        WorkspaceServices services,
        ITextFileAccess? files = null,
        SkillReadAllowance? skillReadAllowance = null,
        IExtensionLog? log = null,
        IReadOnlyList<string>? extraTools = null,
        CredentialRedactor? redaction = null
    )
    {
        ToolRegistry tools = HarnessFactory.CreateTools(
            workspace,
            files,
            skillReadAllowance,
            log,
            extraTools,
            redaction
        );
        foreach (ITool tool in CSharpTools(services, workspace.WorktreeRoot))
        {
            tools.Add(tool);
        }

        return tools;
    }

    internal static Func<string, ToolRegistry> CreateWorkspaceTools(
        WorkspaceServices services,
        IReadOnlyList<ITool> inheritedTools,
        Func<string, IReadOnlyList<string>>? skillReadRoots = null,
        IExtensionLog? log = null,
        IReadOnlyList<string>? extraTools = null,
        CredentialRedactor? redaction = null
    ) =>
        HarnessFactory.CreateWorkspaceTools(
            root => CSharpTools(services, root),
            inheritedTools,
            skillReadRoots,
            log,
            extraTools,
            redaction
        );

    private static IReadOnlyList<ITool> CSharpTools(WorkspaceServices services, string root)
    {
        if (!ExtensionSession.RoslynIsActive(root))
        {
            return [];
        }

        Func<ICSharpBackend> backend = () => services.BackendFor(root);
        return
        [
            new CsDiagnosticsTool(backend),
            new CsFindSymbolTool(backend),
            new CsFindReferencesTool(backend),
            new CsOutlineTool(backend),
            new CsRenameTool(backend),
        ];
    }
}
