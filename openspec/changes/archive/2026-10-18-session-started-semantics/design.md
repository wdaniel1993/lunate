# Design — SessionStarted semantics

## D1. Fire from the composition, after the load loop

The loader keeps one public entry (`Load`) for in-process and remote extensions; the event firing moves out of it. `ExtensionLoader.StartSessionAsync` becomes public (it already carries the once-guard and builds the payload) and `ExtensionSession.ComposeAsync` calls it **once, after the per-descriptor load loop**, before the host is returned. No other production caller loads extensions at session start.

## D2. Ordering consequences (accepted)

- Each extension's background services still start at its own load (unchanged); with the event now firing after the loop, handlers run with **all** services started — strictly stronger than before.
- A zero-extension session now sets the started flag (the call is unconditional), so `EndSessionAsync`'s guard symmetry holds (ending runs its no-op path instead of short-circuiting). Harmless and more consistent.
- Inspect mode (`startSession: false`) loads nothing into a session; it neither starts services nor fires the event (unchanged).

## D3. The `startSession` parameter keeps its name, narrows its meaning

It still gates the per-load block (that extension's services + tools-changed notification) and stays false for inspect loads. It no longer fires the event; the doc comment is updated to say so. (Renaming the parameter would touch every test call site for no behavioral gain.)

## D4. Remote extensions are unaffected

The remote session-started deferral (ADR-0017 addendum; PR #84) holds the event on the proxy until the session is live or the child first starts — it is state-based, not firing-point-based, so moving the firing point changes nothing there.

## D5. Regression test shape

Two extensions loaded at session start, each with a `SessionStarted` handler (reuse the existing fixtures — `HelloExtension` plus `ServiceConsumerExtension`, which already records cross-service lookups), assert both handlers ran and the later one saw the full set; then load a third extension into the running session and assert its handler did not run.

## SIGN-OFF (Daniel, 2026-10-18, card t_1c542df7)

Verbatim decision: *"Fire once, after every extension has loaded"* (option 1 of the card's three options).
