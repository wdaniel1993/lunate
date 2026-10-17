# Design — remote extension host

Status: **approved with adjustments (2026-10-17)** — the maintainer's adjustments are folded below and are part of the design; apply follows this document.

## Context

ADR-0017 fixed the JSON boundary and deferred the host. Everything needed on the Lunate side exists:

- `HookContractTests.Every_hook_dto_round_trips_through_json` pins STJ round-trips for the hook payloads today (17 payload records + the result families in `HookPayloads.cs` / `HookResults.cs`).
- `IProcessRunner` (`core/process`) already runs an interactive child: `LineHandler` receives every stdout line in order and a non-null return is written back to stdin as one flushed line; cancellation and timeouts kill the process tree children-first (the `PosixProcessTree` pattern); `ProcessRunRequest.Environment` supports an explicit allow-list.
- The extension store (discovery, manifest-only reads, trust with per-repo content hash, lazy load, failed-extension containment) is scope-aware and shaped for a second runtime kind.
- `McpServerRegistration` already models "a child process declared by configuration" — command, args, environment, working directory, call timeout — the shape a remote extension's manifest mirrors.
- The code-mode sample (T-48) already scrubs a child environment for its runtime.

## Protocol contract (pinned — the wire is a public contract)

- **Framing**: newline-delimited JSON-RPC 2.0 (one compact JSON object per line), UTF-8, no BOM, **no batches** (a batch is refused). **Stdout is protocol-only: a non-JSON line on stdout is a protocol error.** Stderr is captured into the extension log, capped. **Maximum line size 16 MB — an oversized line is a protocol error, never an OOM.**
- **`initialize`** (host → child, first call): `protocolVersion` + capabilities, the manifest echo, the workspace info, and the declared hooks and services. **A version mismatch refuses the extension with a clear status — never best effort.**
- **Bidirectional and reentrant**: the host may call a hook while the child is calling host services. **Separate id spaces per direction**; concurrent requests are allowed; responses are matched per direction. A test pins the reentrant case (the child calls a host service from inside a hook handler).
- **Cancellation**: `$/cancelRequest` notifications, mapped to `CancellationToken` in both directions.
- **Methods (v1)**: `initialize`; `hook/<name>` — a request for interceptor hooks, a notification for observer hooks; `service/<name>/<method>` (child → host); `service-call` (host → child, for services the remote extension registered); `shutdown`; `$/cancelRequest`.
- **JSON Schema generated from the hook and service DTOs**, published with the docs and shipped in `Lunate.Extensibility.Testing` — non-C# authors never reverse-engineer C#.

## Hook forwarding (pinned)

- The host forwards **only the hooks the manifest subscribes to**.
- **Observers are notifications (fire-and-forget)**: `SessionStarted`, `SessionEnding`, `ProviderStreamEvent`, `RunSettled`, `ModelChanged`, `ToolsChanged`, `FileChanged`. High-frequency stream deltas (`ProviderStreamEvent`) are not forwarded unless explicitly subscribed. **Observer timeout or failure: drop + warn.**
- **Interceptors are requests**: `ProjectTrust`, `InputReceived`, `RunStarting`, `ContextBuilding`, `MessageCompleted`, `ToolCalling`, `ToolResultReady`, `TurnEnded`, `Compacting`. **Timeout = "no opinion" — the hook's existing keep-original / skip semantics — plus a warning, except the gating hooks: `ToolCalling` blocks fail-safe and `ProjectTrust` denies** (the catalogue's declared failure policies, applied identically to remote timeouts and errors).
- **Services registered by a remote extension are async-only** — in-process consumers must not hit them on a synchronous hot path.

## Manifest (pinned)

- `runtime` in the MCP server registration shape, plus the **environment split**: `env` (values to set) and `envPassthrough` (names inherited from the parent). The child receives a **minimal base set — PATH, HOME/USERPROFILE, SYSTEMROOT, TEMP/TMP, LANG — plus `envPassthrough` plus `env`, nothing else**. The scrubber is the **code-mode sample's (T-48) implementation, promoted into the contract assembly — one implementation; the sample switches to it**.
- **A relative `command` or args path resolves against the extension directory; the default working directory is the extension directory.** The workspace root and run ids arrive through `initialize` and the hook DTOs.
- The manifest's hook and service declarations are what make **lazy start** possible: the host builds proxies without starting the child. **The `initialize` result must match the manifest: undeclared → refused; declared-but-missing → warned** (the in-process rules, unchanged).

## Lifecycle (pinned)

- Lazy by default; an optional manifest startup mode **`background`** starts the child **after the TUI is ready — never on the startup path; the calibrated startup budget (`BUDGET_MS`) is untouched**.
- **Crash**: in-flight calls fail immediately (no waiting for timeouts) and the user sees one notice; **at most one automatic restart per session, then the extension reads failed; a manual restart is available from `/extensions`**.
- **One child per session**, shared across subagent and worktree runs; **every call carries RunId/ParentRunId/workspace**.
- Session end: `shutdown`, a bounded grace, then the tree kill — **Job Object on Windows, the children-first POSIX walk on Unix — with a test on all three CI legs** (the existing process-capability kill path, reused).

## Trust (pinned)

- The trust prompt text says the extension **runs with the user's privileges**.
- **Digest: for remote extensions, trust hashes the manifest plus the extension directory contents** (entry script and bundled files) — swapping `index.js` after trust re-prompts. The same principle as hashing the in-process assembly.

## Decisions (approved with adjustments, 2026-10-17)

1. **Transport**: hand-rolled NDJSON JSON-RPC 2.0 on the core process capability; **no new packages**. The remaining surface is ids, errors, notifications and framing — pinned by the conformance suite, mirroring the ACP conventions. (`StreamJsonRpc` stays the alternative if the protocol ever outgrows this; it would need an ADR.)
2. **Manifest**: `runtime` in the MCP server registration shape with the env split; `entryAssembly` optional; exactly one of the two required.
3. **Surface v1**: all catalogue hooks (with the forwarding rules above) + service consumption + service registration. Remote tools are MCP (the existing provider path); tools, renderers, widgets, MCP registrations and model providers stay in-process, shaped for later.
4. **Lifecycle**: as pinned above (lazy/background, crash fail-fast + one restart + manual restart, one child per session, per-call run ids, the reused kill path).
5. **Posture**: isolation, not a sandbox — the trust prompt says the extension runs with the user's privileges; the digest covers manifest + directory contents.

## Testing

- **Wire conformance** (the card's acceptance): every hook payload and result DTO and the service-call envelope round-trips over the real wire — serialized, framed, parsed and compared by the peer — extending the `HookContractTests` inventory so a new DTO cannot ship without a wire entry.
- **A conformance runner in `Lunate.Extensibility.Testing`** that can be pointed at any remote extension executable.
- **The node reference extension in the fitness suite**: a `samples/extensions/` member with its test project, its tests running in CI like every sample.
- **A production composition test**: a remote extension loaded through the real host — hook delivered, service call answered (no "spec'd but not wired").
- **Lifecycle tests**: lazy vs background start (never on the startup path); handshake mismatch refusal; reentrancy (service call from inside a hook handler); crash fail-fast + one automatic restart + manual restart from `/extensions`; session-end grace then kill, on all three CI legs; environment (base set + passthrough + env; a parent key absent).
- **Failure-policy tests**: observer drop + warn; interceptor no-opinion + warn; `ToolCalling` fail-closed; `ProjectTrust` deny; non-JSON stdout line and oversized line as protocol errors.

## Tasks

See `tasks.md`.
