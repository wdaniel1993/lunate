# Proposal: Interactive session — wiring, steering, Esc (T-22, part 1)

## Why

The TUI components exist; nothing connects them to the agent. Part 1 of T-22 wires a real interactive session: the harness's event stream becomes scrollback and a live area, the input line drives runs and steering, `Esc` cancels, approvals round-trip through the prompt, and the harness gains the steering seam the guide promises ("the user may type while a turn runs; the message is queued and injected before the next model call") — today `RunAsync` has no injection point at all. Part 2 (commands, pickers, `/command` completion) follows separately; `@path` completion is deferred to the new card T-53.

## What changes

- **Steering seam (`Lunate.Agent`)**: a thread-safe `SteeringQueue` on the harness options; the loop drains it **before each model request, after all tool results of a batch are appended** — never between a tool call and its result. Each injected message is appended to the history and session (no schema change: position in the entry chain is the origin; if implementation proves an origin field is genuinely needed, stop and propose an ADR), announced by a new **`SteeringInjected(runId, entryId)`** event. Leftovers stay queued; the queue exposes reclamation for the frontend. v1 steers the top-level run only. **ADR-0013 gets a dated amendment** recording the seam.
- **Anthropic wire merge (`Lunate.Ai`)**: steering directly after tool results would create consecutive user turns; for the anthropic path the steering text is merged into the user turn that carries the tool results, at wire time only (history/session unchanged). OpenAI path keeps the separate user message. Tests use the recorded fixtures for both providers.
- **Input pipeline (`Lunate.Coding`)**: every submitted text — new turns and steering — passes through the `InputReceived` hook chain (Transform/Consume) before anything happens; a consumed input starts nothing and is reported as a dim notice. History storage: `~/.lunate/history` (JSONL, one encoded entry per line) behind Up/Down navigation.
- **Interactive session (`Lunate.Coding/InteractiveSession`)**: event pump (streaming text → live tail + paragraph commit to scrollback; tool events → `ToolBlock`s committed; usage → footer; retrying/compaction/step-limit → notices), key wiring (router, Ctrl+C window, history), `Esc` cancel, approval adapter (`ApprovalRequested` → prompt in the live area → `y`/`n`/`a`; "always" memory session-scoped by tool name), and the pinned interaction rules: **Esc keeps queued steering unsent and returns it to the input line; a normally finished run with leftover steering auto-starts the next run; error or step limit does not auto-run (leftover returns to the input line); no steering input while the approval prompt is open.**
- **End-to-end scripted snapshot**: one scripted session covering streaming→scrollback, a tool block, an approval round trip, mid-run steering, and `Esc` cancel — frames and scrollback pinned as goldens.

## Done when

The E2E snapshot passes; steering order falsifiers (never between call and result; top-level only; leftover reclamation) are tested; both providers' merge behavior is proven against recorded fixtures; the replay test (recorded session + steering at a fixed step) reproduces byte for byte under the existing replay conventions; `scripts/verify.sh` green. No new packages.
