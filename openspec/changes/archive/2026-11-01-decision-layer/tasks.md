## 1. Prerequisites

- [x] 1.1 Maintainer sign-off obtained (2026-10-31) — the scope correction and the four sign-off
      answers are recorded under `Sign-off` in `design.md`.
- [x] 1.2 `provider-store`'s schema-4 attribution delta is merged; this change references it and does
      not restate session behaviour.
- [x] 1.3 The duplicate ADR-0027 record is deleted and the numbering check is merged (#339/PR #372);
      `adr/0027-provider-store-model-profiles-and-usages.md` governs.

## 2. The contract in `Lunate.Extensibility.Abstractions`

- [x] 2.1 Write the failing tests first: a request carries a state and several named questions and
      returns keyed results; a yes/no answer is a probability with its complement; a choice question
      with one option or with 256 is refused before any model call; a score question keeps its rubric
      order; a JSON-path question evaluates that field.
- [x] 2.2 Add the request/question/result/answer types to `Lunate.Contracts` and `IDecisionService` to
      `Lunate.Extensibility.Abstractions` (three question kinds, question ids unique within a request,
      provenance repeated on every result). Add the public service to `PublicAPI.Unshipped.txt` and
      bump `ExtensionApi.Current`, since #237 publishes these assemblies as stable only after this lands.
- [x] 2.3 Write the failing validator tests per kind: undefined answer, probability outside [0, 1], a
      distribution that misses 1 by more than the tolerance, and retry-once-then-fail naming the
      model. One validator serves all three kinds.

## 3. The implementation in `Lunate.Agent`

- [x] 3.1 Implement the service and the shared validator; move `TryReadDecision` out of the approval
      path. `DecisionEvaluated`, `DecisionUnavailable` and `DecisionFailed` stay the observation
      surface, now carrying the question id and answer.
- [x] 3.2 Map the contract onto the model: a native provider passes the request through; the chat
      adapter translates all three kinds into one structured-output request and back, with
      `CultureInfo.InvariantCulture` for every formatted number.
- [x] 3.3 Bridge and register the Agent evaluator as `core/decision` through `Lunate.Extensibility` so
      an extension can resolve `IDecisionService`; add the layering test that Abstractions references
      only Contracts (not Agent) and the decision contract names no provider, tool or loop type.

## 4. The four consumers

- [x] 4.1 **Model routing** (acting): decide among the routing options through the shared validator,
      with a shadow setting that records the choice it would have made.
- [x] 4.2 **Approval gate** (acting, escalate-only): redefine `DecisionMode.Enforce` so a `deny`
      recommendation turns an allowed call into a user ask showing the decision and probability. Add
      additive escalation metadata to `ApprovalRequest` and a prompt-capability method without
      changing `IToolApprover`; the harness invokes that method directly, and denies with a no-user
      notice when no prompt capability exists. Keep `--yolo` unchanged without the gate and deny an
      escalated call when the gate is active. An `approve` recommendation changes nothing, and calls
      outside the approval seam (such as `ask_user`) are not evaluated. Move `DecisionApprovalTests`
      to these semantics, and test a prompt-capable approver that ignores the metadata,
      non-interactive fail-closed behavior and call-local escalation.
- [x] 4.3 **Question suggestions** (#344, display only): show a suggested answer beside a question the
      user is answering. Add the test that an answer — suggested, chosen or auto-filled with no user
      present — grants no tool approval and never answers a skipped question while a user is present.
- [x] 4.4 **`decide` tool** (information only): offered only when a `decision` profile is configured,
      batch-friendly (several questions per call), cost under the `decision` usage in `/cost`. Add the
      test that a decision received through the tool grants and skips no approval, and the test that
      the tool is absent without a decision profile.

## 5. Recording and surfaces

- [x] 5.1 Verify attribution: a `decision` usage records usage, profile, provider, resolved model id,
      method and probability exactly as schema 4 requires; nothing about the decision (state,
      questions, answers, path, provider configuration or secrets) is persisted beyond that contract.
- [x] 5.2 `/cost` shows decision spend under the `decision` usage, with the method label visible so an
      adapter decision cannot pass for a native one.

## 6. Validate and hand over

- [x] 6.1 Run `openspec validate decision-layer --type change --strict`, the touched test classes and
      the `scripts/verify.sh` profiles for the touched area; CI runs the full matrix.
- [x] 6.2 Sync the deltas to the main specs and archive as the final commit of the implementation PR,
      after the merge — merge first, then archive.
