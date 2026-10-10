# Tasks: Interactive session — wiring, steering, Esc (T-22, part 1)

## 1. Steering seam (Agent)

- [ ] 1.1 `SteeringQueue` (thread-safe; `Enqueue` + `TryDequeue` for the frontend) + `AgentHarnessOptions.Steering`; no steering when null
- [ ] 1.2 Loop injection: drain before each model request, after the batch's tool results; append message + session entry (parentId chain); emit `SteeringInjected(runId, entryId)`; falsifiers (never between call/result; top-level only; leftover stays)
- [ ] 1.3 Replay test: recorded session + steering at a fixed step reproduces byte for byte (existing replay conventions)
- [ ] 1.4 ADR-0013 dated amendment (steering seam) + status line note

## 2. Anthropic wire merge (Ai)

- [ ] 2.1 Merge consecutive user turns at wire time for the anthropic path in `ChatClientFactory`; openai path unchanged; history untouched
- [ ] 2.2 Unit tests (realistic shapes) + fixture-shaped tests using `anthropic-basic.jsonl` and `opencode-go-basic.jsonl`

## 3. Input pipeline + history (Coding)

- [ ] 3.1 `InputPipeline` over `RunInputReceivedAsync` (pass-through/transform/consume; null runner = pass-through); tests with a real HookRunner + scripted handler (transform seen by next handler; consume stops)
- [ ] 3.2 `InputHistory` (`~/.lunate/history`, JSONL, multiline-safe, tolerant of a corrupt line); Up/Down navigation semantics; tests

## 4. Interactive session (Coding)

- [ ] 4.1 Event pump: streaming tail + paragraph commit; tool blocks committed on result; usage → footer; notices (retrying/compaction/step-limit)
- [ ] 4.2 Approval adapter: `ApprovalRequested` → prompt in live area; `y`/`n`/`a`; session-scoped always-memory; input locked while open; round-trip test through `IToolApprover`
- [ ] 4.3 Steering interaction: Enter-idle runs / Enter-running enqueues through the pipeline; Esc returns leftover to input; normal finish auto-runs leftover; error/step-limit returns leftover; falsifier tests for each
- [ ] 4.4 Key wiring: router + Ctrl+C window + history on the real input loop (scripted `FakeConsoleIO`)

## 5. E2E + close

- [ ] 5.1 Scripted end-to-end snapshot: streaming→scrollback, tool block, approval round trip, mid-run steering, Esc cancel — goldens for scrollback + final frame
- [ ] 5.2 csharpier; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-interactive-session --type change --strict`; self-review; commits per group; no push
- [ ] 5.3 Deviations recorded in design.md; note the T-22 part 2 seams (commands/pickers entry points) and T-53 (@paths)
