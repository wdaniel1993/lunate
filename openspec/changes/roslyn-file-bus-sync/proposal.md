# Wire the Roslyn backend to the file-change bus (workspace sync)

## Why

The file-change bus is live in production: `write` and `edit` publish through `ToolContext.FileChanges`, extensions subscribe via `core/file-bus`, and the bus's sign-off (`wire-file-change-bus`, D10) filed this card as its **first real subscriber** — the card names `core/file-bus` as the subscription path. Meanwhile the Roslyn backend already models exact dirty marks (`NotifyFileChanged` → `_dirty`, drained into the refresh path; `CsDiagnosticsGateTests` proves the mark handles the stamp-invisible class), but **nothing in production calls the mark path**: in-session tool edits reach diagnostics only through the per-call content-stamp scan, which cannot see a change whose (timestamp, length) stamp is unchanged. Two consequences: a real detection gap, and the bus's first product subscriber missing.

## What Changes

- **The Roslyn backend becomes the bus's first core subscriber.** The session's `WorkspaceServices` subscribes once to the session's file-change bus (created before extensions load); events route by workspace id to the backend for that root — **existing entries only** (a mark never creates a backend; a root with no backend ignores the event because its first load reads disk anyway). `.cs` paths become exact dirty marks; project/solution files (`.csproj`, `.sln`, `.slnx`) mark the workspace for a **reload on the next call**.
- **The dirty set becomes concurrent** so the bus handler is mark-only and never blocks on the backend gate: the bus runs handlers on its dispatch queue with a timeout, and a cold load or long compile must not make marks time out (the handler completes in microseconds; delivery stays ordered).
- **The content-stamp scan stays** as the backstop for out-of-band changes — the bus's documented limit is that `bash` (and any write outside the tool path) is not published, and the scan is what keeps those visible today. The subscription makes in-session edits exact and stamp-independent; it does not remove out-of-band detection.
- **Proofs**: unit routing/laziness/eviction tests; a real-Roslyn integration proving a bus-delivered edit with an *unchanged content stamp* refreshes diagnostics **without a reload**; a session-level e2e (write tool → `cs_diagnostics` fresh through a composed session).

## Impact

- Spec delta: **agent-tools** (MODIFIED "C# diagnostics tool": what "files changed since the last check" means — bus marks for in-session mutations, content stamps for out-of-band changes, project-file reloads).
- `docs/spec/extensibility.md` gains a sentence (the first core subscriber); no other docs change.
- No new NuGet packages; no ADR (the change uses existing seams — the bus, the services registry, the backend's freshness machinery).
- Layering: `Lunate.Coding` gains a subscription to the Extensibility bus — it already references `Lunate.Extensibility` (ExtensionSession); no new graph edge.
