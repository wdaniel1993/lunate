# add-compaction

## Why

Card T-23 (deps T-11, done): compaction as an `IChatReducer`-shaped strategy the loop invokes before each model request. It is the last big missing core capability of the agent loop: the session format already carries the `compaction` entry (`summary`, `replaces`) and `AppendCompaction`, but nothing triggers compaction, summarizes, rewrites the request, writes the entry or emits `CompactionApplied`. It also blocks T-44 (memory sample: "survives compaction by re-injection") and is the missing producer for the `Compacting` hook (contract-complete, documented unwired since T-37). Done-gate: a recorded long session compacts once, the next request is under 60% of the window, and replay still matches.

## What Changes

- **Compaction strategy** (`Lunate.Agent`): trigger when the estimated request size passes 80% of the model's context window — estimate `chars / 4`, corrected by the last `UsageReported` input tokens when available; also forceable through a harness API (the `/compact` UI binding lands with the TUI cards). Summarize everything older than the kept tail **with the same model and a fixed prompt** (goal, decisions, files touched, open problems); write a `compaction` session entry (`summary`, `replaces`); the next request = system prompt + summary + last turns verbatim. The session file always keeps the full history.
- **Safety rules**: never split a tool call from its result (the tail boundary expands to pair boundaries); always keep the system prompt, AGENTS.md, and the last N turns verbatim; compactor failure leaves the request unchanged and is reported.
- **`CompactionApplied` event** emitted with the replaced entry ids and the estimated tokens after compaction.
- **`Compacting` hook wired** (producer exists now): handlers run before compaction; `Provide` overrides the default summary, failure falls back to the default. Extensions spec updated (only `InputReceived` remains unwired).
- Spec deltas: `agent-loop` gains a compaction requirement; `extensions` Hook wiring requirement updated.

## Impact

- `Lunate.Agent`: compactor + trigger + request rebuild + event + hook invocation point; `Lunate.Extensibility`: adapter maps `Compacting`.
- No new packages; no golden-invalidating changes; no new ADR (implements the guide's loop/compaction section, lines 278-312).
