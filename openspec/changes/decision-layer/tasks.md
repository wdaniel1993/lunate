## 1. Prerequisites and maintainer sign-off

- [ ] 1.1 When the lane opens — #187, #184 and #186 are done first, per the 0.2 scope order comment on #183 — start this change; everything below waits for that order.
- [ ] 1.2 Obtain maintainer sign-off on the four blocking items in `openspec/changes/decision-layer/design.md` (`Sign-off (blocking)`), especially item 1: whether the existing approval-gate `DecisionMode.Enforce` path stays active, moves to shadow-only, or becomes config-gated in 0.2. Amend the `decision-layer` spec delta in this change (a MODIFIED requirement) if the outcome differs from the approved-scope wording.
- [ ] 1.3 Confirm the `provider-store` agent-sessions schema-4 delta has landed and synced, so this change never re-states session attribution that another spec already owns (sign-off item 2).

## 2. Extract the shared primitive in `Lunate.Agent`

- [ ] 2.1 Write the failing tests first: a contract-generic validator case set covering the two-option approval contract, an undefined option or an option outside the set, probabilities outside [0, 1] or not summing to 1 within tolerance, and retry-once-then-fail naming the model, pinned to the current `DecisionApprovalTests` behaviors.
- [ ] 2.2 Introduce the decision request and result contract types (usage, subject, option set; result with option, probability, full option-probability set, model id, `native|adapter` method) as `Lunate.Agent` types, and move `TryReadDecision` out of the approval path into the single shared validator; keep `DecisionEvaluated`, `DecisionUnavailable` and `DecisionFailed` as the consumer event surface. All model-facing and event text stays user- and model-facing English; any formatted number in output uses `CultureInfo.InvariantCulture`.

## 3. Consumer wiring

- [ ] 3.1 Write the failing tests first for consumer wiring: shadow consumption records the would-have result and never changes execution; an acting consumer receives a validated result before the session acts; a failed, unavailable or off-contract decision leaves the ordinary approval flow unimpeded and cannot widen the envelope.
- [ ] 3.2 Add opt-in settings for decision consumers (shadow first; the acting set per the sign-off outcome), validated by name so settings naming an undefined acting consumer are rejected before a session runs, and keeping `decision`-profile absence as report-unavailable-and-off.
- [ ] 3.3 Routing as an acting consumer per the approved 0.2 scope: pass the request's option set for the routing subject to the shared validator with no per-consumer validation code. If sign-off item 1 keeps the approval-gate `Enforce` path active, its seam behavior is covered by 3.1–3.2; if it moves to shadow-only, adjust it and the delta together.

## 4. Recording and public surface

- [ ] 4.1 Add `PublicAPI.Unshipped.txt` entries only for the contract types the sign-off (design item 4) declares public.
- [ ] 4.2 Verify session attribution: a `decision` usage records usage, profile, provider, resolved model id, method and probability exactly as the schema-4 contract requires; nothing about the decision (option set, subject, prompts, provider configuration or secrets) is persisted beyond that contract; golden-session comparisons stay byte-identical for non-decision traffic.

## 5. Validate and hand over

- [ ] 5.1 Run `openspec validate decision-layer --type change --strict`, the touched test classes, and `scripts/verify.sh` profiles that apply to the touched area; CI runs the full matrix.
- [ ] 5.2 Sync this change's deltas to the main specs and mark the change ready for review/verification (`opsx verify` then archive as the final commit of the implementation PR).
