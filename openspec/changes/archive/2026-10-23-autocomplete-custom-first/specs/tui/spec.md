## ADDED Requirements

### Requirement: Custom completion entries stay visible

The command autocomplete SHALL sort custom entries — skills and extension commands — ahead of the built-in commands, preserving the source order inside each group. When the filtered list exceeds the eight-row budget, the list SHALL show one fewer entry and use the last row for an indicator of the form `… +N more — keep typing to filter`, where N is the number of entries not shown. A list that fits the budget SHALL NOT show the indicator.

#### Scenario: A bare slash with custom entries installed

- **WHEN** the user types `/` with at least one skill or extension command available
- **THEN** the custom entries appear before the built-in commands

#### Scenario: More entries than rows

- **WHEN** the filtered list holds more entries than the eight-row budget
- **THEN** seven entries and one indicator row render, and the indicator names the hidden count

#### Scenario: A list that fits

- **WHEN** the filtered list holds eight or fewer entries
- **THEN** no indicator row renders
