# extensions — delta for agent-skills

## ADDED Requirements

### Requirement: Skill sources from extensions

Extensions SHALL contribute skills through a new `ISkillSource` service registered like other extension services: an id plus an enumeration of descriptors (name, description, path or inline body). Contributed skills SHALL appear in the skills index and `/skills` tagged `extension '<id>'`, SHALL count under the contributing extension's context budget, and SHALL be trusted exactly as the extension is trusted (no separate question). A malformed descriptor SHALL warn like a core skill and SHALL NOT load. The ADR-0017 catalogue and `docs/spec/extensibility.md` SHALL gain the resource-discovery entry; the record lands as `adr/0017-addendum-skills.md`.

#### Scenario: A contributed skill joins the index tagged by source

- **GIVEN** a trusted extension registering a `ISkillSource` with one descriptor
- **WHEN** a session starts
- **THEN** the index and `/skills` list the skill with `extension '<id>'` as its scope

#### Scenario: Over-budget contributions drop and log

- **GIVEN** an extension whose contributed index lines exceed its context budget
- **WHEN** the session starts
- **THEN** exceeding lines are dropped and an extension log entry names the extension and the budget

#### Scenario: An untrusted extension contributes nothing

- **GIVEN** an untrusted project extension registering a skill source
- **WHEN** the session starts
- **THEN** its skills are absent from the index and `/skills`
