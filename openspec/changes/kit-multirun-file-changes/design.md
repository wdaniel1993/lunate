# Design — kit-multirun-file-changes

## D1 — The pin: flush per run, drain at session end (the card's "re-arm" option)

The card offered three options; two are rejected on evidence:

- **Reject a second run with a clear error** — would break the existing multi-turn kit pattern: the lsp-diagnostics tests write once per turn across two `RunAsync` calls, testing in-session server restart across turns. Rejection would force per-turn hosts, changing the semantics those tests exist to prove. Multi-turn hosts are legitimate; the fix must support them.
- **Document the one-run contract** — the docstring would have to say "a second run silently drops its file changes", which documents a wart, not a contract; the kit's own tests violate a "one run" rule today.
- **Re-arm per run** — chosen. The drain cannot be re-armed (the one-way latch is spec'd: "Once a drain begins, further notifications SHALL be dropped and logged"), so "re-arm" reduces to: **do not drain per run; flush per run; drain once at session end.** The kit needs a flush that is not a drain — the core was missing that primitive (the split-convention rule).

## D2 — `FileChangeBus.FlushAsync`: the minimal primitive

```csharp
public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
```

- Completes when `_processed >= _enqueued` at call time (same completion bookkeeping as `DrainAsync`'s `Drain` records); **does not set `_drainStarted`** — notifications after the flush are still delivered.
- No `_disposed` check: after `Dispose` the pump still completes the already-enqueued queue (channel completion drains it), so a pending flush resolves; with nothing pending it returns immediately.
- Stays on the concrete `FileChangeBus` (owner-side capability). The extension-facing `IFileChangeBus` handle stays subscription-only — extensions observe delivery, they do not control it.
- `DrainAsync` unchanged; its doc gains one sentence distinguishing it from the flush.

## D3 — Kit wiring: where the flush and the drain go

- `RunAsync` (`ExtensionTestHost.cs:185`): `DrainAsync` → `FlushAsync`. The docstring becomes: runs one prompt through the real harness and **flushes** file changes; runs may repeat on one host (each call is one prompt in the same session); the one-way drain happens at session end.
- `StopAsync`: add `DrainAsync` before `Runner.EndSessionAsync` — the session-end final flush, mirroring the production teardown (`ExtensionSession.DisposeAsync` drains first, then disposes the bus, then ends the session). `DisposeAsync` already calls `StopAsync` before disposing the bus, so the latch still guards post-teardown publishes.
- Determinism: every existing per-run assertion keeps working — the flush makes this run's deliveries observable before `RunAsync` returns (the existing single-run payload test is the witness).

## D4 — Tests and the spec delta

- **Bus level** (`Lunate.Extensibility.Tests`, alongside the existing bus tests): a flush observes delivery without ending it — notify → flush → handler saw it; notify again → flush → handler saw both; then drain → a late notify is dropped and logged.
- **Kit level** (`Lunate.Extensibility.Tests`, Extensibility.Testing.Tests): two `RunAsync` calls on one host, each running the publishing tool → the collector sees both events in order, and no drop warning appears in the host log. RED before the fix (second event dropped as "a drain has begun").
- **Spec delta** (`extensions` spec, MODIFIED "File change bus"): add "A flush SHALL complete when every notification enqueued so far has been dispatched, without beginning the drain — notifications arriving afterwards are still delivered." + scenario "A flush observes delivery without ending it". All existing scenarios preserved verbatim.
- **Docs** (`docs/spec/extensibility.md`, File change bus section): one clause — delivery is ordered per workspace; a flush observes delivery without ending it; the session-end drain is one-way.

## D5 — Non-goals

No production wiring changes (no production path drains twice — verified at the card); no `IFileChangeBus` change; no `DrainAsync` semantic change; no kit API change beyond the RunAsync/StopAsync behaviour.
