# Design — remote extension host

Status: draft, 2026-10-17. **Sign-off requested on the five decisions below** (card: "Decision: remote extension host — design sign-off").

## Context

ADR-0017 fixed the JSON boundary and deferred the host. Everything needed on the Lunate side exists:

- `HookContractTests.Every_hook_dto_round_trips_through_json` pins STJ round-trips for the hook payloads today (17 payload records + the result families in `HookPayloads.cs` / `HookResults.cs`).
- `IProcessRunner` (`core/process`) already runs an interactive child: `LineHandler` receives every stdout line in order and a non-null return is written back to stdin as one flushed line; cancellation and timeouts kill the process tree children-first (the `PosixProcessTree` pattern); `ProcessRunRequest.Environment` supports an explicit allow-list.
- The extension store (discovery, manifest-only reads, trust with per-repo content hash, lazy load, failed-extension containment) is scope-aware and shaped for a second runtime kind.
- `McpServerRegistration` already models "a child process declared by configuration" — command, args, environment, working directory, call timeout — the shape a remote extension's manifest should mirror.

The remote host is a second `IExtension` producer next to the in-process loader: same discovery, same trust, same hook pipeline; the difference is that the implementation lives in a child process and every call crosses a JSON wire.

## Wire contract

- **Framing**: newline-delimited JSON-RPC 2.0 (one compact JSON object per line) on the child's stdio, matching the ACP conventions already in the repo (`Lunate.Protocols`). UTF-8, no embedded newlines (serializer escapes them), stdout is protocol-only — child logging goes to stderr, which the host captures into the extension log with a line prefix.
- **Transport**: the core process capability. One long-lived child per remote extension; `LineHandler` out, `ProcessRunRequest` return value in; no timeout on the run (the child lives for the session), per-call timeouts on the RPC layer.
- **Correlation**: monotonically increasing ids per direction; `result` / `error` (code, message, optional data); `$/cancel` notifications for cancelled calls; `notify` for fire-and-forget events. The host rejects an unknown or duplicate response id and logs it.
- **Methods (v1)**:
  - `initialize` (host → child, first call): protocol version, the manifest echo, the workspace info, the hook names the child declared, and the services the host can offer. Result: the child's accepted version and the hooks/services it actually implements. A mismatch fails the extension (logged, session continues).
  - `hook/<name>` (host → child): one call per hook dispatch, payload per the catalogue DTOs (`hook/sessionStarted`, `hook/toolCalling`, …). The result is the hook's result DTO (transformed text, block reasons, prompt-section edits — the declared semantics apply unchanged; the wire adds nothing to the policy).
  - `service/<name>/<method>` (child → host): service consumption. Arguments and result are JSON; the host resolves the service by name through the registry and maps "unknown service" to a teaching error result.
  - `service-call` (host → child): calls on services the remote extension registered (the host advertises them at initialize; calls route back over the same channel).
  - `shutdown` (host → child) at session end, then the process closes; a grace window, then a tree kill.
- **Failure mapping**: a JSON-RPC error, a timeout, or a dead process maps to the hook failure policy exactly like an in-process handler throwing: teaching error result, never a crashed session. Handler exceptions on either side become error results, never process death.

## Decisions (sign-off requested)

1. **Transport and protocol — recommendation: hand-rolled NDJSON JSON-RPC 2.0 on the core process capability; no new packages.** The repo rule ("no reason to write our own JSON-RPC", guide §Protocols) applies to ACP and MCP, where external SDKs exist and are adopted (ADR-0020). The hook bridge is Lunate's own protocol: no SDK speaks it, and the JSON-RPC part that remains — ids, errors, notifications, framing — is small, fully pinned by the conformance suite, and mirrors the ACP conventions already in the tree. Alternative: `StreamJsonRpc` (mature, Microsoft) — 2–3 new packages, needs an ADR ask, and would still need the DTO and lifecycle work. If you prefer it, say so and it becomes an ADR.
2. **Manifest — recommendation: `runtime` object mirroring the MCP server registration shape.** A remote extension replaces `entryAssembly` with `"runtime": {"command": […], "args": […], "workingDirectory": "…", "environment": {…}, "callTimeout": "…"}`; `entryAssembly` becomes optional; exactly one of the two is required; validation errors name the field. Rationale: one shape for "child process declared by configuration" in the whole product (MCP servers, remote extensions), and extension authors already know it.
3. **Boundary surface for v1 — recommendation: all catalogue hooks + service consumption + service registration.** Hooks are the acceptance bar (the conformance test); services cross both ways because the in-proc path is symmetric and the envelope is the same machinery. Tools, renderers, widgets, MCP registrations and model providers stay in-process for now — named in the spec as the shaped-for-later families.
4. **Lifecycle — recommendation: lazy start on first use; crash → failed status, session continues; session end → close, grace, tree kill; environment allow-list.** Start happens when the extension's first hook would fire (matching "registration is registration only" and the lazy MCP server behaviour); the spawn handshake gets the repo's 30 s window idiom; a crashed child flips to a failed status with the reason `the extension process exited` (MCP precedent) and its hooks are skipped with a logged reason; the child's environment is exactly what the manifest declares (no inheritance — provider keys cannot leak); the tree kill uses the existing runner path (never `Process.Kill(entireProcessTree)` on POSIX).
5. **Trust and posture — recommendation: unchanged trust flow; the boundary is isolation, not a sandbox.** A remote extension gets the same per-repository content-hash trust prompt (project scope) or is trusted by installation (global), identical to in-proc. The process boundary buys dependency isolation, polyglot extensions and crash containment — it is NOT a permission boundary, and the spec and docs say so (ADR-0017's trust model is unchanged).

## Testing

- **Wire conformance** (the card's acceptance): every hook payload and result DTO and the service-call envelope round-trips through a real wire — serialized by one side, framed, parsed by the other — extending the existing `HookContractTests` inventory so a new DTO cannot ship without a wire round-trip.
- **Scripted remote extension in CI**: a small script (node, already a CI dependency) that speaks the protocol — declares hooks in its manifest, answers `hook/toolCalling`, calls one service, registers one service the host calls back — driven through the real composition path (the production-composition-test recipe).
- **Lifecycle tests**: lazy start (no process before first use), handshake mismatch, crash mid-session (failed status + session continues), session end shutdown within the grace, tree kill after the grace, environment allow-list (a declared-only env — a key in the parent is absent in the child).
- **Failure-policy tests**: timeout, JSON-RPC error, malformed line — each maps to the hook's declared failure policy.

## Tasks (groups)

1. Wire layer (`Lunate.Extensibility.Remote`): framing, correlation, cancellation, error mapping + unit tests.
2. Remote extension adapter: manifest `runtime` parsing/validation, lazy process start, handshake, hook bridging, service bridging both ways, lifecycle (crash/end/kill), env allow-list + tests.
3. Integration into the loader/harness composition: discovery accepts both runtime kinds; failed remote extensions are contained like failed in-proc ones; `--no-extensions` unaffected + tests.
4. Conformance: wire round-trip of every DTO (extend the inventory), the scripted node reference extension end-to-end.
5. Spec deltas (`extensions`), ADR-0017 addendum (`adr/0017-addendum-remote-host.md`), docs (extension authoring page gains the remote manifest + wire reference), `PublicAPI` updates.

## Open questions folded into the five

- Should `initialize` negotiate a protocol version at all, or pin v1 exactly? (Recommendation: pin v1 — a single version string compared for equality; negotiation is a later change if a second consumer appears.)
- Should the child see the manifest it was installed with (echo) or is that redundant? (Recommendation: echo — the child validates it is running against the manifest the host read; cheap and catches drift.)
