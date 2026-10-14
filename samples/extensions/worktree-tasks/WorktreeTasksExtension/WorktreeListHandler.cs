using System.Text;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace WorktreeTasksExtension;

/// <summary>
/// The <c>worktree__list</c> handler: <c>git worktree list --porcelain</c> rendered compactly as
/// one line per worktree (branch, path, and whether a run is active in it).
/// </summary>
internal sealed class WorktreeListHandler(WorktreeTasksState state, GitProbe git)
    : IExtensionToolHandler
{
    public async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    )
    {
        if (!git.Available)
        {
            return new ToolResult(
                "list: the host did not provide the core/process service, so git cannot run. Update Lunate and retry.",
                IsError: true
            );
        }

        string root = Path.GetFullPath(context.WorkingDirectory);
        GitProbeResult listed = await git.RunAsync(root, ["worktree", "list", "--porcelain"], ct)
            .ConfigureAwait(false);
        if (!listed.Succeeded)
        {
            return new ToolResult(
                $"list: `git worktree list` failed: {listed.Detail}. Check that '{root}' is a git repository.",
                IsError: true
            );
        }

        IReadOnlyList<WorktreeLine> worktrees = Parse(listed.Stdout);
        var message = new StringBuilder();
        message
            .Append(worktrees.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .AppendLine(worktrees.Count == 1 ? " worktree:" : " worktrees:");
        foreach (WorktreeLine worktree in worktrees)
        {
            message.Append("  ").Append(worktree.Branch).Append("  ").Append(worktree.Path);
            if (state.IsActive(worktree.Path))
            {
                message.Append("  (active run)");
            }

            message.AppendLine();
        }

        return new ToolResult(message.ToString().TrimEnd(), IsError: false);
    }

    private static IReadOnlyList<WorktreeLine> Parse(string porcelain)
    {
        List<WorktreeLine> worktrees = [];
        string path = string.Empty;
        string branch = "(detached)";
        foreach (string line in porcelain.Split('\n'))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                path = line["worktree ".Length..].Trim();
                branch = "(detached)";
            }
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                string reference = line["branch ".Length..].Trim();
                branch = reference.StartsWith("refs/heads/", StringComparison.Ordinal)
                    ? reference["refs/heads/".Length..]
                    : reference;
            }
            else if (line.Length == 0 && path.Length > 0)
            {
                worktrees.Add(new WorktreeLine(path, branch));
                path = string.Empty;
            }
        }

        if (path.Length > 0)
        {
            worktrees.Add(new WorktreeLine(path, branch));
        }

        return worktrees;
    }

    private sealed record WorktreeLine(string Path, string Branch);
}
