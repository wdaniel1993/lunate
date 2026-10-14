using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace WorktreeTasksExtension;

/// <summary>
/// The <c>worktree__clean</c> handler: it removes a worktree and deletes its branch, but only when
/// the branch is merged (or <c>force: true</c>). It refuses while a run is active in the worktree,
/// and the worktree removal itself still refuses a dirty checkout without force.
/// </summary>
internal sealed class WorktreeCleanHandler(WorktreeTasksState state, GitProbe git)
    : IExtensionToolHandler
{
    public async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    )
    {
        if (
            arguments.ValueKind != JsonValueKind.Object
            || !arguments.TryGetProperty("name", out JsonElement nameValue)
            || nameValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameValue.GetString())
            || !WorktreeNames.IsValid(nameValue.GetString()!.Trim())
        )
        {
            return Error(
                "clean: 'name' is required and must be a valid worktree name (letters, digits, '.', '-' and '_'; no '..' or path separators). Retry."
            );
        }

        string name = nameValue.GetString()!.Trim();
        bool force = false;
        if (
            arguments.TryGetProperty("force", out JsonElement forceValue)
            && forceValue.ValueKind is JsonValueKind.True or JsonValueKind.False
        )
        {
            force = forceValue.GetBoolean();
        }

        if (!git.Available)
        {
            return Error(
                "clean: the host did not provide the core/process service, so git cannot run. Update Lunate and retry."
            );
        }

        string root = Path.GetFullPath(context.WorkingDirectory);
        string branch = WorktreeNames.Branch(name);
        string target = WorktreeNames.TargetPath(root, name);
        if (state.IsActive(target))
        {
            return Error(
                $"clean: a run is active in '{target}'; wait for it to finish before cleaning the worktree."
            );
        }

        GitProbeResult branchExists = await git.RunAsync(
                root,
                ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"],
                ct
            )
            .ConfigureAwait(false);
        if (branchExists.Failure is not null)
        {
            return Error($"clean: {branchExists.Failure}");
        }

        bool hasBranch = branchExists.Succeeded;
        bool merged = false;
        if (hasBranch)
        {
            GitProbeResult ancestry = await git.RunAsync(
                    root,
                    ["merge-base", "--is-ancestor", branch, "HEAD"],
                    ct
                )
                .ConfigureAwait(false);
            if (ancestry.Failure is not null)
            {
                return Error($"clean: {ancestry.Failure}");
            }

            merged = ancestry.Succeeded;
            if (!merged && !force)
            {
                return Error(
                    $"clean: branch '{branch}' is not merged into HEAD; merge it first (worktree__merge) or pass force: true to delete it unmerged."
                );
            }
        }

        bool worktreeExists = Directory.Exists(target);
        if (worktreeExists)
        {
            List<string> removeArguments = ["worktree", "remove"];
            if (force)
            {
                removeArguments.Add("--force");
            }

            removeArguments.Add(target);
            GitProbeResult remove = await git.RunAsync(root, removeArguments, ct)
                .ConfigureAwait(false);
            if (!remove.Succeeded)
            {
                return Error(
                    $"clean: `git worktree remove {target}` failed: {remove.Detail}. Commit, stash or discard the changes, or pass force: true."
                );
            }
        }

        if (hasBranch)
        {
            GitProbeResult delete = await git.RunAsync(
                    root,
                    ["branch", force ? "-D" : "-d", branch],
                    ct
                )
                .ConfigureAwait(false);
            if (!delete.Succeeded)
            {
                return Error($"clean: deleting branch '{branch}' failed: {delete.Detail}");
            }
        }

        if (!worktreeExists && !hasBranch)
        {
            return Error(
                $"clean: no worktree '{target}' and no branch '{branch}' exist; nothing to clean."
            );
        }

        var message = new System.Text.StringBuilder();
        if (worktreeExists)
        {
            message.Append("removed worktree '").Append(target).Append('\'');
        }

        if (hasBranch)
        {
            if (worktreeExists)
            {
                message.Append(" and ");
            }

            message
                .Append(force && !merged ? "deleted unmerged branch '" : "deleted branch '")
                .Append(branch)
                .Append('\'');
        }

        message.Append('.');
        return new ToolResult(message.ToString(), IsError: false);
    }

    private static ToolResult Error(string output) => new(output, IsError: true);
}
