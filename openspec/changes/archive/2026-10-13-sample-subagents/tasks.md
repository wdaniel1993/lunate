# Tasks — sample-subagents

## 1. Runner core (`Lunate.Agent`)

- [x] 1.1 `ISubagentRunner`, `SubagentRequest` (Prompt, MaxSteps, AllowedTools), `SubagentResult` (child session id, run id, output, stop reason, error flag, tokens used) — public, XML-documented; `ToolContext.Subagents` init property (null outside a run).
- [x] 1.2 `SubagentRunner`: child `AgentHarness` from the parent's live facts (D2); **tool allow-list** — subset of the run's own tools by name, unknown name refuses the spawn, null = full set; per-spawn values clamp (never loosen).
- [x] 1.3 Child session: file beside the parent's + `AppendChildSession` before start + no-session path (`ChildSessionId` null); linked cancellation token; joins the child before returning.
- [x] 1.4 Event forwarding: every child event `with { ParentRunId = parentRunId }` onto the parent channel; call ids untouched; **hook payloads during a child run carry the child run id + parent run id**.
- [x] 1.5 Limits: **depth fixed at 1** (depth-1 runner refuses every spawn); **tree-wide concurrency** (shared tree state, refused immediately while held); **per-child + per-tree token budgets** checked **before each model call** (loop seam) → `token_limit` + **partial result**; refusals name the limit **and the setting key**.
- [x] 1.6 Approval seam carries the asking run (run id, parent run id, depth); "always for this session" is session/tree-wide (interactive approver memory unchanged in scope, now exercised from child runs).
- [x] 1.7 Settings: `subagents` section (`concurrency`, `tokenBudget`, `treeTokenBudget`, `unlimited` literal) parsed + validated like existing keys; defaults 4 / 100 000 / 500 000; one-way clamp rule for per-spawn (and future project-scope) values.
- [x] 1.8 `BuildToolContext` stamps `Subagents`; PublicAPI.Unshipped updated for `Lunate.Agent`.

## 2. Frontends (`Lunate.Coding`, `Lunate.Tui`)

- [x] 2.1 Interactive pump: call maps keyed by `(RunId, CallId)`; **child text and child tool blocks hidden**; live area shows `subagent running` while a child is live; child `UsageUpdated` still rolls into the footer.
- [x] 2.2 `/context`: resolved subagent limits + current tree usage lines added to the command.
- [x] 2.3 Print mode: human renderer suppresses child text; JSONL keeps child events with `runId`/`parentRunId`.
- [x] 2.4 ACP: child-run events filtered from `session/update` (never raw); approvals unaffected.
- [x] 2.5 Regression test: parent and child both emit call id `call_1` — no cross-talk in blocks, args, diffs, footer or the live indicator.

## 3. Tests (`tests/Lunate.Agent.Tests` + Coding)

- [x] 3.1 Runner units (scripted client): happy path (child output + usage; child session file + parent link match); allow-list subset + unknown-name refusal; depth refusal (child cannot spawn); tree-wide concurrency refusal (parallel spawns); per-child budget stop (`token_limit`, partial result) and pre-call check timing; tree budget exhaustion (spawn refusal + running child stops); cancel propagation (`cancelled`, no hang); approval routed with asking-run context (allow + deny) and tree-wide "always"; hooks carry `ParentRunId`; no-session path.
- [x] 3.2 Frontend tests from 2.x (pump suppression + status, `/context` lines, print suppression + JSONL, ACP filter).
- [x] 3.3 Replay determinism (D13): fixture parent + child session files replay identically across loads; each child loads its own history.

## 4. Sample extension (`samples/extensions/subagents`)

- [x] 4.1 `SubagentsExtension`: tool `spawn` (`subagents__spawn`; schema `prompt` + optional `maxSteps` + optional `tools` allow-list; annotations: not read-only, destructive-capable → asks); setting for the default `maxSteps` and description; manifest declares the tool.
- [x] 4.2 Sample tests (own project, mirroring `mcp-from-extension`): registration/manifest; the behaviours end to end (child events + link + hook `ParentRunId`; approval routing + labels; cancel; depth/concurrency/token refusals; token-limit partial result; happy path) through the real composition.
- [x] 4.3 README: build, install, what it proves.

## 5. Docs

- [x] 5.1 `docs/spec/extensibility.md`: subagents row (production path = `ToolContext.Subagents` + sample; four behaviours; limits); fitness-suite wording.
- [x] 5.2 Settings documentation: the `subagents` keys where the existing keys are documented.
- [ ] 5.3 `docs/guide.md`: T-47 row → part 2 done note (done at archive).

## 6. Verify, review, archive

- [ ] 6.1 `scripts/verify.sh` green (both cultures); `openspec validate --all --strict` green.
- [ ] 6.2 Adversarial review; fix findings; focused re-review.
- [ ] 6.3 Archive (`openspec archive sample-subagents --yes`); push; PR.
