## Why

Decision-making is currently split between the model call, the session loop, approval handling, and tool execution. A small shared contract is needed so different frontends can ask for and observe decisions consistently, while preserving the user's approval boundary and making the decision source inspectable.

## What Changes

- Define a Jev-shaped decision primitive: propose, validate, decide, and record, with one shared request/result contract.
- Define which decision data consumers receive, including option probabilities and model/method provenance.
- Define shadow consumers that record what they would have decided without changing execution.
- Define the boundary between decision policy, the session/approval path, and `ITool` execution; model routing is the only active decision consumer in this scope.
- Specify the non-negotiable safety rules: decisions only tighten the user's allow envelope; a question answer is never an approval; and a skipped question is not auto-answered while a user is present.
- Keep this change design-only. Implementation and behavior tests follow maintainer sign-off.

## Capabilities

### New Capabilities
- `decision-layer`: Shared decision request, validated proposal, recorded result, consumer contract, shadow-mode guarantees, and safety invariants.

### Modified Capabilities
- `agent-loop`: Identify the loop boundary for routing decisions and approval tightening without delegating tool execution.
- `agent-config`: Specify opt-in activation and the configured decision model usage.
- `agent-sessions`: Specify decision provenance recording within the already-approved session attribution model.

## Impact

Design and future behavior contracts affect `Lunate.Agent` (decision primitive, loop and consumers), `Lunate.Ai` (configured model adapter), configuration and session attribution. This proposal adds no dependencies, public API implementation, code, tests, or session-format change; any such implementation follows sign-off and the existing ADR-0027/schema-4 decisions.
