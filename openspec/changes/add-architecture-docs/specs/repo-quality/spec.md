## ADDED Requirements

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
