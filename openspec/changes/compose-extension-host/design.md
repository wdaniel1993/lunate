# Design: compose the extension host in the CLI

## The shape

New `src/Lunate.Coding/ExtensionSession.cs` (internal, `IAsyncDisposable`) — the composed host:

- `static Task<ExtensionSession> StartAsync(string workingDirectory, string repositoryIdentity, ExtensionSeams seams, CancellationToken ct)`:
  1. builds `ExtensionLoader` with `ExtensionHostOptions { StorePath = seams.StorePath ?? default, Log = seams.Log ?? StderrExtensionLog }`;
  2. `Discover(workingDirectory, repositoryIdentity)` — manifests only;
  3. loads each discovered extension in discovery order through `Load(id, …, prompt)`, catching `ExtensionTrustDeniedException` / `ExtensionLoadException` per extension: log and continue (the hook-failure-policy spirit: never abort the session);
  4. builds `ExtensionMcpServers` from `loader.McpServers.List()` (registrations exist only after loads).
- Exposes: `Hooks` (`AgentHookAdapter` over the loader's runner), `ToolResultRenderers`, `StatusWidgets`, `ModelProviders`, `McpServers`, `Descriptors`, `Errors` (discovery + load, for logs and tests).
- `DisposeAsync`: `EndSessionAsync()` (session-ending hooks, background services stop) then MCP hosts dispose (the T-41 mapping's contract).

`ExtensionSeams` (Coding, internal record): `StorePath?`, `IExtensionTrustPrompt?`, `IExtensionLog?`. Production defaults: `~/.lunate` store, mode-specific prompt, stderr log. Tests inject a temp store + scripted prompt + recording log.

`StderrExtensionLog` (Coding, internal): `Warn`/`Error` → stderr with the `lunate: ` prefix; `Info` suppressed (it is extension-internal detail; a debug env var is a later seam).

## "First use" = session start (the reading, pinned)

The guide says "assemblies load on first use, never at startup" and "manifests are read at startup". This change pins the operational meaning:

- **CLI startup** (`--version`, `--help`, `--discover`, flag parsing, usage errors): zero extension work — no store read, no manifest parse, no load. The measured startup budget (`perf.sh` on `--version`) stays byte-identical.
- **Session start** (interactive, print, ACP): discovery and loads happen as the session starts — the host's first use. A per-contribution load trigger is not implementable for renderers and widgets (the host must know registrations before the first render) and would make the footer pop in mid-session; loading before the first turn is the earliest point where every contribution is meaningful.
- **Inside an extension**, heavy resources stay lazy at their own first use (Roslyn workspace, MCP processes) — that laziness is unchanged.

## Wiring per mode

- **Interactive**: `InteractiveSessionOptions` gains `ExtensionSeams? Extensions`. The session composes the host after resolving its workspace, then: `ToolResultRenderers`/`StatusWidgets` resolve from the host (falling back to the existing direct fields, which tests use); `BuildHarness` sets `Hooks = host.Hooks`; before the first turn the host's MCP tools are listed once and added to the tool registry. Disposal on session end.
- **Print**: `PrintModeOptions` gains the same seams. `CreateHarness` becomes async-aware (or the MCP listing happens in `RunAsync` before harness creation): `Hooks` + MCP tools wired the same way.
- **ACP**: `AcpMode.Run` gains an optional seams parameter; the host is composed once at serve start; the harness factory closes over it (`Hooks`) and the MCP tool list is resolved once and reused for each `session/new`.
- **Workspace identity**: `repositoryIdentity = workspace.GitCommonDir ?? workspace.WorktreeRoot` — git repositories key trust on the common dir (per the worktree architecture); non-git directories get a stable per-directory identity. `workingDirectory` = worktree root.
- **Renderers/widgets in print and ACP**: no surface (the T-40 degradation matrix already pins this); only hooks and MCP cross those modes. The done-when's renderer/widget proof is the interactive path.

## Trust prompts

- Interactive: `ConsoleTrustPrompt` — prints `lunate: project extension '<id>' runs code from this repository (<dir>). Load it? [y/N]` to stderr and reads one line from stdin; only `y`/`yes` approves. One-time per repository via the existing trust store; content changes re-prompt (loader behaviour, unchanged).
- Print + ACP: `NonInteractiveTrustPrompt` — returns false and logs: `project extension '<id>' was not trusted; run an interactive session to approve it.` (Never auto-approve repository code in a non-interactive run.)
- The prompt is a seam everywhere; a TUI-native prompt is a later refinement, not this change.

## Startup budget proof

- Code path: composition is only invoked from the three session paths; no shared startup code touches the store.
- Tests: (a) poisoned-store test — a store whose extension would log on load; `lunate --version` completes and the marker is absent; (b) the existing discovery tripwire (`< 250 ms` with 10 extensions) stays green; (c) `perf.sh` remains the objective gate.
- With no store present, discovery is a directory-existence check — the done-when's "startup budget unchanged when no extension is present" holds trivially and is asserted in the poisoned-store test's negative half.

## Existing tests

Session-level tests construct `PrintModeOptions`/`InteractiveSessionOptions` (3 + 3 files) and `AcpMode.Run`; they will get an **empty temp store** via their shared helpers so a developer's real `~/.lunate` never leaks into test runs.

## Fixture

`tests/TestExtensions/HelloExtension` gains a `HelloToolCallingHandler` (logs the observed tool name) so the e2e can prove "its hook observes a tool call" through the real loader; its renderer and widget (T-40) prove the UI surfaces. The fixture edit is fixture-only and named in the deviations.

## Deviations

- **Post-review fix**: cancellation mid-compose stops already-started services before surfacing (`EndSessionAsync` in the cancellation path; `Cancellation_mid_compose_stops_what_already_started` covers it).
- `ModelProviders` is exposed by the composed host but has no production consumer today (no model-catalog seam reads it yet); wiring it is deferred to the model-provider consumer change. The registry stays loader-owned.
- Extension MCP servers start at their first listing — which the composition performs at session start; "registration is registration only" (T-41) is unchanged.
- The trust prompt is line-based on the console (v1); a TUI-native prompt is a seam. Follow-up card: t_d80be84b (TUI-native trust prompt).
- Interactive composition runs through an awaited `ComposeAsync` in `RunAsync` instead of blocking the constructor with `GetAwaiter().GetResult()`: the workspace must exist first, the session already had a compose phase, and the first `BuildHarness` still sees the host's hooks. The host's MCP tools are listed in that same phase, before the first harness is built.
- The interactive session also re-wires its input pipeline from the host's hook runner when the direct `Hooks` seam is null, so `IInputReceivedHandler` contributions have a production effect like every other hook.
- `Cli.Run` gained an optional `ExtensionSeams? extensions` parameter, threaded to interactive, print and ACP. The non-session paths (`--version`, `--help`, `--discover`, flag parsing) return before it is read; it exists so tests can point a poisoned store at those paths and prove they never touch it.
- ACP composes at serve start against the process working directory (the only workspace known before the first `session/new`); session harnesses still build against each session's own cwd, and the MCP tool list resolved once is reused for every session.
- `HelloExtension` fixture edits (fixture-only): a `HelloToolCallingHandler` that logs `tool call observed: <name>` and an optional `markerPath` setting whose presence makes the factory write a load-marker file (the poisoned-store proof).
- `Lunate.Coding.Tests` now builds and copies the `HelloExtension` fixture into its output (the interactive end-to-end test needs the Coding session harness); the layering allow-list gained that one edge.

## Risks

- Session start gets slower with many extensions (bounded by per-extension load time, logged and warnable); failure isolation keeps one bad extension from costing the session.
- A session in a directory containing an untrusted project extension prompts before the TUI starts; denial is a log line, not a crash.
