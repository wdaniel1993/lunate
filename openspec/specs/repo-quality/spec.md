# repo-quality Specification

## Purpose
Repository quality conventions: one deterministic C# formatter (CSharpier) that yields identical output for humans and models, documentation linting (markdownlint) in the verify gate, contribution templates, and a documentation map and writing guideline that tell every session where each kind of document lives.

## Requirements

### Requirement: Deterministic C# formatting
The repository SHALL format all C# code with CSharpier, pinned as a local dotnet tool in `.config/dotnet-tools.json` and configured by a committed `.csharpierrc.yaml`; the verify gate on both platforms SHALL check it (`dotnet csharpier check .`). Analyzer and style checks SHALL continue through `dotnet format style` and `dotnet format analyzers` with whitespace formatting and `IDE0055` disabled.

#### Scenario: Unformatted file fails the gate
- **GIVEN** a C# file that differs from CSharpier's output
- **WHEN** the verify gate runs
- **THEN** the format step fails and names the file

#### Scenario: Formatted tree passes
- **GIVEN** a tree formatted by CSharpier
- **WHEN** the verify gate runs
- **THEN** the format step passes

### Requirement: Documentation lint
Markdown documentation SHALL be linted with markdownlint-cli2 (pinned version, committed configuration) as part of the verify gate, covering `README.md`, `AGENTS.md`, `docs/` and `adr/`; `openspec/` is excluded because it is tool-managed.

#### Scenario: A violation fails the gate
- **GIVEN** a markdown file in scope with a lint violation
- **WHEN** the verify gate runs
- **THEN** the documentation lint step fails and reports the violation

#### Scenario: Clean documentation passes
- **GIVEN** documentation without violations
- **WHEN** the verify gate runs
- **THEN** the documentation lint step passes

### Requirement: Contribution templates
The repository SHALL provide a pull request template carrying the review-map sections and issue templates for bug reports and feature requests.

#### Scenario: Opening a pull request prefills the template
- **GIVEN** a new pull request
- **WHEN** the body is composed
- **THEN** the review-map sections (what this is, what lands, review map, verification, notes) are prefilled

#### Scenario: Opening an issue offers the templates
- **GIVEN** a new issue
- **WHEN** the reporter picks a template
- **THEN** a bug report or feature request form is offered

### Requirement: Documentation entry points and guideline
`README.md` SHALL be the project entry point (what it is, status, quick start, links); `docs/README.md` SHALL map every documentation kind to its home and state the writing guideline; `AGENTS.md` SHALL point to the map so every session finds it.

#### Scenario: One hop to every document kind
- **GIVEN** a reader starting at `README.md`
- **WHEN** they follow the documentation link
- **THEN** the map names the home of the guide, the specs, the in-flight changes, the ADRs and the spikes

#### Scenario: The guideline states where facts live
- **GIVEN** a contributor adding documentation
- **WHEN** they read the guideline
- **THEN** it tells them which document kind owns which fact and that behavior changes update the specs first

### Requirement: Architecture documentation
`docs/architecture.md` SHALL present the system's structure as Mermaid diagrams: the component map (projects, dependency directions, external boundaries), the model pipeline order, one run as a sequence (model calls, tool execution, events, spans), and the event path from emission to consumers. Each diagram SHALL stay small, SHALL link to the spec or ADR that owns its detail, and SHALL be updated in the same change that alters the pictured structure.

#### Scenario: One hop from the map to the picture and back
- **GIVEN** a reader starting at the documentation map
- **WHEN** they follow the architecture entry
- **THEN** the page shows the four diagrams and each diagram links to its spec or ADR home

#### Scenario: Structure changes update the page
- **GIVEN** a change that alters the project graph or a pictured flow
- **WHEN** the change is reviewed
- **THEN** `docs/architecture.md` is updated in the same change, and the review can verify each changed edge because the diagrams are text
