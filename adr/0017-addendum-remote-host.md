# 0017 addendum — remote extension host

- Status: proposed (2026-10-17)
- Relates to: ADR-0017 ("Out-of-process (constraint now, build later)"), OpenSpec change `remote-extension-host`
- Amends: ADR-0017

## Decision

`Lunate.Extensibility.Remote` implements the deferred out-of-process host:

- **Wire**: newline-delimited JSON-RPC 2.0 on the child's stdio, matching the ACP conventions already in the tree; protocol version v1, pinned (compared for equality at `initialize`, not negotiated). Methods: `initialize`, `hook/<name>`, `service/<name>/<method>` (child → host), `service-call` (host → child), `shutdown`, plus `$/cancel` notifications.
- **Transport**: the core process capability (`IProcessRunner` + `LineHandler`) — one long-lived child per remote extension, no run timeout, per-call timeouts on the RPC layer, tree kills via the existing children-first path. No new packages: the remaining JSON-RPC surface (ids, errors, notifications, framing) is small and pinned by the conformance suite; external JSON-RPC SDKs exist for external protocols (ACP, MCP — ADR-0020), not for Lunate's own hook bridge.
- **Manifest**: `runtime` (command, args, working directory, environment, call timeout) mirroring the MCP server registration shape; exactly one of `entryAssembly` / `runtime`.
- **Lifecycle**: lazy start on first use; crash → failed status `the extension process exited`, session continues; session end → `shutdown`, a bounded grace, then a tree kill; the child's environment is exactly the declared allow-list.
- **Posture**: the boundary is runtime isolation (dependencies, language, crash containment), not a permission boundary; the trust model is ADR-0017's, unchanged.

## Consequences

- Extension authors in any language can implement the wire contract; first-party SDKs are later changes.
- The hook catalogue's JSON-conformance constraint (ADR-0017) now has a wire-level conformance suite as its enforcement point.
- Tools, renderers, widgets, MCP registrations and model providers remain in-process; the design keeps their DTO families shaped for a later crossing.
