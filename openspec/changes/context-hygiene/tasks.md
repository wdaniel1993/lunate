# Tasks — context hygiene

## 1. Accounting harness and red tests

- [x] 1.1 Add a fixture `IChatClient` (in `tests/Lunate.Agent.Tests`) that reports `InputTokenCount = Utf8Length(request) / 4` for every call, so each test below can read the session's usage instead of asserting a string length by hand. Prove the fixture against an existing session-mirror test first.
- [ ] 1.2 Add the four red tests, one file each, and record their failing output on the PR:
  - `ContextSpillTests` — a 9,000-character result spills and the request is under 700 estimated tokens; today it passes through whole.
  - `ContextConciseResultTests` — a `bash` call with 20,000 characters of stdout and a stderr failure line sends stderr and the pointer (under 700 tokens); today it sends stdout.
  - `ContextStaleReadTests` — a read, an edit, then a second edit from the old text is refused and the old read leaves the request; today the edit succeeds and the read stays.
  - `ContextMaskingTests` — a known credential echoed by a tool is `[redacted:<name>]` in the request, event, session entry and spill file; today it is raw in all four.
- [ ] 1.3 Commit: `test(agent): pin the four context-hygiene behaviours red`.

## 2. Spills

- [ ] 2.1 Add `ToolOutput.EstimateTokens` (UTF-8 bytes / 4) and the pointer renderer (10 head and 10 tail lines, at most 2,000 characters, counts culture-invariant, path, next action). Unit tests for: under both limits unchanged; over the token threshold; over the character cap; surrogate-safe cut; tiny limit degrades.
- [ ] 2.2 Add `AgentHarnessOptions.SpillThresholdTokens` (default 2,000) and `SettingsStore`'s `output.spillThresholdTokens` (positive integer, `LUNATE_TOOL_SPILL_THRESHOLD`, unknown-key and invalid-value problems named). Tests: default, file value, environment override, invalid value.
- [ ] 2.3 Route the loop's admission through the threshold: mask first (section 5), then spill `FullOutput` or the big `Output` with the existing `ToolOutputSpill`, then render the pointer. Keep `ToolOutput.Truncate` for the no-session and failed-spill path. Tests: the red test from 1.2 plus the existing `ToolOutputSpillTests` and `AgentToolOutputLimitTests` stay green.
- [ ] 2.4 Grant `read` the session's canonical `tool-output` directory for the run and on resume (extend the existing read allowance; do not accept arbitrary absolute paths). Tests: the marker's path reads back in full; a sibling path outside the directory is still refused; a resume reads it too; a no-session run writes nothing.
- [ ] 2.5 Commit: `feat(agent): spill a large tool result as a readable pointer`.

## 3. Concise results

- [ ] 3.1 Add `ToolResult.FullOutput` with its contract doc and the rule that an equal value is absent. Tests: equal behaves as absent; an error result may carry it; the loop sends `Output` verbatim apart from masking and the pointer.
- [ ] 3.2 Make `bash` return the concise answer (stderr, exit footer, last 20 stdout lines within 2,000 characters) and the composed stdout+stderr as `FullOutput`, keeping the 1 MB capture cap and the timeout/exit-code behaviour. Tests: the red test from 1.2; a short command still sends everything (no pointless spill); a timed-out command keeps its error result.
- [ ] 3.3 Handle the no-session case: append the bounded `FullOutput` after the answer with the count-only marker, never a pointer to a file that does not exist. Test from the agent-tools spec.
- [ ] 3.4 Commit: `feat(coding): let bash send its answer and spill its transcript`.

## 4. Stale reads

- [ ] 4.1 Add the internal `IReadLedger` and the harness `ReadLedger`: canonical path, SHA-256 of LF-normalised text, call id; newest read live, older reads superseded; invalidate on every published `FileChangeEvent`; seed unknown versions from the loaded history on resume. Unit tests for each transition, including a same-content edit (digest unchanged) and an out-of-band change.
- [ ] 4.2 Stamp the internal `ToolContext.Reads` per call (null outside a run) and have `read` record the digest of the text it already reads. Test: the loop-run context has a ledger, a hand-built context has null.
- [ ] 4.3 Check the live version in `edit` and `write` inside the mutation queue, before matching or writing. Tests: the second edit from the old text is refused with the path and the re-read instruction and the file is untouched; a re-read allows it; a never-read path still edits; a digest mismatch (out-of-band write) refuses; direct-tool tests with a hand-built context are unchanged.
- [ ] 4.4 Stub superseded read results in the request and adjust `_historyUtf8` exactly; leave the session entry alone. Tests: the request carries the one-line notice and is at least 4,000 estimated tokens smaller; the session still loads the original; a resume seeds the ledger and the first edit asks for a re-read.
- [ ] 4.5 Commit: `feat(agent): refuse a stale read and stub its old copy`.

## 5. Masking

- [ ] 5.1 Add `AgentHarnessOptions.ToolOutputMask` and apply it to `Output` and `FullOutput` before the event, history, session and spill. Tests: the red test from 1.2; a null mask is identity; a mask is applied once and the at-rest redaction is idempotent.
- [ ] 5.2 Wire `HarnessFactory` (and `PrintMode`) to pass the session redactor's `Redact`, and to build the same exact-value redactor for a `--no-session` run. Tests: a session run and a no-session run both mask a provider key from the environment; a value Lunate does not hold is unchanged.
- [ ] 5.3 Commit: `feat(agent): mask tool output before it enters the context`.

## 6. Contracts, documentation and gate

- [ ] 6.1 Add the public entries to `src/Lunate.Agent/PublicAPI.Unshipped.txt` and confirm no other public type moved. The new public surface is exactly `ToolResult.FullOutput`, `AgentHarnessOptions.SpillThresholdTokens` and `AgentHarnessOptions.ToolOutputMask`; the ledger stays internal.
- [ ] 6.2 Update `docs/guide.md` where it describes tool-output truncation, redaction and the read-before-edit rule; name the new setting and the pointer. Do not touch `AGENTS.md`, `docs/architecture.md` or the ADRs other than the new addendum.
- [ ] 6.3 Present `design.md` and `adr/0012-addendum-context-hygiene.md` for maintainer sign-off. If either is rejected, stop apply and record the decision on the PR; do not merge a partial version of the boundary.
- [ ] 6.4 Run `openspec validate context-hygiene --strict` and `openspec validate --all`, then the fast gate: `dotnet csharpier format .`, `dotnet build Lunate.slnx -c Release`, the Agent and Coding test projects (both cultures), `dotnet csharpier check .`.
- [ ] 6.5 Check the golden session tests: no session format change is expected. Any golden diff means stop and report, per `AGENTS.md`; never update a golden to make this pass.
- [ ] 6.6 Commit: `docs: describe context admission and the spill pointer`; then run `scripts/verify.sh` before marking the change ready for review.
