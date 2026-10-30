## MODIFIED Requirements

### Requirement: Working indicator

The live area SHALL carry one activity line while work is in flight: a spinner frame, a label, the
elapsed seconds and — while the turn is cancellable — the root run's token deltas and `esc to
interrupt`. The label SHALL be `thinking…` while reasoning streams, `<tool name> <summary>` while a
tool runs, `verifying…` while a write verifier runs, and `working…` otherwise. The verifier activity
line SHALL show its elapsed time and SHALL fit the active width band. The narrow band (below 60
columns) SHALL keep the spinner, the label and elapsed seconds and drop the token deltas and the
interrupt hint. The thinking label SHALL carry the thinking colour where the terminal supports
colour — the theme value in truecolor and 256-color modes, the 16-color fallback in 16-color mode —
and SHALL be colour-neutral under NO_COLOR (the spinner carries the motion).

#### Scenario: Thinking work shows the affordances

- **GIVEN** a running turn whose model streams reasoning
- **WHEN** the activity line renders
- **THEN** it reads `⠹ thinking… <elapsed>s · ↑<in> ↓<out> · esc to interrupt`

#### Scenario: A running tool names itself

- **GIVEN** a running tool call
- **WHEN** the activity line renders
- **THEN** it carries the spinner, the tool name with its argument summary, and the elapsed seconds

#### Scenario: Narrow band keeps the essentials

- **GIVEN** a terminal 40 columns wide with a running tool
- **WHEN** the activity line renders
- **THEN** it fits the width with the spinner, the label and the elapsed seconds, and no token deltas
  or interrupt hint

#### Scenario: Subagent usage stays out of the deltas

- **GIVEN** a root run with a child (subagent) run
- **WHEN** the child reports its usage
- **THEN** the activity line keeps the root run's deltas while the footer's cumulative total
  includes the child's tokens

#### Scenario: Verification shows its elapsed time

- **GIVEN** a write verifier is running
- **WHEN** the live area renders its activity line
- **THEN** it shows `verifying… <elapsed>` with the spinner and elapsed seconds

#### Scenario: Esc cancels a verifying run

- **GIVEN** a write verifier is running during a cancellable turn
- **WHEN** the user presses Esc
- **THEN** the turn is cancelled through the normal run-cancellation path and the host does not
  write the staged content
