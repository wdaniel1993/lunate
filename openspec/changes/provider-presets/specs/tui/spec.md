## MODIFIED Requirements

### Requirement: One meaning per key

Key events SHALL route to exactly one intent per the guide's table: Enter submits, Alt+Enter and Ctrl+J edit (newline), Esc cancels, Ctrl+C clears or quits, Ctrl+L opens the model picker, Ctrl+P cycles favourites, Tab completes a leading slash command, Up/Down navigate history, everything else edits. Routing SHALL be pure, and every binding SHALL have a scripted key test.

#### Scenario: Every binding routes distinctly

- **GIVEN** the key events of the guide's binding table
- **WHEN** each is routed
- **THEN** it produces its own intent (and no other binding's), proven by the scripted matrix

#### Scenario: Ctrl+C never cancels a turn

- **GIVEN** a running turn
- **WHEN** Ctrl+C is pressed (any number of times)
- **THEN** the input clears or the quit window arms/quits; no cancel intent exists for it

#### Scenario: Tab completes instead of editing

- **GIVEN** an input that starts with `/`
- **WHEN** Tab is routed
- **THEN** it produces the complete intent, not edit

#### Scenario: Ctrl+P is its own intent

- **GIVEN** the prompt is idle
- **WHEN** Ctrl+P is routed
- **THEN** it produces the favourites-cycle intent and not the model-picker or edit intent

### Requirement: Status footer

The footer SHALL show model, tokens used against the context window with percent, working directory and git branch as one dim line, formatted culture-invariantly. When the thinking level is not `off`, a `think:<level>` segment SHALL appear after the usage segment. When the session's model is a virtual or extension-registered model, the model segment SHALL show the last routed resolution (`auto → deep`), updating as `ModelRouted` events arrive. When the active model is in the favourites list, a `fav:<position>/<count>` segment SHALL appear after the usage segment. When every token lane of the current turn is priced, a cost segment SHALL appear after the favourites segment; an unpriced turn SHALL omit it rather than show zero. Extension status widgets, when registered, SHALL append their segments after the core fields in registration order. It SHALL degrade deterministically at narrow widths (widget segments drop first, then branch, then directory, then thinking, then favourites, then cost; model and usage survive) and omit percent when the window is unknown. Below 60 columns the footer SHALL switch to the compact form `model · <pct>%`, dropping the token counts along with the favourites, cost, thinking, directory, branch and widget segments regardless of remaining width. The interactive session SHALL provide a `/context` command showing the context usage plus the resolved subagent limits (concurrency, per-child and per-tree token budgets, the fixed depth) with the current tree usage.

#### Scenario: Narrow width degrades in order

- **GIVEN** a footer too wide for the terminal
- **WHEN** it renders
- **THEN** widgets drop first, then the branch, then the directory, then thinking, then favourites, then cost, and model and usage remain

#### Scenario: No repository, no branch

- **GIVEN** a working directory outside a git repository
- **WHEN** the footer renders
- **THEN** the branch segment is absent and the rest renders normally

#### Scenario: Widget segments append after the core fields

- **GIVEN** a registered status widget returning a segment
- **WHEN** the footer renders
- **THEN** the segment appears after model, usage, favourites and cost, and drops before them at narrow widths

#### Scenario: /context lists the subagent limits

- **GIVEN** an interactive session after a subagent ran
- **WHEN** `/context` is entered
- **THEN** the context usage and the resolved subagent limits with the current tree usage are shown

#### Scenario: The footer shows the last route

- **GIVEN** a session on a virtual model
- **WHEN** a call routes to the `deep` backend
- **THEN** the footer's model segment shows `auto → deep`

#### Scenario: The footer shows the thinking level

- **GIVEN** a session with thinking level `medium`
- **WHEN** the footer renders
- **THEN** the segment `think:medium` appears after the usage segment, and at level `off` it is absent

#### Scenario: The compact footer below 60 columns

- **GIVEN** a terminal 40 columns wide with tokens and a context window known
- **WHEN** the footer renders
- **THEN** it reads `model · <pct>%` with no tokens, window, favourites, cost, thinking, directory, branch or widget segments

#### Scenario: An unknown window omits the percent at 40 columns

- **GIVEN** a 40-column terminal whose context window is unknown
- **WHEN** the footer renders
- **THEN** only the model segment appears

#### Scenario: The footer shows the favourite position

- **GIVEN** a favourites list of five with the active model at position two
- **WHEN** the footer renders
- **THEN** it carries `fav:2/5` after the usage segment

#### Scenario: An unpriced turn has no cost segment

- **GIVEN** a turn whose model has no catalog pricing
- **WHEN** the footer renders
- **THEN** the token usage is unchanged and no cost segment appears

## ADDED Requirements

### Requirement: Cost in the status footer

When every `UsageUpdated` record of the current turn tree (root and subagents) resolves to a catalog model with a known rate for every positive token lane, the footer SHALL show the turn's cost as `$` plus a culture-invariant decimal: four decimals below `$1`, two at or above `$1`, and `<$0.0001` for a positive value that would otherwise round to zero. If any record is unpriced, unknown, or the turn has no usage, the segment SHALL be absent. A new turn SHALL start a fresh cost. The cost SHALL be display-only and SHALL NOT change the token totals, budgets or approvals.

#### Scenario: A fully priced turn shows a cost

- **GIVEN** a turn with input, output and cache usage whose model prices every used lane
- **WHEN** the footer renders
- **THEN** it shows the summed turn cost in dollars

#### Scenario: A subagent's usage is included

- **GIVEN** a turn whose root and child run both report priced usage
- **WHEN** the footer renders
- **THEN** the cost is the sum of both runs' records

#### Scenario: One unpriced record suppresses the turn cost

- **GIVEN** a turn where one routed model has no pricing
- **WHEN** the footer renders
- **THEN** no cost segment appears, even though other records are priced

#### Scenario: Tiny costs never read as zero

- **GIVEN** a priced turn whose cost is positive but below `$0.0001`
- **WHEN** the footer renders
- **THEN** the segment reads `<$0.0001`

### Requirement: Favourites cycling

`Ctrl+P` SHALL switch to the next favourite in settings order and wrap at the end, using the same model-switch path as `/model` (a session model-change entry, a rebuilt harness and an updated footer). When the active model is not in the list, it SHALL switch to the first favourite. An empty list SHALL produce a notice and no switch. The binding SHALL be refused while a turn runs, an approval is open, a picker is open or a secret prompt is open, and SHALL win over an open autocomplete list like `Ctrl+L` and `Ctrl+O`.

#### Scenario: Ctrl+P advances and wraps

- **GIVEN** favourites `[a, b, c]` and active model `a`
- **WHEN** Ctrl+P is pressed three times
- **THEN** the active model moves to `b`, then `c`, then back to `a`, and each switch is recorded in the session

#### Scenario: A non-favourite starts at the first

- **GIVEN** favourites `[a, b]` and active model `z`
- **WHEN** Ctrl+P is pressed
- **THEN** the active model becomes `a`

#### Scenario: An empty list is a notice

- **GIVEN** no favourites
- **WHEN** Ctrl+P is pressed
- **THEN** a notice says to add one with `/favorite` and no run starts

#### Scenario: Ctrl+P waits for idle

- **GIVEN** a running turn
- **WHEN** Ctrl+P is pressed
- **THEN** a notice refuses the switch and the run is untouched

### Requirement: Masked provider login prompt

`/login <provider>` SHALL open a secret prompt in the live area for a known preset: the typed characters SHALL render as a mask, Enter SHALL save through the auth store, and Esc or an empty value SHALL cancel. While the prompt is open, ordinary input SHALL be inert and only prompt/quit keys SHALL act. The secret SHALL NOT appear in the input line's history, the transcript, scrollback, a notice, the session file or a log. A failed save SHALL report the failure without the value. `/logout <provider>` SHALL need no prompt and SHALL NOT make a network call.

#### Scenario: The key is masked and never persisted visibly

- **GIVEN** an idle session
- **WHEN** `/login groq` is submitted, a key is typed and Enter is pressed
- **THEN** the frames show only a mask, the key is stored in auth.json, and the key text appears nowhere in scrollback, history or the session file

#### Scenario: Esc cancels without saving

- **GIVEN** an open secret prompt
- **WHEN** Esc is pressed
- **THEN** the prompt closes, the auth store is unchanged, and a notice says the login was cancelled

#### Scenario: Ordinary input is inert while the prompt is open

- **GIVEN** an open secret prompt
- **WHEN** ordinary text and Enter are pressed
- **THEN** only the secret prompt consumes them and no run starts

#### Scenario: Only presets are accepted

- **GIVEN** an idle session
- **WHEN** `/login not-a-provider` is submitted
- **THEN** a notice lists the supported presets and no prompt opens
