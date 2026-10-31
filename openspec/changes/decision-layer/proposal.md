## Why

Decision-making is currently split between the model call, the session loop, approval handling and
tool execution, and the only path that exists is approval-shaped. A general-purpose primitive — an
abstraction over the Jev-compatible decision API — is needed so approvals, questions, model routing
and the chat model itself can all ask for a judgement through one contract, while preserving the
user's approval boundary and keeping the decision source inspectable.

## What Changes

- Define the primitive as a mirror of the Jev-compatible API: a state (text or JSON) plus one or more
  named questions, evaluated independently, with three question kinds — yes/no, choice (up to 255
  options), score (ordered rubric levels) — an optional JSON-path target into the state, and a
  per-question result carrying the chosen answer, its probability, the full distribution and the
  exchange's provenance (`model id`, `native|adapter`).
- Define one technical validator over that contract, per kind, with retry-once-then-fail naming the
  model, so no consumer validates a response itself.
- Define the primitive as extension-facing surface: `IDecisionService` and its contract types in
  `Lunate.Extensibility.Abstractions`, implemented in `Lunate.Agent`, landing before #237 publishes
  Abstractions as stable. Front ends keep observing through `AgentEvent`.
- Name the 0.2 consumers, all opt-in and all through the same primitive: model routing (acting), the
  approval gate (acting, **escalate-only** — a deny recommendation turns an allowed call into a user
  ask, fail-closed when unattended, and an approve recommendation never skips a prompt), question
  suggestions (display only), and a `decide` tool for the chat model (information only, batch
  friendly, offered only when a decision profile is configured).
- Define shadow consumers that record what they would have done without changing execution, per
  acting consumer.
- Specify the non-negotiable safety rules: decisions only tighten the user's allow envelope; a
  question answer is never an approval; a skipped question is not auto-answered while a user is
  present; a `decide` result is information, never an approval.
- Keep this change design-only. Implementation and behavior tests follow this sign-off.

## Capabilities

### New Capabilities

- `decision-layer`: Shared request, validated per-question results, extension-facing service,
  consumer contract, shadow-mode guarantees, and safety invariants.

### Modified Capabilities

- `agent-loop`: The loop boundary for the primitive, the approval seam's escalate semantics, and the
  `decide` tool as an information-only caller.
- `agent-config`: Per-consumer opt-in activation and the configured `decision` usage profile,
  including the `decide` tool's availability rule.
- `agent-sessions`: Decision provenance within the already-approved schema-4 attribution model.

## Impact

Contracts affect `Lunate.Extensibility.Abstractions` (service and contract types, public API and
`ExtensionApi` bump), `Lunate.Agent` (primitive, validator, routing and approval consumers, `decide`
tool), `Lunate.Coding` (settings and `/cost`), and session attribution. `DecisionMode.Enforce`
changes meaning from veto to escalate. No new dependencies, no session-format change beyond the
merged schema-4 contract, and no implementation until this change is approved.
