# permission-gate

A first-party Lunate extension that enforces a settings-driven policy on destructive tool calls
before the approval prompt: in `block` mode (the default) a destructive tool call is denied outright
— the tool never runs and the model reads why; in `confirm` mode the call falls through to the normal
approval prompt, where you decide each time.

Use it when you want a hard rule about destructive operations — for example on an unattended or
agent-only session — instead of relying on answering every prompt correctly.

## What it looks like

A `bash` call whose tool annotations mark it `destructive` is returned to the model as:

```text
permission-gate: destructive tool 'bash' blocked - set mode=confirm to approve interactively
```

`confirm` mode runs no policy of its own: the call reaches the approval prompt exactly as it would
without the extension.

## Behaviour

- The gate decides on the tool call's **annotations** (the resolved `read-only`, `destructive`,
  `idempotent`, `open-world` wire names), never on a hard-coded tool-name list, so a custom tool that
  declares itself destructive is gated like `bash`.
- Anything without a `destructive` annotation, and every non-tool situation, proceeds unchanged.
- The hook runs **before** the approver: in `block` mode the approval prompt is never reached for a
  blocked call.
- The handler logs its decisions through the extension's log.

## Install

```sh
lunate install Lunate.Extensions.PermissionGate
```

The package carries the extension's build output, `extension.json` and its `lunate.package.json`;
content lands in `~/.lunate/extensions/permission-gate/` with an install record beside it, and the
extension stays pending until you answer its first-load question — installing is not trusting.

Without a feed, copy the extension's output directory (`PermissionGateExtension.dll`,
`extension.json`, its dependencies) under `<worktree>/.lunate/extensions/permission-gate/` and trust
it on first load; nothing registers until the extension is trusted.

## Configuration

One setting, in `~/.lunate/extensions-settings/permission-gate.json`:

| Setting | Type | Default | Meaning |
| --- | --- | --- | --- |
| `mode` | `block` or `confirm` | `block` | `block` denies destructive tool calls before approval; `confirm` falls through to the approval prompt. |

See `extension.json`'s `settingsSchema` for the enforced shape.

## Support

Officially supported for bugs and API compatibility at the `apiVersion` the manifest declares — see
[`extensions/`](../README.md) for what the tier promises. Report issues on the repository; the
hook surface it builds on is documented in
[`docs/extensions/contributions/hooks.md`](../docs/extensions/contributions/hooks.md).
