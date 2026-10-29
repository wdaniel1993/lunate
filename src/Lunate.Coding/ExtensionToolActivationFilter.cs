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
    internal static IReadOnlyList<ITool> ActiveOnly(
        IEnumerable<ITool> tools,
        Workspace workspace
    ) => Split(tools, workspace).Active;

    internal static (
        IReadOnlyList<ITool> Active,
        IReadOnlyList<ExtensionInactiveTool> Inactive
    ) Split(IEnumerable<ITool> tools, Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(workspace);
        string root = workspace.WorktreeRoot;
        // An adapter carries the manifest's toolActivation rule (null = no rule = always active);
        // the evaluator takes the resolved git common dir, so the git condition reads this run's
        // host-side workspace, not a re-detection.
        string? gitCommonDir = workspace.GitCommonDir;

        List<ITool> active = [];
        List<ExtensionInactiveTool> inactive = [];
        foreach (ITool tool in tools)
        {
            if (
                tool is ExtensionToolAdapter adapter
                && !ToolActivationEvaluator.Evaluate(adapter.ActivationRule, root, gitCommonDir)
            )
            {
                // Withheld at this root; the report names the condition and the reason.
                inactive.Add(Report(adapter));
                continue;
            }

            active.Add(tool);
        }

        return (active, inactive);
    }

    /// <summary>The withheld tool's view report: its condition and the reason the rule answered false.</summary>
    private static ExtensionInactiveTool Report(ExtensionToolAdapter adapter)
    {
        ExtensionToolActivationRule rule = adapter.ActivationRule!;
        string reason = rule.When switch
        {
            ExtensionToolActivationRule.WorkspaceRootHasFile => FormattableString.Invariant(
                $"no file matching {string.Join(", ", rule.Patterns.Select(pattern => $"'{pattern}'"))} at the workspace root"
            ),
            ExtensionToolActivationRule.WorkspaceIsGitRepository =>
                "the workspace root is not a git repository",
            // The manifest parser produces the closed condition set; an unknown condition never
            // activates a tool, and the report says so rather than inventing a behaviour.
            _ => "the condition is not one this host evaluates",
        };
        return new ExtensionInactiveTool(adapter.ExtensionId, adapter.Name, rule.When, reason);
    }
}

/// <summary>
/// One tool the activation filter withheld at the run root: the model-facing name, the manifest
/// condition, and the one-line reason (the <c>/extensions</c> view's
/// <c>inactive (&lt;condition&gt;): &lt;reason&gt;</c> report).
/// </summary>
internal sealed record ExtensionInactiveTool(
    string ExtensionId,
    string Tool,
    string Condition,
    string Reason
);
