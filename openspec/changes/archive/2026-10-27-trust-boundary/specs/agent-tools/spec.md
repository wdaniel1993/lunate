## MODIFIED Requirements

### Requirement: Tool namespaces and annotations
`ITool` SHALL gain optional `Namespace` (`Name`, `Description`, `Instructions`) and `Annotations` (`ReadOnly`, `Destructive`, `Idempotent`, `OpenWorld`; all default false) as default interface members. `ToolRisk` SHALL stay an explicit override; without one it SHALL derive from annotations: a tool that declares `ReadOnly` and neither `Destructive` nor `OpenWorld` SHALL be `ReadOnly`; a tool that declares no annotations, or declares `Destructive` or `OpenWorld`, SHALL be `Execute`; everything else SHALL be `Write`.

#### Scenario: Risk derives from annotations
- **GIVEN** a tool without an explicit risk that declares `ReadOnly`
- **WHEN** its risk is read
- **THEN** it is `ReadOnly`

#### Scenario: An explicit risk wins
- **GIVEN** a tool with an explicit `Execute` risk and `ReadOnly` annotations
- **WHEN** its risk is read
- **THEN** it is `Execute`

#### Scenario: Destructive or open-world work is execute risk
- **GIVEN** a tool without an explicit risk that declares `Destructive`
- **WHEN** its risk is read
- **THEN** it is `Execute`

#### Scenario: An unannotated tool is execute risk
- **GIVEN** a tool without an explicit risk and without annotations
- **WHEN** its risk is read
- **THEN** it is `Execute`

## ADDED Requirements

### Requirement: High-risk calls are never auto-approved

A tool call SHALL be high-risk when it runs `lunate trust`, `lunate install` or `lunate uninstall` through the bash tool, or when a bash command, write or edit targets the state root (`LUNATE_HOME`, default `~/.lunate`), resolving the configured value rather than matching the literal `~`. A high-risk call SHALL always ask the user under the ask and auto-edit policies, and SHALL NOT be allowed by an allowlist, by a remembered allow-always grant, or by a future decision model. Under `--yolo` the approval panel is off by design; in that mode the CLI SHALL print at startup that it disables the approval boundary, and the call runs without a panel.

#### Scenario: `lunate trust` through the bash tool asks under auto-edit
- **GIVEN** an interactive session under the auto-edit policy
- **WHEN** the model runs `lunate trust --yes` through the bash tool
- **THEN** the approval panel is shown and the command runs only if the user allows it

#### Scenario: A write inside the state root asks under auto-edit
- **GIVEN** an interactive session under the auto-edit policy
- **WHEN** the model writes a file under `LUNATE_HOME`
- **THEN** the approval panel is shown even though the write tool is otherwise allowed to run

#### Scenario: A remembered grant does not cover a high-risk call
- **GIVEN** a session where the user chose allow-always for the bash tool
- **WHEN** the model runs `lunate install` through it
- **THEN** the panel is shown again

#### Scenario: `--yolo` announces that the boundary is off
- **GIVEN** a run started with `--yolo`
- **WHEN** it starts
- **THEN** it prints one line saying the approval boundary is disabled
