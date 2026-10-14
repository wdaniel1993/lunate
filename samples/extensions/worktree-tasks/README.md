# Sample: worktree-tasks

Worktree management as extension territory: four tools that create a git worktree per task, run a
subagent inside it through `ToolContext.Subagents` and the `WorkspaceRoot` request field, merge the
result back, and clean up. All four are thin wrappers over the `core/process` service — no new
primitives, and worktree naming and paths are the sample's policy (the spec assigns worktree
management to extensions).

## Core capabilities proven

| Capability | Where |
| --- | --- |
| Child runs get their own workspace | `worktree__start` passes `WorkspaceRoot`; the child's working directory, tool set and session header follow that worktree (the child edits there, not in the parent). |
| Per-workspace services | The child's `cs_*` tools are bound to the child root through the per-workspace registry, so `cs_diagnostics` in the worktree loads its own Roslyn workspace. |
| Fixed child tool set | The spawn's allow-list is the coding tools plus the C# tools when the worktree looks like a C# workspace; extension tools and project MCP tools never reach the child. |
| Git through `core/process` | Every git call (worktree add, status, merge, worktree list, branch delete) runs through the host's process service with a timeout and captured output. |
| Refusals as teaching errors | Bad names, existing branches, dirty target worktrees, conflicting merges and active runs all return error results naming the problem and the fix; nothing throws. |
| Safe cleanup | `worktree__clean` refuses while a run is active in the worktree and deletes the branch only when it is merged (or with `force: true`). |

## Layout

```text
samples/extensions/worktree-tasks/
  WorktreeTasksExtension/        the extension: manifest, factory, git probe, one handler per tool
  WorktreeTasksExtension.Tests/  kit-based tests over real temp git repositories
  README.md
```

## Tools

- **`worktree__start`** (`name`, `task`, optional `base`): validates the name (letters, digits,
  `.`, `-`, `_`; no `..`, no path separators), validates the branch with
  `git check-ref-format --branch`, refuses an existing branch or path, creates the worktree at
  `<parentOfRoot>/<rootName>-<name>` on branch `lunate/<name>`, then runs one subagent in it and
  returns the child's summary plus the worktree path and branch.
- **`worktree__merge`** (`name`, optional `into`): refuses a dirty target worktree naming the
  changes, merges `lunate/<name>` with `--no-ff` into the current branch, and on conflict runs
  `git merge --abort` and returns an error naming the conflicted files — the tree is never left
  half-merged.
- **`worktree__clean`** (`name`, optional `force`): refuses while a run is active in the worktree,
  removes it with `git worktree remove` (a dirty checkout still refuses without `force`) and
  deletes the branch only when it is merged, or with `force: true`.
- **`worktree__list`**: renders `git worktree list --porcelain` as one line per worktree, marking
  worktrees with an active run.

## Build and test

```sh
dotnet build samples/extensions/worktree-tasks/WorktreeTasksExtension/WorktreeTasksExtension.csproj -c Release
dotnet test samples/extensions/worktree-tasks/WorktreeTasksExtension.Tests/WorktreeTasksExtension.Tests.csproj
```

The tests install the built extension into a temp store through `Lunate.Extensibility.Testing`'s
`ExtensionTestHost`, create real git repositories with `git init` and one commit through the kit's
`core/process` service, drive the tools with a scripted model client, and assert the child session
header, file isolation, merge/conflict/clean behaviour and the refusal messages.

## Install

Copy the extension's output directory (`WorktreeTasksExtension.dll`, `extension.json`, its
dependencies) under `<worktree>/.lunate/extensions/worktree/` and trust it on first load, exactly
like the other samples. Nothing runs until the model calls a tool; `start` and `clean` ask before
every call (destructive-capable), `merge` follows the write policy.

## Honest limits

- **Merges happen in the current worktree.** `into` must name the branch the session is already on;
  the tool never checks out another branch for you.
- **No conflict resolution.** A conflicting merge is aborted and reported; resolving is the
  model's job (for example with a dedicated conflict worktree).
- **The active-run guard covers this extension's own spawns** in this process: a worktree created
  outside the extension, or started in another process, is not tracked.
- **The name policy is a policy.** It is deliberately conservative; branch schemes that need `/`
  in names would extend `WorktreeNames`.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `tools`).
2. Keep the handler shape: validate arguments, run git through `core/process`, and return teaching
   errors — the loop turns them into error results the model can read.
3. Keep the tests: they create real temp repositories and prove the start/merge/clean/list
   behaviours end to end without live APIs.

See `docs/spec/extensibility.md` § Workspaces and repository identity for the workspace contract and
`docs/guide.md` for T-51.
