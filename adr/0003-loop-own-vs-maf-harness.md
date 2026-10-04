# 0003 — Loop: own loop on IChatClient, borrowing Microsoft Agent Framework patterns

- Status: proposed — awaiting maintainer sign-off
- Date: 2026-10-04
- Spike: `docs/spikes/S-1/report.md` (evidence under `docs/spikes/S-1/evidence/`)

## Context

Phase 0's S-1 spike tested whether the Microsoft Agent Framework (MAF) Harness
(`Microsoft.Agents.AI.Harness` 1.23.0) can be Lunate's loop instead of the own
loop on `IChatClient` the guide assumes. Both minimal agents were driven by the
same stub `IChatClient` and the same four tools; six checks were run without API
keys. The maintainer leaned toward MAF, so the harness was configured with every
optional feature off and measured fairly.

Result summary (details in the report): the harness passes checks 2 (prompt
size), 4 (approvals) and 5 (deterministic replay, plus session serialization),
but check 3 is only partial — it does not stream steering, its cancel path
throws without a terminal event or a cancelled tool result, run lifecycle
events must be synthesized, and `FunctionInvokingChatClient` rewrites tool
failures to `Error: Function failed.`, which conflicts with Lunate's rule that
tool errors tell the model what failed and what to do next. The harness also
hardwires `FunctionInvokingChatClient`, while AGENTS.md forbids it because
approvals, cancellation and events must stay under the loop's control. Check 6
shows high churn: 15 .NET releases in 115 days, breaking changes in 11 of them,
concentrated in approvals, session replay and file access.

## Why the tool loop must be ours

`FunctionInvokingChatClient` (FIC) runs the tool loop inside the chat
client: when the model requests a tool, FIC executes it, sends the
result back and repeats until the model answers without tool calls.
The caller sees mainly the final response. For apps with a few simple
functions this is ideal.

For Lunate the tool loop is the product. Almost every feature acts
between "the model requested a tool" and "the next model call":

- approvals: our policy (risk level, standing rules, ACP permission
  requests) decides before anything runs;
- events: tool blocks, diffs from `ToolResult.Details` and match tiers
  for the TUI and ACP, which never go to the model;
- steering: queued user messages are injected before the next call;
- cancel: `Esc` ends the run with a cancelled tool result and a valid
  history, so the session can continue;
- model-facing output: our error messages and truncation, exactly;
- compaction, step limits, retries and session recording between
  iterations.

With FIC each of these becomes a hook into a loop we do not own: some
have extension points, some need workarounds, some are not possible
today. Workarounds depend on internal behaviour of a fast-moving
package (see S-1 check 6). The MAF Harness always builds FIC into its
pipeline, so adopting it means handing the loop to the framework
permanently.

This is not a flaw in Microsoft.Extensions.AI; it is the wrong layer
for a coding agent. Lunate uses Microsoft's standard for talking to
models (`IChatClient`, `ChatMessage`) and keeps the loop itself (about
200 lines in `Lunate.Agent`).

## Decision

- **The loop stays ours**: a plain loop on `IChatClient` with Microsoft.Extensions.AI
  message types, our `ITool`/`AgentEvent` types, our approval policy, our
  cancellation contract and our session JSONL store, as specified in the guide.
- **Do not reference Microsoft.Agents.AI packages from `src/`.** MAF is not a
  product dependency in any layer.
- **Borrow patterns, not the harness**: use MAF's approval semantics
  (per-tool and per-tool+arguments standing rules, approval request/response
  content shapes) as the reference for Lunate's own approval policy and event
  mapping; use its session serialization approach as a reference for our JSONL
  store; revisit its compaction strategy if ours under-delivers.
- **Re-open conditions**: MAF removes the `FunctionInvokingChatClient` coupling,
  exposes mid-run steering/message injection we can consume, stops rewriting
  tool error detail, and reduces release churn.

## Alternatives considered

- **Adopt the MAF Harness as the loop**: passes approvals, replay and prompt
  checks; rejected on check 3 gaps, the hardwired function-invocation pipeline,
  tool-error rewriting and churn, all of which fight the architecture the guide
  and AGENTS.md already fix.
- **Adopt MAF agent types without the Harness**: still pulls MAF session and
  pipeline types into `Lunate.Agent`, violating the "model types only from
  Microsoft.Extensions.AI" rule for no demonstrated benefit.
- **Status quo, ignore MAF entirely**: rejected — the approval standing-rule
  model and session serialization are worth copying into our design.

## Consequences

- `docs/spikes/S-1/` remains throwaway; no `src/` changes come from this ADR.
- `Lunate.Agent` implements approval decisions with per-tool and per-arguments
  granularity plus an "always for this session" cache (spike `OwnLoop` shows a
  minimal working shape).
- A follow-up change tracks MAF releases for ideas; any future proposal to adopt
  it must re-run S-1's check matrix against the then-current version.
