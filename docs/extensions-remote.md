# Remote extensions (out-of-process)

Lunate hosts an extension whose manifest declares a `runtime` as a child process instead of an
in-process assembly load, speaking newline-delimited JSON-RPC 2.0 over stdio. The full JSON
Schema of every payload and result is checked in at
[`extensions-remote.schema.json`](extensions-remote.schema.json) and is generated from the same
DTOs the host uses (the generator ships in `Lunate.Extensibility.Testing`, together with
`RemoteJsonSchema.Build()` and the conformance runner below). Background on the decision: [ADR-0017](../adr/0017-extensibility-architecture.md) and its
[remote host addendum](../adr/0017-addendum-remote-host.md).

## Manifest

Exactly one of `entryAssembly` (in-process) or `runtime` (out-of-process) is required.

```json
{
    "id": "node-remote",
    "version": "0.1.0",
    "apiVersion": "^1.0.0",
    "runtime": {
        "command": "node",
        "args": ["index.js"],
        "workingDirectory": ".",
        "env": { "MY_TOKEN": "literal" },
        "envPassthrough": ["HOME"],
        "callTimeout": 60,
        "startup": "background"
    },
    "hooks": ["tool-calling"],
    "services": ["echo"]
}
```

- `command` / `args` / `workingDirectory` — a relative path-like token (starting with `.` or
  containing a separator) resolves against the extension directory; a bare command resolves on
  `PATH`. The default working directory is the extension directory.
- `env` / `envPassthrough` — the child's whole environment is the minimal base set (`PATH`, `HOME`,
  `USERPROFILE`, `SystemRoot`, `TEMP`, `TMP`, `LANG`) plus the pass-through names plus the `env`
  values. Nothing else is inherited; Lunate's provider keys never leak.
- `callTimeout` — the per-call timeout in seconds for host → child requests (default 60).
- `startup` — `background` starts the process after the interactive UI is ready; omitted starts it
  lazily on the extension's first use (a forwarded hook or a service call).
- `hooks` / `services` — declaration names (kebab-case, e.g. `tool-calling`); the host builds
  proxies from them without starting anything. The `initialize` result must match: a hook or
  service the child implements but the manifest does not declare refuses the extension;
  a declared one the child does not implement only warns and is dropped.

The trust prompt states that the extension runs with the user's privileges; the trust digest hashes
the manifest together with the extension directory contents, so changing `index.js` after trust
re-prompts. The process boundary is runtime isolation (dependencies, language, crash containment),
not a permission boundary.

## Wire

One compact JSON object per line, UTF-8, no BOM, no batches. **Stdout is protocol-only** — a
non-JSON line or a line over 16 MB is a protocol error and stops the extension. stderr is captured
into the extension log, capped at 64 KB.

Protocol version 1. The host sends `initialize` first, with
`{ protocolVersion, capabilities, manifest, workspace: { worktreeRoot, repoRoot, gitCommonDir }, hooks, services }`;
the child answers `{ protocolVersion, hooks, services }`. A version mismatch or an undeclared
surface refuses the extension; the session continues without it.

Methods:

| Direction | Method | Shape |
| --- | --- | --- |
| host → child | `initialize` | request; result `{ protocolVersion, hooks, services }` |
| host → child | `hook/<Name>` | request for interceptor hooks, notification for observer hooks |
| child → host | `service/<name>/<method>` | request; params are the method's JSON arguments |
| host → child | `service-call` | request; params `{ service, method, arguments }` |
| either | `$/cancelRequest` | notification; params `{ id }` — maps to the handler's `CancellationToken` |
| host → child | `shutdown` | request; sent at session end, then a bounded grace, then the tree is killed |

Separate id spaces per direction; concurrent and reentrant calls are allowed (a child may call a
host service while a hook request is in flight). A handler exception on either side becomes a
JSON-RPC error result, never a crash.

### Hook calls

`hook/<Name>` params: `{ call: { runId, parentRunId, workspace }, payload: <the payload DTO> }`.
`runId`/`parentRunId` are null outside a run (project trust, session lifecycle, file changes).

Interceptor hooks (`ProjectTrust`, `InputReceived`, `RunStarting`, `ContextBuilding`,
`MessageCompleted`, `ToolCalling`, `ToolResultReady`, `TurnEnded`, `Compacting`) are requests whose
result is one object with an `outcome` discriminator (or the concrete result DTO for
`ContextBuilding` and `ToolResultReady`):

- `ProjectTrust`: `{"outcome":"allow"}` | `{"outcome":"deny","reason":"…"}`
- `InputReceived`: `passThrough` | `transform` + `text` | `consume`
- `RunStarting`: `none` | `apply` + `sectionEdits` + `activeTools`
- `MessageCompleted`: `keep` | `replace` + `text`
- `ToolCalling`: `proceed` + optional `arguments` | `block` + `reason`
- `TurnEnded`: `none` | `turnEnded` + `entries` + `requestContinuation`
- `Compacting`: `useDefault` | `provide` + `summary`
- `ContextBuilding`: `{ "addedMessages": [...] }`
- `ToolResultReady`: `{ "output": "...", "data": …, "structuredContent": … }`

Observer hooks (`SessionStarted`, `SessionEnding`, `ProviderStreamEvent`, `RunSettled`,
`ModelChanged`, `ToolsChanged`, `FileChanged`) are notifications — no answer is awaited.
`ProviderStreamEvent` is only forwarded when explicitly declared.

A hook's recorded time follows its call shape: an interceptor's includes the child-process round
trip; an observer's is the host-side send time, with no round trip implied. The lazy start and
`initialize` handshake are excluded from the first dispatch's hook time and reported separately as
the extension's start duration (`start <ms> ms` in `/extensions`, `startMs` in `--json`).

A timeout or failure follows the hook catalogue's failure policy: observers drop with a warning,
interceptors keep the original (no opinion) with a warning, except the gating hooks —
`ToolCalling` blocks fail-safe and `ProjectTrust` denies.

### Services

The child consumes host services by name; a service must implement `IJsonService` (and
`IBackgroundService` to register) — calls are async-only. `core/workspace` answers `get` with the
workspace info. The host calls a service the child registered with `service-call`; on the host side
that service appears in the registry as an `IBackgroundService` + `IJsonService` named after the
manifest declaration.

### Lifecycle

One child serves the whole session (subagent and worktree runs included). A process that exits
fails in-flight calls immediately, produces one notice, flips the extension to failed and attempts
at most one automatic restart per session; after that `/extensions restart <id>` restarts it
manually. Session end sends `shutdown`, waits the grace (5 s) and kills the process tree
children-first.

## Checking an extension

`RemoteConformanceRunner` in `Lunate.Extensibility.Testing` points at any executable that speaks
the wire and performs start, handshake, a hook round-trip, a service call in each direction and
shutdown, reporting each step:

```csharp
RemoteConformanceReport report = await RemoteConformanceRunner.RunAsync(
    new RemoteConformanceOptions
    {
        FileName = "node",
        Arguments = ["index.js"],
        WorkingDirectory = extensionDirectory,
    },
    cancellationToken
);
Assert.True(report.Succeeded, report.Summary);
```

The [node-remote sample](../samples/extensions/node-remote) is the reference implementation; its
fitness tests run in CI.
