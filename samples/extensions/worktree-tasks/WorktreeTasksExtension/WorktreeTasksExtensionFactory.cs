using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace WorktreeTasksExtension;

/// <summary>
/// The one public factory the loader discovers in the entry assembly. It registers the four
/// worktree tools (model-facing <c>worktree__start</c>, <c>worktree__merge</c>,
/// <c>worktree__clean</c> and <c>worktree__list</c>) over the <c>core/process</c> service and the
/// per-run subagent seam. Registration is registration only: nothing runs until the model calls a
/// tool; the tool annotations drive the approval ask (start and clean are destructive-capable,
/// merge is a write, list is read-only).
/// </summary>
public sealed class WorktreeTasksExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.TryGetCoreService<IProcessRunner>("core/process", out IProcessRunner? runner);
        var git = new GitProbe(runner);
        var state = new WorktreeTasksState();

        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = "start",
                Description =
                    "Creates a git worktree and branch (lunate/<name>) and runs one subagent inside it with the coding and C# tools; returns the subagent's summary and the worktree facts",
                ParametersSchema = WorktreeToolSchemas.Start,
                Annotations = new ToolAnnotations(ReadOnly: false, Destructive: true),
                Risk = ToolRisk.Execute,
                Handler = new WorktreeStartHandler(state, git),
            }
        );
        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = "merge",
                Description =
                    "Merges a worktree branch back into the current branch (--no-ff); refuses a dirty target worktree and aborts conflicts, naming the conflicted files",
                ParametersSchema = WorktreeToolSchemas.Merge,
                Annotations = new ToolAnnotations(ReadOnly: false),
                Handler = new WorktreeMergeHandler(git),
            }
        );
        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = "clean",
                Description =
                    "Removes a worktree and deletes its branch when merged (or with force); refuses while a run is active in the worktree",
                ParametersSchema = WorktreeToolSchemas.Clean,
                Annotations = new ToolAnnotations(ReadOnly: false, Destructive: true),
                Risk = ToolRisk.Execute,
                Handler = new WorktreeCleanHandler(state, git),
            }
        );
        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = "list",
                Description = "Lists the repository's git worktrees with their branches and paths",
                ParametersSchema = WorktreeToolSchemas.List,
                Annotations = new ToolAnnotations(ReadOnly: true),
                Risk = ToolRisk.ReadOnly,
                Handler = new WorktreeListHandler(state, git),
            }
        );

        return new WorktreeTasksExtension(context.Id);
    }
}

/// <summary>The extension instance.</summary>
public sealed class WorktreeTasksExtension(string id) : IExtension
{
    public string Id { get; } = id;
}
