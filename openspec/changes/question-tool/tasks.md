# Question Tool Implementation Tasks

## 1. Approval checkpoint

- [ ] 1.1 Obtain maintainer sign-off on the `ask_user` tool contract, MCP form elicitation and ACP fallback before implementation; record the decision on the proposal, and do not proceed if sign-off is withheld. Apply starts only after this proposal is available on `main`.

## 2. Agent question contract

- [ ] 2.1 Test-first in `tests/Lunate.Agent.Tests`: add focused failing tests for 1–4 questions, 2–4 options, descriptions, single/multi-select, recommended-option ordering, free text, answer output and invalid counts; confirm failure, implement the hand-authored `ask_user` tool and its decision-focused description, then rerun the focused tests.
- [ ] 2.2 Test-first in `tests/Lunate.Agent.Tests`: prove `ask_user` bypasses tool approval while a subsequent `bash` call still reaches a fresh approval prompt, and prove the tool is absent from a subagent registry with no parent bubbling; confirm failure, implement the exposure and approval boundary, then rerun the focused tests.

## 3. Interactive question picker

- [ ] 3.1 Test-first in `tests/Lunate.Tui.Tests`: cover recommended display, arrows, number-key selection/toggle, Space multi-select, Enter, free-text alongside labels, Esc skip/discard, and state across repaint; confirm failure, implement the shared `SelectList` question presentation, then rerun the focused tests.
- [ ] 3.2 Test-first in the TUI/interactive-host tests: prove buffered keys and keys in the first 300 ms cannot select, change text or submit; prove Esc skips without producing partial answers; confirm failure, wire the approval-style arming rule through each question, then rerun the focused tests with virtual time.
- [ ] 3.3 Test-first in `tests/Lunate.Tui.Tests`: add width fixtures at 120, 80, 60 and 40 columns with long descriptions; confirm the 40-column Termux case fails, implement the width-band layout without clipping controls, then rerun the focused tests and review any updated TUI goldens.

## 4. No-user and ACP behavior

- [ ] 4.1 Test-first in `tests/Lunate.Coding.Tests` and `tests/Lunate.Protocols.Tests`: prove print and unattended `ask_user` calls return `unanswered: no user present` immediately and MCP form requests cancel immediately while recording that outcome; direct the model to state an assumption and continue, and do not read stdin or write question diagnostics to stdout; confirm failure, wire the non-interactive results, then rerun the focused tests.
- [ ] 4.2 Test-first in `tests/Lunate.Protocols.Tests`: prove ACP does not route questions through `session/request_permission` and returns the immediate unanswered result when no separate non-approval input mechanism is advertised; confirm failure, implement only an explicitly supported input route if the client contract provides one, then rerun the focused tests.

## 5. MCP form elicitation

- [ ] 5.1 Test-first in `tests/Lunate.Protocols.Tests`: add a fake-server test for form capability negotiation and `elicitation/create` mapping of message, flat primitive fields, enum choices, enum arrays, schema validation and server identity; confirm failure, connect the request to the shared question interaction, then rerun the focused tests.
- [ ] 5.2 Test-first in `tests/Lunate.Protocols.Tests`: prove Esc/no-user produces a content-free cancel response and URL mode is not advertised or opened; confirm failure, implement the protocol responses and actionable unsupported-mode handling, then rerun the focused tests.

## 6. Session, transcript and export pairing

- [ ] 6.1 Test-first in `tests/Lunate.Agent.Tests` and `tests/Lunate.Coding.Tests`: prove an `ask_user` call/result pair and an MCP server-labelled elicitation pair retain request order through session replay, the interactive transcript, JSONL export and HTML export; confirm failure, implement explicit paired rendering/recording through existing tool messages without changing the session schema, then rerun the focused tests. Do not update session golden files; stop and report any unrelated golden failure.

## 7. Final verification

- [ ] 7.1 Run `PATH="/opt/homebrew/bin:$PATH" /opt/homebrew/bin/openspec validate question-tool --type change --strict`, the touched test projects in Release, `dotnet build Lunate.slnx -c Release`, `dotnet csharpier check .`, and the required docs-profile checks; resolve failures before marking the change ready for review.
