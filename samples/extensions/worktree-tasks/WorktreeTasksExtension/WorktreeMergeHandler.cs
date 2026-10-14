using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace WorktreeTasksExtension;

/// <summary>
/// The <c>worktree__merge</c> handler: it merges the worktree branch back with <c>--no-ff</c>,
/// refusing a dirty target worktree first. On conflict it aborts the merge and returns a teaching
/// error naming the conflicted files, so the tree is never left half-merged.
/// </summary>
internal sealed class WorktreeMergeHandler(GitProbe git) : IExtensionToolHandler
{
    private const int NamedFiles = 10;

    public async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    )
    {
        if (!TryRead(arguments, out string name, out string into, out string error))
        {
            return Error(error);
        }

        if (!git.Available)
        {
            return Error(
                "merge: the host did not provide the core/process service, so git cannot run. Update Lunate and retry."
            );
        }

        string root = Path.GetFullPath(context.WorkingDirectory);
        string branch = WorktreeNames.Branch(name);
        GitProbeResult exists = await git.RunAsync(
                root,
                ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"],
                ct
            )
            .ConfigureAwait(false);
        if (exists.Failure is not null)
        {
            return Error($"merge: {exists.Failure}");
        }

        if (!exists.Succeeded)
        {
            return Error(
                $"merge: no branch '{branch}' exists. Create the worktree with worktree__start first."
            );
        }

        GitProbeResult status = await git.RunAsync(root, ["status", "--porcelain"], ct)
            .ConfigureAwait(false);
        if (status.Failure is not null)
        {
            return Error($"merge: {status.Failure}");
        }

        if (!status.Succeeded)
        {
            return Error(
                $"merge: `git status` failed: {status.Detail}. Resolve the repository state and retry."
            );
        }

        if (status.Stdout.Trim().Length > 0)
        {
            return Error(
                $"merge: the target worktree has uncommitted changes ({Changes(status.Stdout)}). Commit or stash them, then retry."
            );
        }

        if (into != "HEAD")
        {
            GitProbeResult current = await git.RunAsync(
                    root,
                    ["rev-parse", "--abbrev-ref", "HEAD"],
                    ct
                )
                .ConfigureAwait(false);
            if (!current.Succeeded)
            {
                return Error($"merge: could not resolve the current branch: {current.Detail}");
            }

            string currentName = current.Stdout.Trim();
            if (!string.Equals(currentName, into, StringComparison.Ordinal))
            {
                return Error(
                    $"merge: 'into' names '{into}' but the target worktree is on '{currentName}'. Check out '{into}' first (or omit 'into' to merge into the current branch)."
                );
            }
        }

        GitProbeResult merge = await git.RunAsync(root, ["merge", "--no-ff", branch], ct)
            .ConfigureAwait(false);
        if (merge.Succeeded)
        {
            string summary = merge.Stdout.Trim();
            return new ToolResult(
                $"merged {branch} into the current branch"
                    + (summary.Length > 0 ? ":\n" + summary : "."),
                IsError: false
            );
        }

        // Only a merge that actually started can be rolled back; a failure before that (for
        // example unrelated histories) leaves the worktree untouched and gets git's own words.
        GitProbeResult inProgress = await git.RunAsync(
                root,
                ["rev-parse", "-q", "--verify", "MERGE_HEAD"],
                ct
            )
            .ConfigureAwait(false);
        if (!inProgress.Succeeded)
        {
            return Error(
                $"merge: merging '{branch}' failed: {merge.Detail}. No merge is in progress, so nothing is left half-merged. Inspect the branch and retry."
            );
        }

        GitProbeResult conflicts = await git.RunAsync(
                root,
                ["diff", "--name-only", "--diff-filter=U"],
                ct
            )
            .ConfigureAwait(false);
        GitProbeResult abort = await git.RunAsync(root, ["merge", "--abort"], ct)
            .ConfigureAwait(false);
        string files = conflicts.Succeeded ? conflicts.Stdout.Trim() : string.Empty;
        string named = files.Length > 0 ? $" Conflicted files: {List(files)}." : string.Empty;
        string aborted = abort.Succeeded
            ? " The merge was aborted; the target worktree is unchanged."
            : $" `git merge --abort` failed: {abort.Detail}. Run it manually to restore the target worktree.";
        return Error(
            $"merge: merging '{branch}' failed: {merge.Detail}.{named}{aborted} Resolve the conflict in a side branch or rebase and retry."
        );
    }

    private static bool TryRead(
        JsonElement arguments,
        out string name,
        out string into,
        out string error
    )
    {
        name = string.Empty;
        into = "HEAD";
        error = string.Empty;
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            error = "merge: arguments must be a JSON object. Fix the arguments and retry.";
            return false;
        }

        if (
            !arguments.TryGetProperty("name", out JsonElement nameValue)
            || nameValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameValue.GetString())
            || !WorktreeNames.IsValid(nameValue.GetString()!.Trim())
        )
        {
            error =
                "merge: 'name' is required and must be a valid worktree name (letters, digits, '.', '-' and '_'; no '..' or path separators). Retry.";
            return false;
        }

        name = nameValue.GetString()!.Trim();
        if (
            arguments.TryGetProperty("into", out JsonElement intoValue)
            && intoValue.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(intoValue.GetString())
        )
        {
            into = intoValue.GetString()!.Trim();
        }

        return true;
    }

    private static string Changes(string porcelain) =>
        List(
            string.Join(
                '\n',
                porcelain
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Length > 3 ? line[3..].Trim() : line.Trim())
            )
        );

    private static string List(string lines)
    {
        string[] entries = lines.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        string[] items = [.. entries.Take(NamedFiles)];
        string listed = string.Join(", ", items);
        return entries.Length > items.Length
            ? $"{listed} and {entries.Length - items.Length} more"
            : listed;
    }

    private static ToolResult Error(string output) => new(output, IsError: true);
}
