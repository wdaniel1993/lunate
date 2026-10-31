## ADDED Requirements

### Requirement: Approval seam escalates through the decision layer

The agent loop SHALL consult the decision layer only at the approval seam, between a proposed tool
call and its execution, and SHALL apply the tighten-only invariant there rather than in any consumer
or tool. When the decision recommends `deny` for a call the allow envelope would permit, the loop
SHALL call a prompt-capable approver with escalation details, forcing the ordinary prompt even if
the approver ignores the escalation flag. The prompt SHALL show the recommendation, probability and
model id. When no user is present, including print mode, unattended queue runs, ACP without a client
prompt and `--yolo`, the loop SHALL deny the call, fail-closed, with a notice. With the opt-in gate,
`--yolo` SHALL auto-approve other calls and deny escalated calls; plain `--yolo` SHALL be unchanged.
An `approve` recommendation SHALL NOT change the outcome. Failed or unavailable decisions SHALL
leave the ordinary approval flow unchanged. Decision events emitted while the loop resolves an ask
SHALL belong to the asking run's event stream.

#### Scenario: An ask resolves through the single seam

- **GIVEN** a tool call that requires an approval ask while decision consumption is active
- **WHEN** the loop resolves the ask
- **THEN** the decision is proposed, validated and recorded during the ask, and the tightened outcome
  is applied at the seam before any tool runs

#### Scenario: Escalation becomes a user ask

- **GIVEN** a decision that recommends `deny` for an allowed call while a user is present
- **WHEN** the loop applies the decision
- **THEN** the user is asked, and no other tool call in the run is affected

### Requirement: The decide tool is an information-only caller

The loop SHALL offer a `decide` tool to the chat model only when a `decision` usage profile is
configured. The tool SHALL hand the primitive a state and one or more named questions in a single call
and return the per-question results. Its result SHALL be information: it SHALL NOT grant or skip an
approval, SHALL NOT answer a question the user was asked, and SHALL NOT alter the approval seam.

#### Scenario: The tool result changes no approval

- **GIVEN** a model that calls `decide` and then calls a tool the decision described
- **WHEN** the loop resolves that call
- **THEN** the approval path behaves exactly as it would have without the decision
