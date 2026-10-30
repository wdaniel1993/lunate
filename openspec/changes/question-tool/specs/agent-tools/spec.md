# agent-tools Specification Delta

## ADDED Requirements

### Requirement: Ask the user for a decision

The agent SHALL expose an `ask_user` tool for decisions that belong to the user. Its hand-authored schema SHALL accept 1–4 questions, each with a short header, 2–4 options whose labels have one-line descriptions, single- or multi-select mode, at most one optional recommended option, and a free-text response path. The question fields SHALL map to the MCP form-elicitation model (`message` plus a flat `requestedSchema` object of primitive fields and enum choices) without private protocol extensions, so both inputs use the same normalized question interaction. A recommended option SHALL be first and visibly marked, but SHALL NOT be treated as an answer until the user submits it. The result SHALL preserve each question's chosen option label or labels and any entered text; it SHALL distinguish an answered response from a skipped or unavailable one.

#### Scenario: A user answers several questions with labels and text

- **GIVEN** the model asks two valid questions, one single-select and one multi-select
- **WHEN** the user chooses labels and adds free text to either answer
- **THEN** the result associates the chosen labels and text with their question headers

#### Scenario: A recommended option is displayed first without being accepted

- **GIVEN** one option is marked recommended
- **WHEN** the question is presented
- **THEN** that option is first and marked recommended, and the result remains unanswered until submission

#### Scenario: Free text is available with selected options

- **GIVEN** a question with labelled options
- **WHEN** the user supplies custom text, with or without selecting options
- **THEN** the result includes the text and any selected labels without substituting one for the other

#### Scenario: Invalid question counts and option counts are rejected

- **GIVEN** a request with no questions, more than four questions, fewer than two options, or more than four options
- **WHEN** `ask_user` is called
- **THEN** it returns an instructing tool error and presents no prompt

### Requirement: Question wording is for user-owned decisions

The `ask_user` tool description SHALL tell the model to ask only for decisions that belong to the user, SHALL discourage asking permission to proceed, and SHALL discourage asking for facts the model can discover with its tools.

#### Scenario: The tool description limits when to ask

- **GIVEN** the model reads the `ask_user` description
- **WHEN** it considers asking permission to continue or asking for information it can discover itself
- **THEN** the description directs it not to use `ask_user` for either purpose

### Requirement: Question answers never approve tools

The `ask_user` call SHALL use the question interaction instead of the tool-approval path. Its answer SHALL be information only and SHALL NOT create, satisfy, or modify a pending tool approval, an approval policy, or a remembered approval grant; tool approvals SHALL remain on their separate approval path.

#### Scenario: Asking a question does not ask permission to ask

- **GIVEN** an interactive run using the normal ask approval policy
- **WHEN** the model calls `ask_user`
- **THEN** the question picker opens without a tool-approval prompt

#### Scenario: An affirmative answer does not authorize a command

- **GIVEN** the user answers `Yes` to an `ask_user` question and the model then calls `bash`
- **WHEN** the command reaches the approval boundary
- **THEN** the normal approval prompt is presented for the command and the command has not run

### Requirement: Subagents cannot ask the user

In v1, `ask_user` SHALL NOT be exposed to a subagent run, and a subagent question SHALL NOT be bubbled to the parent run's user interaction.

#### Scenario: A subagent cannot call the question tool

- **GIVEN** a subagent run whose parent has `ask_user` available
- **WHEN** the subagent inspects its callable tools or attempts `ask_user`
- **THEN** the tool is unavailable, no question is presented, and no question is forwarded to the parent
