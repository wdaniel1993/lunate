# S-1 report — own loop vs Microsoft Agent Framework harness

- Date: 2026-10-04
- Change: `run-phase0-spikes` (guide card T-03)
- Question: should Lunate's loop be our own loop on `IChatClient`, or the
  Microsoft Agent Framework (MAF) Harness?
- Method: two throwaway agents in the same `IChatClient` stub harness, six
  checks, no API keys. Raw evidence: [`evidence/`](evidence/), including
  `run-checks.sh` output and the check 6 release extract.
- Outcome: **recommend the own loop, borrow MAF's approval and session
  patterns**; ADR draft [`adr/0003`](../../../adr/0003-loop-own-vs-maf-harness.md)
  is proposed and awaits maintainer sign-off.

## What was built

Both agents use the same `ScriptedChatClient` (`Shared/`), which streams canned
responses over `IChatClient` and records every request, so prompt size and
request shape are measured at the same seam for both. The canned streams
include a tool call whose arguments arrive in fragments, and support side
effects at a precise point in the stream (used for steering and `Esc` cancel).

- `Shared/` — stub client, four tool stubs (`read`, `write`, `edit`, `bash`),
  stream recorder/replayer (JSONL, turn boundaries), prompt metrics.
- `OwnLoop/` — a minimal loop: messages, tool declarations, `StreamAccumulator`
  (merges split arguments), approval callback with a session cache, steering
  queue, cancellation, events.
- `MafHarness/` — `Microsoft.Agents.AI.Harness` 1.23.0 pinned; `HarnessAgent`
  with every optional feature off, the same four tools, and a mapping from
  `AgentResponseUpdate` to Lunate-style events.

### MAF pins and "everything off"

Pinned: `Microsoft.Agents.AI.Harness` 1.23.0 (transitively
`Microsoft.Agents.AI` 1.23.0, `Microsoft.Extensions.AI` 10.10.0,
`Microsoft.Extensions.AI.Abstractions` 10.10.1, `Microsoft.ML.Tokenizers`
2.0.0). Flagged off: `DisableCompaction`, `DisableTodoProvider`,
`DisableAgentModeProvider`, `DisableFileMemory`, `DisableWebSearch`,
`DisableAgentSkillsProvider`, `DisableOpenTelemetry`; file access is opt-in so
no `FileAccessStore`; `HarnessInstructions = string.Empty` for the
apples-to-apples prompt measurement. Two defaults were not obvious and would
otherwise have been on: `AgentSkillsProvider` (file-based skill discovery from
the CWD) and the OpenTelemetry agent wrapper.

## Check matrix

| # | Check | Own loop | MAF harness | Verdict |
| --- | --- | --- | --- | --- |
| 1 | Startup + idle memory (median of 5, Release, real processes) | startup 54.5 ms, first run 4.8 ms, RSS 48.7 MB, heap 302 KB | startup 64.5 ms, first run 18.8 ms, RSS 54.3 MB, heap 453 KB | Both negligible; MAF +10 ms startup, +5.6 MB RSS, +14 ms first run |
| 2 | Prompt size, everything off | 1337 chars ≈ 335 tokens | 1337 chars ≈ 335 tokens (harness instructions omitted); 2112 chars ≈ 528 tokens with MAF's default instructions | PASS both, well under 1000 |
| 3 | Complete event stream for TUI/ACP incl. steering and cancel | Complete: run lifecycle, text, tool start/args/end/result, approval, steering, usage; cancel yields a valid stream with a cancelled tool result | Maps text, tool calls/results, usage, approvals via `ToolApprovalRequestContent`; **missing**: run lifecycle (synthesize), steering (no API), cancel event (throws), and FIC replaces tool failures with `Error: Function failed.` | Partial: app-level wrapper required for lifecycle, steering, cancel; error detail loss |
| 4 | Approval hooks: per tool, per args, always-for-session | Callback gets tool + args; deny path; session cache by tool; demonstrated all cases | Per-call args visible; `CreateAlwaysApproveToolResponse` (per tool), `CreateAlwaysApproveToolWithArgumentsResponse` (per exact args), `AutoApprovalRules` with full `FunctionCallContent` | PASS both; MAF's model is richer |
| 5 | Session recording + deterministic replay | JSONL with turn boundaries, replay transcript byte-identical | Same recorder/replayer produces byte-identical transcript; `SerializeSessionAsync`/`DeserializeSessionAsync` round-trip (1361 chars) | PASS both; MAF adds built-in session persistence |
| 6 | API churn over recent releases | — | 15 .NET releases in 115 days; breaking entries in 11 of 15, concentrated in approvals, session replay, file access; several `[Experimental]` harness APIs | High churn; pin and budget upgrades |

Evidence: `evidence/check1-summary.txt`, `check1-*.txt`, `check2-*.txt`,
`check3-*.txt`, `check4-*.txt`, `check5-*.txt`, `check6-api-churn.md`.

## Detail per check

### 1. Startup and idle memory

Probe mode builds the agent, runs one turn, prints self-reported timings and
heap, then idles; the runner samples RSS from a second process. Five
process-fresh runs each; first runs carry cold-start JIT (own 202 ms, MAF
233 ms) and the median over the remaining runs is stable. The own loop is
lighter, but both numbers are far below any plausible budget; this check does
not decide the question.

### 2. System prompt size with everything off

`PromptSurface.Measure` counts the instructions actually sent to `IChatClient`
plus tool name/description/schema, on the first request:
own loop 1337 chars (499 system prompt + 838 tools) ≈ 335 tokens (chars/4, the
guide's estimator). MAF with `HarnessInstructions = string.Empty` is exactly
the same 1337 chars. MAF's default `HarnessAgent.DefaultInstructions` adds 773
chars (`evidence/check2-maf-harness.txt`), total 2112 ≈ 528 tokens — still under
1000. Sub-check passes for both.

### 3. Event stream, steering, cancel

Own loop (`check3-own-loop.txt`): complete, ordered stream including
`steering_injected` before the next model call (verified in the next recorded
request) and a cancel transcript that ends with a cancelled tool result instead
of a broken history. Events have a stable textual form, which is also what
check 5 hashes.

MAF (`check3-maf-harness.txt`, `evidence/split-maf-harness.txt`): text, tool
call/result, usage and approvals map cleanly; message boundaries must be
inferred (the mapper closes a text message when a call or result arrives). Not
streamed by the framework: `RunStarted`/`RunFinished` (only a last
`FinishReason`), steering, and a cancel terminal event — cancel surfaces as
`OperationCanceledException` with no cancelled tool result and no terminal
event. Tool failures are rewritten to the literal `Error: Function failed.`,
which conflicts with the rule that tool error messages must say what failed and
what to do next.

### 4. Approvals

Both pass. MAF's `ToolApprovalAgent` is a genuine strength: standing rules for
"always this tool" and "always this tool with these exact arguments" persisted
in the session, plus argument-aware auto-approval rules. All demonstrated in
`evidence/check4-maf-harness.txt`. This is the most compelling reason to
**borrow** rather than ignore MAF. Note the same middleware is why the harness
hardwires `FunctionInvokingChatClient` (`HarnessAgent` builds
`UseFunctionInvocation`), which AGENTS.md forbids for Lunate.

### 5. Recording and deterministic replay

Both agents run the same canonical scenario twice — once through
`RecordingChatClient` wrapping the stub, once through `ReplayChatClient`
reading the JSONL — and the mapped transcripts hash identically
(`replay_deterministic=true` for both). MAF additionally round-trips its
`AgentSession` state through JSON (`session_roundtrip=true`, 1361 chars).

### 6. API churn

`dotnet-*` releases from 1.10.0 (2026-06-10) to 1.23.0 (2026-10-01): 15
releases, breaking entries in 11 of them, including 1.14.0 (graduated
`ToolApprovalAgent` and todo/mode providers; made file access opt-in), 1.21.0
(file-access line-numbering contract), 1.22.0 (approval binding for replay) and
1.23.0 ("better support tool changes between runs", "enforce approval response
binding consistently"). The `HarnessAgentOptions` surface itself is partly
`[Experimental]`. Adopting the harness means pinning and re-validating often.

## Extra evidence: split tool arguments

The task requires the stub to split a tool call's arguments across chunks, as
some adapters do. The own loop's `StreamAccumulator` concatenates fragments by
`CallId` and the tool executes with complete arguments
(`evidence/split-own-loop.txt`, `split_arguments_assembled=true`). MAF has no
such accumulation: each fragment is treated as a separate complete call, the
tool is invoked five times with `{"$raw": ...}` fragments, and all fail with
`Error: Function failed.` (`evidence/split-maf-harness.txt`,
`tool_invocations=5`). Fairness note: the official OpenAI adapter in
`Microsoft.Extensions.AI` 10.10.1 buffers partial arguments internally and emits
one complete `FunctionCallContent` (source review), so this is defensive
robustness rather than the adapter norm. If Lunate ever fronts an adapter that
streams fragments — the guide already plans an Anthropic client — the own loop
handles it and the harness needs an accumulator middleware in front.

## Surprises

- MAF's prompt with everything off is byte-for-byte the same size as the own
  loop; the cost is in wiring and control, not context.
- Two default-on harness features (`AgentSkillsProvider` scanning the CWD,
  OpenTelemetry wrapper) are invisible unless you read `HarnessAgentOptions`;
  "everything off" needed more flags than the guide listed.
- MAF's approval model is the strongest part of the package, and it is exactly
  the part coupled to `FunctionInvokingChatClient`.
- The harness collapses tool exceptions to `Error: Function failed.` — a
  blocker for the "errors teach the model what to do next" rule.
- Session serialization is only 1361 chars for a three-turn session and
  round-trips, so borrowing a durable session format from MAF is realistic.
- The split-argument failure is silent from the model's perspective: it sees
  five failed tool calls, not malformed input.

## Recommendation

**Keep the own loop on `IChatClient`; borrow parts from MAF.** The guide's rule
is to switch to the harness only if checks 2–5 pass cleanly. Checks 2, 4 and 5
pass cleanly; check 3 does not (steering absent, cancel semantics do not meet
the spec, run lifecycle not streamed, error detail lost), and the harness
hardwires `FunctionInvokingChatClient` against an explicit AGENTS.md rule. The
default therefore stands.

Borrow, in priority order:

1. The approval model: approval request/response content types and the
   "always tool" / "always tool+args" standing-rule semantics as the shape of
   Lunate's own approval policy and event mapping (without FIC).
2. Session serialization ideas for the JSONL store and `--resume`.
3. Compaction strategy shape if our own compaction under-delivers.

Re-open the decision if a released MAF version removes the
`FunctionInvokingChatClient` coupling, adds steering/message-injection
semantics we can consume mid-run, yields a terminal cancel event, and
preserves tool error detail for both returned results and thrown exceptions
(or exposes `IncludeDetailedErrors`). Measurable check: re-run
`run-checks.sh` against that version — re-open if checks 2–5 pass cleanly.

## Revision addendum (2026-10-04)

ADR-0003 revision, items 2 and 4:

- **Tool errors (item 2).** A tool that *returns* an error result (no
  exception) keeps its message through the harness: `Error: file not found at
  src/Missing.cs. Use bash ls to locate the file.` reached the model verbatim.
  A tool that *throws* collapses to `Error: Function failed.` (the exception
  is attached to `FunctionResultContent.Exception` but the message is not
  sent). `FunctionInvokingChatClient.IncludeDetailedErrors` is **not
  exposed** by `HarnessAgent` 1.23.0 — no public member, and the harness
  package contains no reference to it. Evidence:
  `evidence/errorpath-maf-harness.txt`,
  `evidence/revision-experimental-and-harness-surface.txt`.
- **Churn split (item 4).** In the 1.10.0 → 1.23.0 window: 41 `[BREAKING]`
  lines across 15 releases (12 releases with at least one); only 2 of the 41
  are harness-scoped (both in 1.14.0: `HarnessAgent` graduation, FileAccess
  opt-in). The rest are concentrated in packages the harness consumes
  (approvals, session replay, file access contracts). Full split in
  `evidence/check6-api-churn.md` (addendum; script: `churn-split.py`).

## Limits

- Stub streams only: no live provider quirks, no real latency; the recorder
  round-trip is tested, but network behaviour is not.
- One machine (macOS arm64, .NET 10.0.103); the memory/startup deltas are small
  enough that another OS will not change the conclusion.
- MAF at 1.23.0 specifically; the package moves weekly (check 6).
- Spike code is throwaway and lives outside `lunate.sln`; nothing here changes
  `src/`.
