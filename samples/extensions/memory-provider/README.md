# Sample: memory-provider

An extension that remembers facts across a session: user lines starting with `remember:` are
captured into a registered in-memory service, committed at the turn boundary and injected as one
source-tagged context message before every model request. Because injection happens at
request-build time, memories survive compaction — the injected section is rebuilt per request even
after the history has been replaced by a summary.

## Core capabilities proven

| Capability | Where |
| --- | --- |
| `ContextBuilding` capture and injection | `MemoryContextBuildingHandler` scans the request's user messages for `remember:` lines and adds one `## Memory` message per request; capture is idempotent (case-insensitive exact-text match), so repeated scans never duplicate. |
| Registered service with session lifecycle | `MemoryStore` is registered as `memory-store`; the host starts it once the session is live and stops it idempotently. |
| `TurnEnded` storage and audit entry | `MemoryTurnEndedHandler` commits the buffer through the service and appends `ext/memory-provider/committed` with the committed count. |
| Re-injection after compaction | The memory section is rebuilt on every request, so it is present in the first request after compaction even though the history was replaced by a summary. |
| Per-extension context budget | Over-budget additions are dropped by the hook runner and logged with the extension id. |
| Testing kit | `MemoryProviderExtension.Tests` loads the built extension through the real loader in a temp directory, replays committed fixtures and drives the hooks through the real harness. |

## Layout

```text
samples/extensions/memory-provider/
  MemoryProviderExtension/        the extension: manifest, factory, store service, handlers
  MemoryProviderExtension.Tests/  kit-based tests and the replay fixtures
  README.md
```

## Semantics

- **Capture** — every user line whose trimmed text starts with `remember:` (case-insensitive); the
  remainder after the colon is the memory. `Add` ignores duplicates (case-insensitive exact match)
  across the committed list and the buffer.
- **Commit** — at every turn end the buffer is committed; the committed list is capped at
  `maxMemories` (default 20), oldest dropped first.
- **Injection** — one `system` message containing the `## Memory` heading and one bullet per
  committed memory (capped at `maxMemories`), added before every request that has memories; the
  hook runner source-tags the added message with the extension id.
- **Ordering** — memories captured while building request N are committed at that turn's end and
  injected from request N+1.
- **Store lifetime** — in-memory per session, deliberately: persistence is the extension's own
  concern (a per-extension data directory is a candidate future primitive).

## Settings

`maxMemories` (number, default 20) bounds the committed store; the oldest memories are dropped
first.

## Copy and extend

1. Copy this directory, rename the projects and namespaces, and update `extension.json`
   (`id`, `entryAssembly`, `apiVersion`, `hooks`, `services`, `settingsSchema`).
2. Keep the tests: `MemoryProviderExtension.Tests` installs the built extension into a temp
   directory, so tests never touch the real `~/.lunate`; re-record the fixtures with the
   `RecordingChatClient` pattern when request shapes change.
3. Replace `MemoryStore` with your own persistence (a file, a database); the handlers only need
   capture, commit and a snapshot to inject.

See `templates/extension/README.md` for the full manifest and hook wiring reference.
