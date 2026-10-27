# Sample: subagents

An extension that contributes one tool, `subagents__spawn`, which runs a child agent through
`ToolContext.Subagents` — the per-run subagent seam the core stamps into every tool context. The
child runs on the same client, workspace, hooks, approver and file-change sink as the parent, in a
child session linked beside the parent's; its events are forwarded onto the parent's stream with
`ParentRunId`, and the core enforces depth (fixed at 1), tree-wide concurrency and the per-child /
per-tree token budgets. The annotations are not read-only and destructive-capable, so the approval
policy asks before a spawn.

## What it proves (T-47, part 2)

| Capability | Where |
| --- | --- |
| Parent/child runs | `SpawnToolHandler` calls `ToolContext.Subagents.RunAsync`; the child's final text becomes the tool result the parent model reads. |
| Child session linking | The child session file sits beside the parent's and the parent carries the `AppendChildSession` link (asserted through the real loader in `SubagentsRunTests`). |
| Event forwarding | Child events reach the parent stream with `ParentRunId` and their own run/session ids; child usage rolls up. |
| Approvals | A child's ask goes through the parent's approver with the child's run id, the parent run id and depth 1; the interactive prompt labels it `subagent (depth 1)`. |
| Cancel | Cancelling the parent run cancels the child (`cancelled`), and the spawn call joins it. |
| Limits | A child cannot spawn (`depth_limit`); a spawn while the tree concurrency is held is refused (`concurrency_limit`); an over-budget child stops with `token_limit` and returns its partial result. |
| Hooks | Hook payloads fired during the child run carry the child's run id and the parent run id (the harness seam). |

## Layout

```text
samples/extensions/subagents/
  SubagentsExtension/            the extension: manifest, factory, settings, spawn handler
  SubagentsExtension.Tests/      kit-based tests, scripted model client, concurrency probe
  README.md
```

## Settings

- `defaultMaxSteps` (integer ≥ 1, optional): the step ceiling the tool passes when the model omits
  `maxSteps`; the run's own `MaxSteps` still clamps it (per-spawn values only tighten).
- `description` (string, optional): overrides the tool description the model reads.

## Build and test

```sh
dotnet build samples/extensions/subagents/SubagentsExtension/SubagentsExtension.csproj -c Release
dotnet test samples/extensions/subagents/SubagentsExtension.Tests/SubagentsExtension.Tests.csproj
```

The tests install the built extension into a temp store through `Lunate.Extensibility.Testing`'s
`ExtensionTestHost` (real manifest, real load context, real harness) and drive it with a scripted
model client — no live APIs.

## Install

The sample is published with every release, so installing it is one command:

```sh
lunate install Lunate.Samples.Subagents
```

The package carries this extension's build output, `extension.json` and its `lunate.package.json`;
the content lands in `~/.lunate/extensions/subagents/` with a record beside it, and the extension is
pending until you answer its first-load question — installing is not trusting. `lunate packages
--verify` re-hashes the record, and `lunate uninstall Lunate.Samples.Subagents` removes exactly what
the install wrote.

Without a feed, copy the extension's output directory (`SubagentsExtension.dll`, `extension.json`,
its dependencies) under `<worktree>/.lunate/extensions/subagents/` and trust it on first load,
exactly like the other samples. Nothing runs until the model calls `subagents__spawn`; the call asks
for approval.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `tools`).
2. Keep the handler shape: read `ToolContext.Subagents`, validate the model's arguments, call
   `RunAsync` and return the child's text — refusals already arrive as teaching error results.
3. Keep the tests: they prove the four behaviours (events/link/hooks, approvals, cancel, limits)
   through the real composition.

See `docs/spec/extensibility.md` for the subagent seam and `docs/guide.md` for T-47.
