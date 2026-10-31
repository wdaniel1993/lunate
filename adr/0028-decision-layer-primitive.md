# ADR-0028: The decision layer is a general primitive, not an approval feature

- Status: proposed — 2026-10-31 (sign-off scope correction on #183)
- Date: 2026-10-31
- Relates to: ADR-0002 (layering and MEAI model types), ADR-0003 (loop ownership),
  ADR-0017 (extensibility architecture), ADR-0027 (provider store, model profiles and usages)

## Context

The decision path in `Lunate.Agent` is approval-shaped: `DecisionMode.Enforce` lets a decision model
veto an approval ask, and the contract is a fixed `{approve, deny}` option pair. That shape has three
problems:

- it makes the primitive's contract an approval contract, so a second consumer (routing, a question
  suggestion, or a model asking for a judgement) would either fork it or re-derive validation;
- it hides the capability from extensions, which can observe decisions through events but cannot ask
  for one;
- its `Enforce` mode silently refuses calls the user's allow envelope permitted, which is a stricter
  power than the user granted when they configured a decision model.

## Decision

The decision layer is a **general-purpose primitive**, an abstraction over the Jev-compatible decision
API. Approvals, questions, model routing and a model-facing tool are consumers of it.

1. **Contract.** A request carries a state (text or JSON) and one or more named questions evaluated
   independently, optionally targeting a JSON path in the state. Three kinds: yes/no, choice (2–255
   options) and score (ordered rubric levels). A result per question carries the chosen answer, its
   probability, the full distribution and the exchange's provenance (`model id`, `native|adapter`).
   Native providers receive the request; adapters translate all three kinds onto a chat model's
   structured output. One contract, no per-consumer code.
2. **Surface.** The contract and `IDecisionService` live in `Lunate.Extensibility.Abstractions`; the
   implementation lives in `Lunate.Agent` and is registered in its service provider, so extensions
   can request decisions. Front ends keep observing through `AgentEvent`. This lands before #237
   publishes Abstractions as stable.
3. **Consumers in 0.2**, all opt-in, all through the primitive: model routing (acting), the approval
   gate (acting, **escalate-only**), question suggestions (display only) and a `decide` tool for the
   chat model (information only).
4. **Escalation, not veto.** A `deny` recommendation on an allowed call turns it into a user ask that
   shows the decision; unattended runs fail closed with a notice; an `approve` recommendation never
   skips a prompt. The user remains the only party who can widen the envelope.
5. **Invariants unchanged.** Decisions only tighten approvals; a question answer is never an
   approval; contract validation is technical; provenance is recorded on every decision; no decision
   profile means no decision layer.

## Consequences

- `DecisionMode.Enforce` changes meaning from veto to escalate. The setting is opt-in and the change
  is recorded in the release notes; `DecisionApprovalTests` moves to the new semantics.
- `Lunate.Extensibility.Abstractions` gains a public service and contract types, with an
  `ExtensionApi` bump, before its stability promise in #237.
- A chat model can ask for a judgement through a tool, which is a new cost line under the `decision`
  usage; the tool returns information and grants nothing.
- A consumer that needs a shape the three kinds do not express adds a kind or a vocabulary to this
  primitive rather than a parallel contract.
