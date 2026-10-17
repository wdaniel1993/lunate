# 0017 addendum — remote extension host

- Status: accepted — 2026-10-17 (addendum to ADR 0017; remote-extension-host sign-off; T-52)
- Relates to: ADR-0017 ("Out-of-process (constraint now, build later)"), OpenSpec change `remote-extension-host`
- Amends: ADR-0017

## Decision

`Lunate.Extensibility.Remote` implements the deferred out-of-process host. The wire is a public contract:

- **Wire**: newline-delimited JSON-RPC 2.0 on the child's stdio (UTF-8, no BOM, no batches), matching the ACP conventions already in the tree; `protocolVersion` + capabilities at `initialize` (a mismatch refuses — never best effort). Methods: `initialize`, `hook/<name>` (requests for interceptor hooks, notifications for observer hooks), `service/<name>/<method>` (child → host), `service-call` (host → child), `shutdown`, plus `$/cancelRequest` mapped to `CancellationToken` both ways. Bidirectional and reentrant with separate id spaces per direction. Stdout is protocol-only (a non-JSON line is a protocol error; 16 MB line cap); stderr goes to the extension log, capped. A JSON Schema generated from the DTOs ships with the docs and `Lunate.Extensibility.Testing`.
- **Transport**: the core process capability (`IProcessRunner` + `LineHandler`) — one long-lived child per session, shared across subagent and worktree runs, every call carrying RunId/ParentRunId/workspace; no run timeout, per-call timeouts on the RPC layer; tree kills via the existing path (Job Object on Windows, children-first POSIX walk on Unix). No new packages: external JSON-RPC SDKs exist for external protocols (ACP, MCP — ADR-0020), not for Lunate's own hook bridge.
- **Manifest**: `runtime` (command, args, working directory, `env` + `envPassthrough`, call timeout) mirroring the MCP server registration shape; exactly one of `entryAssembly` / `runtime`; relative command/arg paths and the default cwd resolve against the extension directory; the declared hooks/services drive proxies without starting the child.
- **Lifecycle**: lazy start on first use, or `background` after the UI is ready — never on the startup path; crash → in-flight calls fail immediately, one notice, at most one automatic restart, then failed until a manual restart from `/extensions`; session end → `shutdown`, bounded grace, tree kill. The child's environment is the minimal base set plus `envPassthrough` plus `env` (the promoted code-mode scrubber — one implementation).
- **Failure semantics**: observers drop + warn; interceptors take no-opinion semantics + warn; gating hooks fail closed (`ToolCalling` blocks, `ProjectTrust` denies).
- **Trust**: the prompt states the extension runs with the user's privileges; the digest hashes the manifest plus the extension directory contents, so a swapped bundled file re-prompts.
- **Posture**: the boundary is runtime isolation (dependencies, language, crash containment), not a permission boundary.

## Consequences

- Extension authors in any language can implement the wire contract against the published JSON Schema; first-party SDKs are later changes.
- The hook catalogue's JSON-conformance constraint (ADR-0017) now has a wire-level conformance suite (plus a runner in `Lunate.Extensibility.Testing`) as its enforcement point, and the node reference extension joins the fitness suite.
- Tools, renderers, widgets, MCP registrations and model providers remain in-process; the design keeps their DTO families shaped for a later crossing.
