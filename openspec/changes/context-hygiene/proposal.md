# Context hygiene: spills, concise results, stale reads and masking

## Why

The model's context is the scarce resource, and today the loop spends it on text the model cannot use. A 20,000-character build log enters the request verbatim because spilling starts only at 30,000 characters; even above that limit the request still carries 30,000 characters of head and tail. A file read before an edit stays in the request after the edit has made it wrong. A credential a tool echoes is redacted in the session and spill files at rest, but the request sent to the provider still carries it. These four problems were designed separately; this change makes them one admission pipeline with one accounting story, so a session's token numbers show the effect instead of a test asserting a string by hand.

The inventory also found a promise that is not kept: the spill marker names an absolute path under the sessions directory, but `read` refuses every path outside the workspace (`ReadTool.cs:98-104`), so the agent-tools spec's "`read` on it returns the full content" is false today. This change closes that gap as part of the spill behaviour.

## What Changes

- **Spills.** A result whose model-facing text exceeds `output.spillThresholdTokens` (new, default 2,000 estimated tokens) is written in full to the existing per-session spill file and the request carries a bounded preview plus a pointer that names the path, the size and the omitted count. `output.toolResultLimit` stays as the absolute character cap for the fallback path. The spill file becomes readable by `read` for the owning session and for a resume of it.
- **Concise results.** `ToolResult` gains `FullOutput`: a tool whose answer is a projection of a larger transcript returns the answer in `Output` and the transcript in `FullOutput`; the loop spills the transcript and sends the answer plus its pointer. `bash` adopts it (exit code and stderr as the answer, stdout as the transcript); the other core tools keep today's behaviour.
- **Stale reads.** A harness-level read ledger records the version a `read` saw (canonical path plus a digest of the LF-normalised text). `edit` and `write` refuse when the version they are based on is no longer current — mutated by the run or changed on disk — and tell the model what changed and that it must re-read. When a read is invalidated, the loop replaces that read result in its in-memory history with a one-line notice, so the next request and the compaction estimate see it; the session file keeps the original entry.
- **Masking.** `AgentHarnessOptions.ToolOutputMask` makes masking the first step of admission: every tool result is masked before it enters the history, the session entry, the emitted event and the spill file. `HarnessFactory` wires the session's existing exact-value `CredentialRedactor` for session and `--no-session` runs. Redaction at rest stays as defence in depth.
- Spec deltas for `agent-tools`, `agent-loop` and `agent-sessions`; a new addendum to ADR-0012 records the admission boundary and the contract additions (proposed, awaiting maintainer sign-off).

**Not in this change:** pattern-based secret detection (the repository's written rule is exact value only; reversing it needs its own decision), a session format change, model-summarised tool results, sweeping spill files, and any change to image handling.

## Capabilities

### New Capabilities

- None. The four behaviours refine existing capabilities; no new capability folder is introduced.

### Modified Capabilities

- `agent-tools`: the tool result contract (`FullOutput`), token-threshold spill with a pointer, spill files readable through `read`, and the read ledger with its stale-read refusal.
- `agent-loop`: context admission (masking, concise/spill projection, stale-read projection), tool execution safety, and the compaction trigger measured over the request as built.
- `agent-sessions`: what a tool entry stores (the model-facing result, not the raw transcript), session-owned spill files, and recorded usage that is the provider's count for the request as built.

## Impact

- **Public API:** `ToolResult.FullOutput`, `AgentHarnessOptions.ToolOutputMask` and `AgentHarnessOptions.SpillThresholdTokens`; entries in `src/Lunate.Agent/PublicAPI.Unshipped.txt`. The read ledger is internal between the loop and the core file tools, which share internals access.
- **Settings:** new `output.spillThresholdTokens` and `LUNATE_TOOL_SPILL_THRESHOLD`; `output.toolResultLimit` keeps its meaning and default. All three format culture-invariantly.
- **Code:** `Lunate.Agent` (`AgentHarness.Tools.cs`, `ToolOutput.cs`, `ToolOutputSpill.cs`, a new read ledger and context-admission type), `Lunate.Coding` (`ReadTool`, `EditTool`, `WriteTool`, `BashTool`, `HarnessFactory`, `SettingsStore`, the interactive/print wiring), `docs/guide.md`, and `adr/0012-addendum-context-hygiene.md`.
- **Tests:** `Lunate.Agent.Tests` and `Lunate.Coding.Tests`, using a fixture provider that reports the request's own estimated size as usage. No live APIs, no new packages, no session schema change, no golden session update.
- **Risk to review:** one public contract addition, two options and one reversed boundary (the model now sees masked text). They are flagged in `design.md` and the ADR for maintainer sign-off.
