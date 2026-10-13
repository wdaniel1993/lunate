# Tasks — sample-subagents

## 1. Runner core (`Lunate.Agent`)

- [ ] 1.1 `ISubagentRunner`, `SubagentRequest`, `SubagentResult` (public, XML-documented); `ToolContext.Subagents` init property (null outside a run); `AgentHarnessOptions`: `MaxSubagentDepth` (2), `MaxConcurrentSubagents` (4), `SubagentTokenBudget` (100 000).
- [ ] 1.2 `SubagentRunner`: child `AgentHarness` from the parent's live facts (D2); child session beside the parent + `AppendChildSession` before start + no-session path (`ChildSessionId` null); linked cancellation token; joins the child before returning.
- [ ] 1.3 Event forwarding: every child event `with { ParentRunId = parentRunId }` onto the parent channel; call ids untouched.
- [ ] 1.4 Limits: depth/concurrency/token (`UsageUpdated` summation; exceeding cancels the child) — refusals as teaching error results with `depth_limit`/`concurrency_limit`/`token_limit`; concurrency refused immediately when held.
- [ ] 1.5 `BuildToolContext` stamps `Subagents` (same place as `RunId`/`FileChanges`); PublicAPI.Unshipped updated for `Lunate.Agent`.

## 2. Consumer run-scoping

- [ ] 2.1 Interactive pump: call maps keyed by `(RunId, CallId)`; only the primary run's top-level calls take over the live tool indicator; child tool blocks commit normally.
- [ ] 2.2 Regression test: parent and child both emit call id `call_1` — no cross-talk in blocks, args, diffs or the live indicator.
- [ ] 2.3 Print mode: child events reach the JSONL with `runId`/`parentRunId` intact; resume of the parent renders the child-session link line; resume of the child loads its history.

## 3. Tests (`tests/Lunate.Agent.Tests`)

- [ ] 3.1 Runner units (scripted client): happy path (child output returned; child session file + parent link match the child's ids); depth refusal; concurrency refusal (parallel spawns); token-budget stop (`token_limit`, child cancelled); cancel propagation (`cancelled`, no hang); approval routed to the parent approver (recording approver: allow + deny); event forwarding (`ParentRunId` stamped, child ids intact); no-session path.
- [ ] 3.2 Interactive/print scoping tests from 2.x.

## 4. Sample extension (`samples/extensions/subagents`)

- [ ] 4.1 `SubagentsExtension`: tool `spawn` (`subagents__spawn`; schema `prompt` + optional `maxSteps`; annotations: not read-only, destructive-capable → asks); setting for the default `maxSteps` and description; manifest declares the tool.
- [ ] 4.2 Sample tests (own project, mirroring `mcp-from-extension`): registration/manifest; the four behaviours end to end (child events + link; approval routing; cancel; limits) through the real composition, plus the refusal teaching texts.
- [ ] 4.3 README: build, install, what it proves.

## 5. Docs

- [ ] 5.1 `docs/spec/extensibility.md`: subagents row (production path = `ToolContext.Subagents` + sample; four behaviours); fitness-suite wording.
- [ ] 5.2 `docs/guide.md`: T-47 row → part 2 done note (done at archive).

## 6. Verify, review, archive

- [ ] 6.1 `scripts/verify.sh` green (both cultures); `openspec validate --all --strict` green.
- [ ] 6.2 Adversarial review; fix findings; focused re-review.
- [ ] 6.3 Archive (`openspec archive sample-subagents --yes`); push; PR.
