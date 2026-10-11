# Tasks: TUI-native trust prompt

## 1. The question surface (Tui)

- [x] 1.1 `TrustPromptModel` (title, detail lines, `Decide(KeyEvent)`: bare `y`/`Y` approves; `n`/Enter/Esc deny; Ctrl+C and everything else null) + unit tests
- [x] 1.2 `LiveArea.SetTrustPrompt` + state + renderer branch (styled like the approval line: yellow question marker, `[y]es` `[n]o` hint, user text escaped, `n of m` label) + render tests

## 2. The session restructure (Coding)

- [x] 2.1 `RunAsync`: live area starts first; the key pump runs while composition runs; `_composed` flips after composition (notices keep buffering + the joined flush); normal keys inert until composed (`SubmitAsync` gated); footer shows `loading project…` during composition (ASCII fallback)
- [x] 2.2 `_trustPrompt`/`_trustDecision` + the `HandleKeyAsync` modal branch (Ctrl+C → quit window, never a decision) + `AskTrustAsync(model, ct)` + read-time key sequence stamping so keys read before the arm never answer
- [x] 2.3 Quit/cancel during composition: the linked token cancels composition; existing error handling unchanged

## 3. Wiring, questions, `/trust`

- [x] 3.1 `TuiExtensionTrustPrompt` + `TuiMcpConfigTrustPrompt` (both delegate to `AskTrustAsync`); the interactive session uses them; print/ACP keep the non-interactive refusals
- [x] 3.2 Question content: extension → id/version/scope/directory + the "files changed" wording on re-prompts (store recorded-before query); config → path + server names with truncated commands + count
- [x] 3.3 Pending-count precompute (extensions via `Discover` + trust evaluation, no load; +1 for an untrusted config) → `n of m`; after any answer, one notice ("remembered for this repository — change it with `/trust`")
- [x] 3.4 `/trust` + `/trust clear` (store: recorded-before query + repository clear; command list + dispatch + completion)
- [x] 3.5 Delete `ConsoleTrustPrompt` + `ConsoleMcpConfigTrustPrompt`; move their key-semantics tests to the model; keep the non-interactive prompt tests

## 4. Tests (the done-when)

- [x] 4.1 e2e: untrusted project extension → the question renders in the frames → `y` loads it (hook observable) → a later session does not re-prompt
- [x] 4.2 e2e: `n` refuses + logs; Enter/Esc deny; `Y` approves; Ctrl+C does not decide; other keys leave it pending; input inert while pending
- [x] 4.3 e2e: type-ahead (keys queued before the question shows) never approves
- [x] 4.4 e2e: the project `mcp.json` question shows path + servers + commands + count, approved with `y`; two pending decisions show `1 of 2` then `2 of 2`
- [x] 4.5 `/trust` lists and clears; after clear a new session re-prompts; the changed-files re-prompt wording
- [x] 4.6 Regressions: print/ACP non-interactive refusals unchanged; the approval flow unchanged

## 5. Gate + record

- [x] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md
- [ ] 5.2 Archive, PR, CI green
