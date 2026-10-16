# extensions — delta for agent-skills

## ADDED Requirements

### Requirement: Skill sources from extensions

Extensions SHALL contribute skills through a new `ISkillSource` service registered like other extension services: an id plus an enumeration of skill directories **on disk inside the extension's own folder** (v1 is disk-only — no in-memory skills; the core scans and validates them exactly like any other skill). Contributed skills SHALL appear in the skills index and `/skills` tagged `extension '<id>'`, SHALL inherit the extension's trust (no separate question) **and its disable state** (`disable-model-invocation` honoured), and SHALL count under the contributing extension's context budget (enforced before the global index cap). A malformed descriptor SHALL warn like a core skill and SHALL NOT load. A shadowed extension skill follows the common collision rule: the winner carries the `shadows` marker, the one-time warning names it, and the extension copy is not listed separately. The ADR-0017 catalogue and `docs/spec/extensibility.md` SHALL gain the resource-discovery entry; the record lands as `adr/0017-addendum-skills.md`.

#### Scenario: A contributed skill joins the index tagged by source

- **GIVEN** a trusted extension registering an `ISkillSource` with one skill directory inside its folder
- **WHEN** a session starts
- **THEN** the index and `/skills` list the skill with `extension '<id>'` as its scope, and the read tool can read its files

#### Scenario: Over-budget contributions drop and log

- **GIVEN** an extension whose contributed index lines exceed its context budget
- **WHEN** the session starts
- **THEN** exceeding lines are dropped and an extension log entry names the extension and the budget

#### Scenario: An untrusted extension contributes nothing

- **GIVEN** an untrusted project extension registering a skill source
- **WHEN** the session starts
- **THEN** its skills are absent from the index and `/skills`
