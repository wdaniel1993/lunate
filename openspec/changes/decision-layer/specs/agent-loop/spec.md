## ADDED Requirements

### Requirement: Approval seam owns decision enforcement
The agent loop SHALL consult the decision layer only at the approval seam, between a proposed tool call and its execution, and SHALL apply the tighten-only invariant in that seam rather than in any consumer or tool. Decision events emitted while the loop resolves an ask SHALL belong to the asking run's event stream; the ordinary approval outcome SHALL remain the approver's, and a decision can only tighten it.

#### Scenario: An ask resolves through the single seam
- **GIVEN** a tool call that requires an approval ask while decision consumption is active
- **WHEN** the loop resolves the ask
- **THEN** the decision is proposed, validated and recorded during the ask, and the tightened outcome is applied at the approval seam before any tool runs

#### Scenario: A decision vetoes only an ask
- **GIVEN** a decision that chose `deny` for an ask the allow envelope would have permitted
- **WHEN** the loop applies the tightened outcome
- **THEN** the tool call is refused with the harness's instructing denial, and no other tool call in the run is affected
