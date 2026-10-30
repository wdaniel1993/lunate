# subagents

A first-party Lunate extension that gives the model one tool, `subagents__spawn`, for delegating a
focused subtask to a child agent: the model writes a prompt, Lunate runs it as a separate agent run
in a child session beside the current one, and the child's final answer comes back to the parent
model as the tool result.

The child runs on the same client, workspace, hooks, approver and file-change sink as the parent. Its
events are forwarded onto the parent's stream tagged with the parent run, its usage rolls up into the
session, and its child session file is linked beside the parent's. Core enforces the budgets: a child
cannot spawn a further child, tree-wide concurrency is bounded, and each — and every — child run
carries a token budget.

## What it looks like

The model calls:

```json
{"prompt": "Find every use of CultureInfo.CurrentCulture in this repo and list file, line and call", "maxSteps": 8}
```

and reads back the child agent's answer. Because spawning is not a read-only operation, the approval
policy asks before a spawn runs; the prompt labels it `subagent (depth 1)`, and saying yes at that
prompt covers the child's work, its tool calls ask for approval like any other tool call.

Cancelling the parent run cancels the child and joins it. A refused spawn is not an error tone: one
of `depth_limit`, `concurrency_limit` or `token_limit` comes back as the tool result so the model can
adjust (an over-budget child returns its partial result instead of nothing).

## Install

```sh
lunate install Lunate.Extensions.Subagents
```

The package carries the extension's build output, `extension.json` and its `lunate.package.json`;
content lands in `~/.lunate/extensions/subagents/` with an install record beside it, and the
extension stays pending until you answer its first-load question — installing is not trusting.
`lunate packages --verify` re-hashes the record, and `lunate uninstall Lunate.Extensions.Subagents`
removes exactly what the install wrote.

Without a feed, copy the extension's output directory (`SubagentsExtension.dll`, `extension.json`,
its dependencies) under `<worktree>/.lunate/extensions/subagents/` and trust it on first load.
Nothing runs until the model calls `subagents__spawn`; the call asks for approval.

## Configuration

Settings live in `~/.lunate/extensions-settings/subagents.json` (inside your Lunate home); both are
optional:

| Setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| `defaultMaxSteps` | integer ≥ 1 | unset | The step ceiling the spawn tool passes when the model omits `maxSteps`; the run's own ceiling still clamps it (a per-spawn value only tightens). |
| `description` | string | the built-in text | Overrides the tool description the model reads. |

See `extension.json`'s `settingsSchema` for the enforced shape.

## Support

Officially supported for bugs and API compatibility at the `apiVersion` the manifest declares
(`^1.7.0`) — see [`extensions/`](../README.md) for what the tier promises. Report issues on the
repository; the platform seam and its tests are documented in
[`docs/extensions/contributions/tools.md`](../docs/extensions/contributions/tools.md).
