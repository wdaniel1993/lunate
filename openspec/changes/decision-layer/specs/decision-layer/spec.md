## ADDED Requirements

### Requirement: Decision primitive contract

The decision layer SHALL expose one primitive with four phases — propose, validate, decide, record —
owned by the agent harness and modelled on the Jev-compatible decision API. Its request, question,
answer and result DTOs SHALL live in `Lunate.Contracts`; a request SHALL carry a
**state** (free text, or a JSON value) and **one or more named questions** evaluated independently
against that state. Results SHALL be keyed by question id, and question ids SHALL be unique within a
request. A result SHALL carry the chosen answer, that answer's probability, the full distribution
over the question's answers, the resolved model id and the method label (`native` or `adapter`). The
result SHALL be the only thing a caller sees of the model exchange: never raw model text or
off-contract probabilities.

#### Scenario: A request carries a state and several named questions

- **GIVEN** a caller that supplies a state and two named questions
- **WHEN** the harness proposes the decision
- **THEN** both questions are evaluated against that same state and each result is returned under its
  own question id

#### Scenario: Callers see validated results only

- **GIVEN** a model response that is off-contract (not valid JSON, an undefined answer, a probability
  outside [0, 1], or probabilities that do not sum to 1 within contract tolerance)
- **WHEN** it is retried once and is off-contract again
- **THEN** the caller receives a failed-decision result naming the model and the reason, never the raw
  response and never a fabricated answer

#### Scenario: A decision is an exchange, not a verdict

- **GIVEN** a decision that failed twice as off-contract
- **WHEN** the result reaches a consumer
- **THEN** it is presented as a failed advice request, not as an approval, denial, answer or routing
  result

### Requirement: Three question kinds

The primitive SHALL support exactly three question kinds in this scope: **yes/no** (a probability that
the answer is yes, the no probability being its complement), **choice** (between 2 and 255 named
options, one probability per option) and **score** (an ordered rubric of at least two named levels,
one probability per level, where the order belongs to the question). A question MAY target a JSON path
inside a JSON state; the target SHALL be resolved against that state before the exchange and SHALL NOT
be persisted.

#### Scenario: A choice question reports per-option probabilities

- **GIVEN** a choice question with options `small`, `medium` and `large`
- **WHEN** the decision validates
- **THEN** the result names the chosen option, its probability, and all three option probabilities

#### Scenario: An option count outside the contract is refused

- **GIVEN** a choice question with one option, or with 256 options
- **WHEN** the request is proposed
- **THEN** it is refused as malformed before any model call is made

#### Scenario: A score question preserves rubric order

- **GIVEN** a score question with ordered levels `reject`, `weak`, `strong`
- **WHEN** the result is presented
- **THEN** the levels appear in the question's declared order, and the chosen level carries its own
  probability

#### Scenario: A JSON-path question evaluates one field

- **GIVEN** a JSON state and a question targeting `/diff/header`
- **WHEN** the decision is proposed
- **THEN** the exchange is about that field, and the request records no path beyond the exchange

### Requirement: Contract validity

One validator SHALL check every response against the question that produced it: the chosen answer
SHALL be one of the question's defined answers, every defined answer SHALL carry one finite
probability in [0, 1], the probabilities SHALL sum to 1 within the contract tolerance (one part in a
million), and the chosen answer's probability SHALL be its own. A malformed response SHALL be retried
once and then reported as failed, naming the model and the reason. No consumer SHALL validate a
response itself.

#### Scenario: A well-formed response validates

- **GIVEN** a yes/no question and a response naming `yes` with probability 0.7 and `no` with 0.3
- **WHEN** the validator runs
- **THEN** the decision validates with the chosen answer and its own probability

#### Scenario: An undefined answer fails the contract

- **GIVEN** a response naming an answer outside the question's defined answers
- **WHEN** the validator runs
- **THEN** the response is rejected as off-contract

#### Scenario: Probabilities must form a distribution

- **GIVEN** per-answer probabilities that do not sum to 1 within tolerance, or a probability that is
  not a finite value in [0, 1]
- **WHEN** the validator runs
- **THEN** the response is rejected as off-contract before any consumer sees a probability

### Requirement: Extension-facing service

The primitive SHALL be available to extensions as a service: `IDecisionService` SHALL live in
`Lunate.Extensibility.Abstractions`, the evaluator SHALL live in `Lunate.Agent`, and
`Lunate.Extensibility` SHALL bridge and register the service so an extension can resolve it. The
Abstractions assembly SHALL reference `Lunate.Contracts` and SHALL NOT reference `Lunate.Agent`.
Front ends SHALL keep observing decisions through the agent event channel
(`DecisionEvaluated`, `DecisionUnavailable`, `DecisionFailed`). The public shape SHALL land before
#237 publishes `Lunate.Extensibility.Abstractions` as stable.

#### Scenario: An extension requests a decision

- **GIVEN** an extension that resolved `IDecisionService` while a `decision` profile is configured
- **WHEN** it proposes a request with a state and one question
- **THEN** it receives the validated per-question result, and the decision appears in the run's event
  stream like any other

#### Scenario: The extension service preserves the assembly boundary

- **GIVEN** the decision service in `Lunate.Extensibility.Abstractions` and decision DTOs in `Lunate.Contracts`
- **WHEN** the layering test runs
- **THEN** Abstractions references Contracts but not Agent, and no decision contract type names a
  provider, a tool or the loop

### Requirement: Decision availability and provenance

The decision layer SHALL be available only when a `decision` usage profile is configured, and SHALL
report itself unavailable and stay off otherwise — it SHALL NOT borrow the chat profile or any other
usage's model. Every proposed decision SHALL emit a channel event, and every validated decision SHALL
record provenance: the model id and the method label (`native` for a provider decision API, `adapter`
for a chat-model path), so an adapter decision cannot pass for a native one in `/cost` or the
transcript.

#### Scenario: No decision profile keeps the layer off

- **GIVEN** a session with no `decision` usage profile configured
- **WHEN** a consumer proposes a decision
- **THEN** the layer emits an unavailable event and proposes nothing, and no other usage's model is
  called

#### Scenario: Provenance is recorded on every validated decision

- **GIVEN** a validated decision from an adapter-profiled model
- **WHEN** the decision is recorded
- **THEN** the record names the resolved model id, the `adapter` method label, the answer and its
  probability

### Requirement: Consumers

Decision consumption SHALL be opt-in per consumer, and every consumer SHALL use the same primitive.
This scope SHALL define exactly four consumers: **model routing** (acting), the **approval gate**
(acting, escalate-only), **question suggestions** (display only), and the **`decide` tool**
(information only). Observers SHALL receive decisions as channel events. Shadow mode SHALL be
available for each acting consumer and SHALL record what it would have done without acting.

#### Scenario: Activation is opt-in and off by default

- **GIVEN** a fresh configuration that did not opt into decision consumption
- **WHEN** the session asks for tool approvals
- **THEN** the decision layer proposes nothing and no consumer applies any decision

#### Scenario: A front end observes through events

- **GIVEN** a configured decision whose validated result is recorded
- **WHEN** a front end (TUI, print mode, export) reads the event stream
- **THEN** it sees the answer, probability, model id and method label without calling the model or
  reading the primitive's internals

#### Scenario: Nothing chosen through a question approves a call

- **GIVEN** a decision surfaced as a suggestion inside a question the user is answering
- **WHEN** the user's answer or an auto-answer is applied
- **THEN** the answer grants no tool approval; approving stays a separate approval prompt

### Requirement: Approval gate escalates, never blocks

The approval-gate consumer SHALL act only by escalation. A `deny` recommendation on a call the user's
allow envelope would permit SHALL turn that call into a user ask that shows the decision and its
probability; it SHALL NOT silently refuse the call. In an unattended run escalation SHALL be treated
as a denial, fail-closed, with a notice that no user was present. An `approve` recommendation SHALL
NOT skip a prompt, waive a denial, or authorize a call the tool rules exclude from any decision model.

#### Scenario: A deny recommendation becomes a user ask

- **GIVEN** an allowed call and a decision that recommends `deny` while a user is present
- **WHEN** the approval seam resolves the call
- **THEN** the user is asked, the decision and its probability are shown, and the user's answer decides

#### Scenario: Unattended escalation fails closed

- **GIVEN** the same recommendation with no user present
- **WHEN** the seam resolves the call
- **THEN** the call is denied, the session continues, and a notice records that the decision escalated
  with no user present

#### Scenario: An approve recommendation grants nothing

- **GIVEN** a call that requires the user's approval and a decision that recommends `approve`
- **WHEN** the seam resolves the call
- **THEN** the approval prompt still runs

### Requirement: The decide tool is information only

The chat model SHALL be able to call a `decide` tool that hands the primitive a state and one or more
named questions and returns the per-question results, batched in a single call. The tool SHALL be
offered only when a `decision` usage profile is configured, its cost SHALL appear under the `decision`
usage in `/cost`, and its result SHALL be information for the model — never an approval, never an
answer to a question the user was asked, and never a substitute for the approval prompt.

#### Scenario: One call returns several answers

- **GIVEN** a configured `decide` tool and a call with a state and three questions
- **WHEN** the tool returns
- **THEN** the model receives three keyed results, each with its answer, probability and distribution

#### Scenario: A decision taken through the tool approves nothing

- **GIVEN** a model that calls `decide` and receives an `approve` answer for a state describing a tool
  call
- **WHEN** the model then calls that tool
- **THEN** the ordinary approval path runs exactly as it would have without the decision, and no
  approval is granted or skipped

#### Scenario: The tool is absent without a decision profile

- **GIVEN** a session with no `decision` usage profile configured
- **WHEN** the tool list is assembled
- **THEN** the `decide` tool is not offered

### Requirement: Shadow-mode guarantees

A shadow consumer SHALL observe every decision it registered for and record what it would have done,
and SHALL NOT change execution: it cannot stop, delay or redirect a tool call, change an approval
outcome, change routing, suppress a prompt, or alter the session's recorded history beyond its own
decision records and would-have records. Shadow output SHALL be presentable per method label so an
adapter's shadow record cannot pass for a native one.

#### Scenario: Shadow records instead of acting

- **GIVEN** shadow-mode consumption is opted in for the approval gate
- **WHEN** an ask the decision model would escalate arrives
- **THEN** the ask runs under the ordinary approval flow, and the record shows the would-have
  escalation with its provenance

#### Scenario: Shadow changes nothing observable

- **GIVEN** a session with shadow consumption active and a `decision` profile configured
- **WHEN** the run's event stream and session file are compared against the same run with decision
  consumption off
- **THEN** they differ only by decision events, usage attribution and would-have records

#### Scenario: Shadow failures do not change the flow

- **GIVEN** the decision layer is unavailable or a contract is invalid while shadow consumption is
  active
- **WHEN** the ask continues
- **THEN** the ordinary approval flow runs unimpeded and the failure is recorded for the observer

### Requirement: Tighten-only enforcement

The invariant that a decision may only tighten the user's allow envelope SHALL be enforced in one
place — the harness's approval seam — not once per consumer. A decision may escalate an ask the
envelope would allow; it SHALL never override a denial, never waive a prompt, and never authorize a
call the tool rules exclude from any decision model. No consumer, acting or shadow, can weaken the
invariant from its own code.

#### Scenario: A failed decision may not loosen the gate

- **GIVEN** an acting consumer whose decision failed, was unavailable or was off-contract
- **WHEN** the seam resolves the ask
- **THEN** the ordinary approval flow answers the ask, and the failure cannot widen the envelope

#### Scenario: A decision cannot authorize an excluded call

- **GIVEN** a call the tool rules exclude from any decision model
- **WHEN** a decision recommends `approve` for it
- **THEN** the call still requires the user's explicit approval

### Requirement: Boundary between the loop and the executor

The harness loop SHALL own proposing, validating, deciding and recording, and the executing tool SHALL
be reached only after the decision has resolved. A tool SHALL NOT observe another tool's decision
records or turn its own error into a decision failure. A decision failure reaching the loop SHALL
surface as an instructing error result produced by the harness, never as an exception out of the
stream.

#### Scenario: The executor sees a resolved call

- **GIVEN** a tool call whose decision was resolved
- **WHEN** the tool executes
- **THEN** it receives only approved arguments and no decision request, result or failure
