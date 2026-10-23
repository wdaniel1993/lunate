# ADR: the keymap is data, and approvals keep Enter as deny

**Status:** Accepted with this change (maintainer sign-off requested in the PR).

## Context

Key handling is currently a `switch` in `KeyRouter` plus a hand-maintained table in `docs/guide.md`. Three rekeys in the follow-up-queue change had to edit the switch, the tests and the documentation separately, and the guide drifted twice before review caught it. Two more consumers are coming: an in-app `?` overlay and, after it, extension-contributed bindings and a keybindings file (P8). A `switch` cannot describe itself, so each consumer needs a parallel copy.

Separately, Claude Code's permission prompt binds Enter to yes. Lunate binds Enter to deny, deliberately: a stray Enter on a pending `bash` approval can run an arbitrary command, and that is not recoverable. Copying the binding set does not require copying that.

## Decision

1. **One ordered catalog** of `(key, intent, contexts, description)` rows is the only source of key meaning. `KeyRouter` routes from it, the guide's table is checked against it, and the `?` overlay renders it. Overlap within a context is a test failure, not a review finding.
2. **Contexts are data** (idle, running, queue, approval, picker, transcript, bash), replacing the two ad-hoc booleans, so new surfaces add context without adding parameters.
3. **Editor keys get intents** (`KillToEnd`, `Undo`, …) and are dispatched by the session to the input line, so they appear in the catalog like every other key rather than hiding inside the input widget.
4. **Enter keeps denying approvals**; `Esc` denies too, `Up`/`Down` move a selection and `Space` activates it, and `y`/`n`/`a` still decide directly. The divergence from Claude Code is intentional and documented.

## Consequences

- Adding or changing a key is one row plus its test; the guide and the overlay cannot disagree with routing.
- Extension bindings and a keybindings file become row sources rather than new mechanisms.
- The catalog must stay pure data: no handlers, no async, no closures. Everything executable stays behind `RoutedKey`.
- The guide's table becomes a checked artifact; a stale row fails the build instead of shipping.
- Approvals remain safe against a stray Enter, at the cost of not matching Claude Code exactly on that one prompt.
