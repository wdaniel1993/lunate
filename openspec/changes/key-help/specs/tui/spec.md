## ADDED Requirements

### Requirement: In-app key help

The prompt SHALL offer its own bindings on screen, generated from the same catalog the router
reads: a dim `? keys` hint sits beside the mode hint while the prompt is idle, and `?` on an
empty prompt SHALL open a read-only overlay of the bindings that apply in the current context.
The overlay SHALL list one row per binding as `key — action`, SHALL move its selection with
`j`/`k` and the arrows, SHALL filter rows case-insensitively when `/` is pressed, SHALL switch
between the active contexts and every binding with `Tab`, and SHALL close on `Esc` or a second
`?` without touching the draft. A `?` typed into a non-empty prompt SHALL stay a literal
character, and no other key SHALL reach the session while the overlay is open.

#### Scenario: The hint advertises the overlay

- **GIVEN** an idle prompt with an empty input
- **WHEN** the status footer renders
- **THEN** it shows `? keys`, and hides that hint while a turn runs, in bash mode, or when the
  prompt holds text

#### Scenario: The overlay lists the bindings that apply here

- **GIVEN** the prompt is idle and the input is empty
- **WHEN** `?` is pressed
- **THEN** the overlay opens in the "here" scope with the idle editing bindings, each row reading
  `key — action` from the catalog's own description

#### Scenario: Help is read-only

- **GIVEN** the overlay is open with a draft in the prompt
- **WHEN** `Enter`, `Tab` outside the scope key, or any character other than the overlay's own keys
  is pressed
- **THEN** the session state, the draft and the transcript are unchanged, and `Esc` closes the
  overlay with the draft exactly as it was

#### Scenario: `?` is a character when typing

- **GIVEN** a prompt that already holds text
- **WHEN** `?` is pressed
- **THEN** the character is inserted into the input and no overlay opens
