using System.Text;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace WorktreeTasksExtension;

/// <summary>
/// The <c>worktree__start</c> handler: it validates the model's arguments, creates the worktree and
/// branch (never reusing an existing branch), then runs one subagent in that worktree through the
/// per-run subagent seam. The child gets a fixed allow-list — the coding tools plus the C# tools
/// when the worktree looks like a C# workspace — so it can edit, build and test without reaching
/// the extension tools again. Refusals and failures arrive as teaching error results.
/// </summary>
internal sealed class WorktreeStartHandler(WorktreeTasksState state, GitProbe git)
    : IExtensionToolHandler
{
    private static readonly string[] BaseTools = ["read", "write", "edit", "bash"];

    private static readonly string[] CSharpTools =
    [
        "cs_diagnostics",
        "cs_find_symbol",
        "cs_find_references",
        "cs_outline",
        "cs_rename",
    ];

    public async Task<ToolResult> InvokeAsync(
        JsonElement arguments,
        ToolContext context,
        CancellationToken ct
    )
    {
        if (
            !TryRead(
                arguments,
                out string name,
                out string task,
                out string baseRef,
                out string error
            )
        )
        {
            return Error(error);
        }

        if (context.Subagents is not { } runner)
        {
            return Error(
                "start: subagents are unavailable because this call is not running inside an agent run. Use the tool from a normal model turn."
            );
        }

        if (!git.Available)
        {
            return Error(
                "start: the host did not provide the core/process service, so git cannot run. Update Lunate and retry."
            );
        }

        string root = Path.GetFullPath(context.WorkingDirectory);
        string branch = WorktreeNames.Branch(name);
        string target = WorktreeNames.TargetPath(root, name);

        GitProbeResult format = await git.RunAsync(
                root,
                ["check-ref-format", "--branch", branch],
                ct
            )
            .ConfigureAwait(false);
        if (format.Failure is not null)
        {
            return Error($"start: {format.Failure}");
        }

        if (!format.Succeeded)
        {
            return Error(
                $"start: '{branch}' is not a valid git branch name. Pick a simpler name (letters, digits, '.', '-' and '_') and retry."
            );
        }

        GitProbeResult existing = await git.RunAsync(
                root,
                ["show-ref", "--verify", "--quiet", $"refs/heads/{branch}"],
                ct
            )
            .ConfigureAwait(false);
        if (existing.Failure is not null)
        {
            return Error($"start: {existing.Failure}");
        }

        if (existing.ExitCode == 0)
        {
            return Error(
                $"start: branch '{branch}' already exists. Pick another name; the tool never reuses or force-moves an existing branch."
            );
        }

        if (Directory.Exists(target))
        {
            return Error(
                $"start: the worktree path '{target}' already exists. Remove it first (worktree__clean) or pick another name."
            );
        }

        GitProbeResult add = await git.RunAsync(
                root,
                ["worktree", "add", "-b", branch, target, baseRef],
                ct
            )
            .ConfigureAwait(false);
        if (!add.Succeeded)
        {
            return Error(
                $"start: `git worktree add -b {branch} {target} {baseRef}` failed: {add.Detail}. Check the base ref and retry."
            );
        }

        SubagentResult result;
        state.MarkActive(target);
        try
        {
            result = await runner
                .RunAsync(
                    new SubagentRequest(
                        task,
                        AllowedTools: AllowList(target),
                        WorkspaceRoot: target
                    ),
                    ct
                )
                .ConfigureAwait(false);
        }
        finally
        {
            state.UnmarkActive(target);
        }

        string output =
            result.Output.Length > 0
                ? result.Output
                : $"The subagent finished without text (stop reason: {result.StopReason}).";
        var message = new StringBuilder(output);
        message
            .AppendLine()
            .AppendLine()
            .Append("worktree: ")
            .AppendLine(target)
            .Append("branch: ")
            .AppendLine(branch);
        return new ToolResult(message.ToString().TrimEnd(), result.IsError);
    }

    private static bool TryRead(
        JsonElement arguments,
        out string name,
        out string task,
        out string baseRef,
        out string error
    )
    {
        name = string.Empty;
        task = string.Empty;
        baseRef = "HEAD";
        error = string.Empty;
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            error = "start: arguments must be a JSON object. Fix the arguments and retry.";
            return false;
        }

        if (
            !arguments.TryGetProperty("name", out JsonElement nameValue)
            || nameValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(nameValue.GetString())
        )
        {
            error =
                "start: 'name' is required and must be a non-empty string naming the worktree. Retry with a name like 'feature-x'.";
            return false;
        }

        name = nameValue.GetString()!.Trim();
        if (!WorktreeNames.IsValid(name))
        {
            error =
                $"start: the worktree name '{name}' is not allowed; use letters, digits, '.', '-' and '_' (no '..' or path separators) and retry.";
            return false;
        }

        if (
            !arguments.TryGetProperty("task", out JsonElement taskValue)
            || taskValue.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(taskValue.GetString())
        )
        {
            error =
                "start: 'task' is required and must be a non-empty string: the prompt for the subagent in the new worktree. Retry with the task text.";
            return false;
        }

        task = taskValue.GetString()!.Trim();
        if (
            arguments.TryGetProperty("base", out JsonElement baseValue)
            && baseValue.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(baseValue.GetString())
        )
        {
            baseRef = baseValue.GetString()!.Trim();
            // argv has no shell, but git would still read a leading '-' as an option (for example
            // '--detach'); refuse before it reaches git.
            if (baseRef.StartsWith('-'))
            {
                error =
                    $"start: 'base' must name a git ref (branch, tag or commit), not an option: '{baseRef}' starts with '-'. Retry with the ref to branch from.";
                return false;
            }
        }

        return true;
    }

    /// <summary>The child's fixed allow-list: the coding tools, plus the C# tools when the worktree
    /// root looks like a C# workspace (the same cheap root check the composition uses).</summary>
    private static IReadOnlyList<string> AllowList(string worktreeRoot)
    {
        List<string> tools = [.. BaseTools];
        if (LooksLikeCSharp(worktreeRoot))
        {
            tools.AddRange(CSharpTools);
        }

        return tools;
    }

    private static bool LooksLikeCSharp(string worktreeRoot)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(worktreeRoot))
            {
                if (Path.GetExtension(file) is ".sln" or ".slnx" or ".csproj")
                {
                    return true;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // An unreadable root offers the base tools only.
        }

        return false;
    }

    private static ToolResult Error(string output) => new(output, IsError: true);
}
