## MODIFIED Requirements

### Requirement: Status footer

The footer SHALL show model, tokens used against the context window with percent, working directory and git branch as one dim line, formatted culture-invariantly. Extension status widgets, when registered, SHALL append their segments after the core fields in registration order. It SHALL degrade deterministically at narrow widths (branch dropped first, then directory; model and usage survive; widget segments drop before the branch) and omit percent when the window is unknown.

#### Scenario: Narrow width degrades in order

- **GIVEN** a footer too wide for the terminal
- **WHEN** it renders
- **THEN** the branch is dropped first, then the directory, and model and usage remain

#### Scenario: No repository, no branch

- **GIVEN** a working directory outside a git repository
- **WHEN** the footer renders
- **THEN** the branch segment is absent and the rest renders normally

#### Scenario: Widget segments append after the core fields

- **GIVEN** a registered status widget returning a segment
- **WHEN** the footer renders
- **THEN** the segment appears after model, usage, directory and branch, and drops before them at narrow widths
