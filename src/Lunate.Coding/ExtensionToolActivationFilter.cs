using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The one shared activation filter of the composition (design §3): per run root, the extension
/// tools down to the active ones. Every composition site that adds extension tools into a session
/// tool set runs its list through this helper once — so an inactive tool never enters the tool
/// registry, the model declarations, a nested workspace's tool set or <see cref="ToolContext"
/// />'s tool stubs. A verdict is evaluated once per tool per composition; a mid-session workspace
/// change waits for the next composition. Extension tools keep registration order and come before
/// the MCP tools (design D7); the sites add the filtered list to their registry and their inherited
/// snapshot together, so the two can never disagree.
/// </summary>
internal static class ExtensionToolActivationFilter
{
    internal static IReadOnlyList<ITool> ActiveOnly(IEnumerable<ITool> tools, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(workspace);
        string root = workspace.WorktreeRoot;
        // An adapter carries the manifest's toolActivation rule (null = no rule = always active);
        // the evaluator takes the resolved git common dir, so the git condition reads this run's
        // host-side workspace, not a re-detection.
        string? gitCommonDir = workspace.GitCommonDir;

        List<ITool> active = [];
        foreach (ITool tool in tools)
        {
            if (
                tool is ExtensionToolAdapter adapter
                && !ToolActivationEvaluator.Evaluate(
                    adapter.ActivationRule,
                    root,
                    gitCommonDir
                )
            )
            {
                continue;
            }

            active.Add(tool);
        }

        return active;
    }
}
