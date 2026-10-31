using System.Collections.Concurrent;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;
using Lunate.Roslyn.BuiltIn;

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
        CredentialRedactor? redaction = null,
        TimeSpan? bashChangeScanTimeout = null
    )
    {
        ToolRegistry tools = HarnessFactory.CreateTools(
            workspace,
            files,
            skillReadAllowance,
            log,
            extraTools,
            redaction,
            bashChangeScanTimeout: bashChangeScanTimeout
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
        if (
            !ToolActivationEvaluator.Evaluate(
                RoslynBuiltInFactory.Manifest.ToolActivation["*"],
                root
            )
        )
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

internal sealed class RecordingFileChangeHandler(ConcurrentQueue<FileChangedPayload> changes)
    : IFileChangedHandler
{
    public int Priority => -1;

    public ValueTask OnFileChangedAsync(
        FileChangedPayload payload,
        CancellationToken cancellationToken
    )
    {
        changes.Enqueue(payload);
        return ValueTask.CompletedTask;
    }
}
