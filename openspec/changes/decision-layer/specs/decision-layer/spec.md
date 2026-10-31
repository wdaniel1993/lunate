## ADDED Requirements

### Requirement: Decision primitive contract
The decision layer SHALL expose one primitive with four phases — propose, validate, decide, record — owned by the agent harness. A decision request SHALL name the usage being served (`decision`), the subject being decided (for example an approval ask or a model-routing choice), and the option set the subject admits. A decision result SHALL carry the chosen option, its probability, the full option-to-probability set, the resolved model id and the method label (`native` or `adapter`). The result SHALL be the only thing a consumer sees of the model exchange: never raw model text or off-contract probabilities.

#### Scenario: A request names a defined subject and option set
- **GIVEN** an approval ask entering the decision layer while a `decision` profile is configured
- **WHEN** the harness proposes the decision
- **THEN** the request names the `decision` usage, the approval-ask subject and the `{approve, deny}` option set

#### Scenario: Consumers see a validated result only
- **GIVEN** a model response for a proposed decision
- **WHEN** the response is off-contract (not valid JSON, an undefined option, a probability outside [0, 1], or probabilities that do not sum to 1 within contract tolerance)
- **WHEN** it is retried once and is off-contract again
- **THEN** consumers receive a failed-decision result naming the model and the reason, never the raw response and never a fabricated verdict

#### Scenario: A decision is a model exchange, not a verdict
- **GIVEN** a decision that failed twice as off-contract
- **WHEN** the result reaches consumers
- **THEN** it is presented as a failed advice request, not as an approval, denial or routing result

### Requirement: Option-set validity
The contract SHALL be defined over any option set the subject admits: a valid response contains exactly the defined options, one finite probability in [0, 1] per option, probabilities that sum to 1 within the contract tolerance (one part in a million), and a chosen option from that set carrying its own probability. The approval ask SHALL use the option set `{approve, deny}`; consumers of other subjects SHALL pass their own option sets to the same validity contract with no per-consumer validation code.

#### Scenario: A well-formed two-option response validates
- **GIVEN** a response naming option `approve` with probabilities `approve` 0.7 and `deny` 0.3
- **WHEN** the validator runs
- **THEN** the decision validates with the chosen option and its own probability

#### Scenario: An undefined option fails the contract
- **GIVEN** a response naming an option outside the request's option set
- **WHEN** the validator runs
- **THEN** the response is rejected as off-contract

#### Scenario: Probabilities must form a distribution
- **GIVEN** a response whose per-option probabilities do not sum to 1 within the contract tolerance, or a probability that is not a finite value in [0, 1]
- **WHEN** the validator runs
- **THEN** the response is rejected as off-contract before any consumer sees a probability

### Requirement: Decision availability and provenance
The decision layer SHALL be available only when a `decision` usage profile is configured, and SHALL report itself unavailable and stay off otherwise — it SHALL NOT borrow the chat profile or any other usage's model. Every proposed decision SHALL emit a channel event, and every validated decision SHALL record provenance: the model id and the method label (`native` for a provider decision API, `adapter` for a chat-model path), so an adapter decision cannot pass for a native one in `/cost` or the transcript.

#### Scenario: No decision profile keeps the layer off
- **GIVEN** a session with no `decision` usage profile configured
- **WHEN** a decision would be proposed for a subject that asked for one
- **THEN** the layer emits an unavailable event and proposes nothing, and no other usage's model is called

#### Scenario: Provenance is recorded on every validated decision
- **GIVEN** a validated decision from an adapter-profiled model
- **WHEN** the decision is recorded
- **THEN** the record names the resolved model id, the `adapter` method label, the option and its probability

### Requirement: Consumer surface
A decision consumer SHALL either observe or act, and activation SHALL be a user opt-in setting. Observers receive decisions only as channel events (`DecisionEvaluated`, `DecisionUnavailable`, `DecisionFailed`). An acting consumer SHALL be a role named as acting by its capability's requirements, applies a validated result to one action, and receives the result before the session acts on it — from the harness, not from a synchronous registry with call-back rights into the loop. In this scope model routing acts; every other consumer is a shadow or suggestion-only consumer.

#### Scenario: A front end observes through events
- **GIVEN** a configured decision whose validated result is recorded
- **WHEN** a front end (TUI, print mode, export) reads the event stream
- **THEN** it sees the option, probability, model id and method label on the decision event without calling the model or reading the primitive's internals

#### Scenario: Activation is opt-in off by default
- **GIVEN** a fresh configuration that did not opt into decision consumption
- **WHEN** the session asks for tool approvals
- **THEN** the decision layer proposes nothing, and no consumer applies any decision

#### Scenario: Nothing chosen through a question approves a call
- **GIVEN** a decision surfaced as a suggestion inside a question the user is answering
- **WHEN** the user's answer or an auto-answer is applied
- **THEN** the answer grants no tool approval; approving stays a separate approval prompt

### Requirement: Shadow-mode guarantees
A shadow consumer SHALL observe every decision it registered for and record what it would have done, and SHALL NOT change execution: it cannot stop, delay or redirect a tool call, change an approval outcome, change routing, suppress a prompt, or alter the session's recorded history beyond its own decision records and would-have records. Shadow output SHALL be presentable per method label so an adapter's shadow record cannot pass for a native one.

#### Scenario: Shadow records instead of acting
- **GIVEN** shadow-mode decision consumption is opted in for the approval gate
- **WHEN** a denied-by-the-decision-model ask arrives in shadow mode
- **THEN** the ask runs under the ordinary approval flow, and the record shows the would-have veto with its provenance

#### Scenario: Shadow changes nothing observable
- **GIVEN** a session with shadow consumption active and a `decision` profile configured
- **WHEN** the run's event stream and session file are compared against the same run with decision consumption off
- **THEN** they differ only by the decision events, usage attribution and would-have records

#### Scenario: Shadow failures do not change the flow
- **GIVEN** the decision layer is unavailable or a contract is invalid while shadow consumption is active
- **WHEN** the ask continues
- **THEN** the ordinary approval flow runs unimpeded and the failure is recorded for the shadow observer

### Requirement: Tighten-only enforcement
The invariant that a decision may only tighten the user's allow envelope SHALL be enforced in one place — the harness's approval seam — not once per consumer. A decision may veto an ask that the envelope would allow; it SHALL never override a denial, never waive a prompt, and never authorize a call the tool rules exclude from any decision model. No consumer, acting or shadow, can weaken the invariant from its own code.

#### Scenario: A deny decision may veto an allowed ask
- **GIVEN** an ask the user's allow envelope permits and an acted-on decision that chose `deny`
- **WHEN** the seam resolves the ask
- **THEN** the ask is refused with an instructing error result and no prompt is needed

#### Scenario: An approve decision cannot cross the seam
- **GIVEN** an ask that requires the user's approval and a decision that chose `approve`
- **WHEN** the seam resolves the ask
- **THEN** the approval prompt still runs; the decision alone grants nothing

#### Scenario: A failed decision may not loosen the gate
- **GIVEN** an acting consumer whose decision failed, was unavailable or was off-contract
- **WHEN** the seam resolves the ask
- **THEN** the ordinary approval flow answers the ask, and the failure cannot widen the envelope

### Requirement: Boundary between the loop and the executor
The harness loop SHALL own proposing, validating, deciding and recording, and the executing tool SHALL be reached only after the decision has resolved. A tool SHALL NOT be able to call the decision primitive, observe another tool's decision, or turn its own error into a decision failure. A decision failure SHALL surface as a tool error result produced by the harness, worded so the model knows what to do next, never as an exception out of the stream.

#### Scenario: The executor sees a resolved call
- **GIVEN** a tool call whose decision was resolved
- **WHEN** the tool executes
- **THEN** it receives only approved arguments and no decision request, result or failure

#### Scenario: A decision failure is a harness error result
- **GIVEN** an acting consumer whose decision fails twice as off-contract
- **WHEN** the harness resolves the ask
- **THEN** the failure becomes an instructing tool error result naming the failure, and the stream continues normally
