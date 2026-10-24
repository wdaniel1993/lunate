## Purpose

Closing the two ergonomic gaps the shipped prompt workflow leaves: a queued follow-up must be recognisable in the footer, and the word-editing intents must be reachable on terminals that drop Alt.

## ADDED Requirements

### Requirement: Queued follow-up visibility

While a turn is running, the live-area footer SHALL show the number of queued follow-ups and the first line of the head entry, so a queued message is recognisable without taking the queue back. The line SHALL be clipped to the remaining footer width on cell boundaries and SHALL never wrap; when it clips to nothing the count SHALL still render. When the queue is empty the segment SHALL be absent, and taking the queue back SHALL clear it in the same frame that refills the editor.

#### Scenario: One queued follow-up shows its first line

- **WHEN** a follow-up is queued while a turn runs
- **THEN** the footer shows `1 queued` and the first line of that message, clipped to the footer width

#### Scenario: A narrow footer keeps the count

- **GIVEN** a footer too narrow for the queued line
- **WHEN** it renders with two queued follow-ups
- **THEN** the count `2 queued` is shown and the line is clipped, never wrapped onto a second row

#### Scenario: Taking the queue back clears the segment

- **WHEN** the user takes the queue back with Up
- **THEN** the editor holds the queued text and the footer segment is gone in the same frame

### Requirement: Reachable alternatives for Alt bindings

Every intent bound to an Alt chord SHALL also be reachable without Alt. `Ctrl+Left` and `Ctrl+Right` SHALL route word-left and word-right in every context where `Alt+B` and `Alt+F` do, and `Ctrl+Delete` SHALL route kill-word-forward where `Alt+D` does. Bindings that already have an Alt-free route — newline (`Ctrl+J`), the queue take-back (`Up`), the model picker (`/model`) — SHALL NOT gain a second alternative. The binding catalog SHALL remain the only routing table: no alternative may be handled outside a catalog row, and every row SHALL keep a non-empty description and a scripted key test.

#### Scenario: Word movement without Alt

- **GIVEN** a prompt line with two words and the cursor at the end
- **WHEN** `Ctrl+Left` is pressed
- **THEN** the cursor moves to the start of the last word, exactly as `Alt+B` does

#### Scenario: Kill word forward without Alt

- **GIVEN** a prompt line with two words and the cursor at the start
- **WHEN** `Ctrl+Delete` is pressed
- **THEN** the first word is killed and `Ctrl+Y` restores it

#### Scenario: No second alternative for a binding that has one

- **WHEN** the binding catalog is inspected
- **THEN** newline, queue take-back and the model picker each have exactly one Alt-free route, and no additional row was added for them
