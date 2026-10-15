# Kit: make multi-run hosts deliver file changes (flush per run, drain at session end)

## Why

Card `t_4b1b2bf9` (deferred from the wire-file-change-bus review): `ExtensionTestHost.RunAsync` drains the file change bus after every run, and the drain is a spec'd one-way latch ("Once a drain begins, further notifications SHALL be dropped and logged"). A second `RunAsync` on the same host therefore silently drops every file change. The class docstring says "runs one prompt" without forbidding a second, the public API permits it, and real kit users already run multi-turn hosts (the lsp-diagnostics tests write once per turn across two `RunAsync` calls).

## What Changes

- **The missing primitive: `FileChangeBus.FlushAsync`.** Completes when every notification enqueued so far has been dispatched, **without beginning the drain** — delivery continues afterwards. `DrainAsync` stays the one-way session-end latch (unchanged, spec'd). This is the "a workaround means the core is missing a primitive" rule applied: the kit needs a flush that is not a drain.
- **The kit re-arms per run.** `RunAsync` flushes (this run's deliveries are observable when it returns — the existing single-run assertions stay deterministic); the one-way drain moves to `StopAsync` (session end), mirroring the production teardown order (drain → dispose → end session). Multi-run hosts now deliver every run's changes; late notifications are dropped only after the session actually ends.
- **The contract is pinned by tests.** Bus level: flush observes delivery without ending it, and a drain after a flush still latches. Kit level: two runs on one host deliver both runs' events in order (RED before the fix — the second event was dropped), and the existing single-run payload test keeps passing.

## Impact

- Spec delta: **extensions** (MODIFIED "File change bus": the flush sentence + one scenario).
- Public API: one additive method on the public `FileChangeBus` (no PublicAPI tracking file exists for this assembly — nothing to update; the extension-facing `IFileChangeBus` handle is untouched — flush is an owner-side capability).
- No behaviour change in production (nothing production-side drains twice); no new packages; no ADR (the card pins the resolution; the design records why the other two options were rejected).
