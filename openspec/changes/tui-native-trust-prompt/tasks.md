# Tasks: TUI-native trust prompt

## 1. The question surface (Tui)

- [ ] 1.1 `TrustPromptModel` (title, detail lines, `Decide(KeyEvent)`: bare `y` approves; `n`/Enter/Esc deny; else null) + unit tests
- [ ] 1.2 `LiveArea.SetTrustPrompt` + state + renderer branch (styled like the approval line: yellow question marker, `[y]es` `[n]o` hint, user text escaped) + render tests

## 2. The session restructure (Coding)

- [ ] 2.1 `RunAsync`: live area starts first; the key pump runs while composition runs; `_composed` flips after composition (notices keep buffering + the joined flush); normal keys inert until composed (`SubmitAsync` gated)
- [ ] 2.2 `_trustPrompt`/`_trustDecision` + the `HandleKeyAsync` modal branch (quit window still acts) + `AskTrustAsync(model, ct)`
- [ ] 2.3 Quit/cancel during composition: the linked token cancels composition; existing error handling unchanged

## 3. Wiring + deletion

- [ ] 3.1 `TuiExtensionTrustPrompt` + `TuiMcpConfigTrustPrompt` (both delegate to `AskTrustAsync`); the interactive session uses them; print/ACP keep the non-interactive refusals
- [ ] 3.2 Delete `ConsoleTrustPrompt` + `ConsoleMcpConfigTrustPrompt`; move their key-semantics tests to the model; keep the non-interactive prompt tests

## 4. Tests (the done-when)

- [ ] 4.1 e2e: untrusted project extension → the question renders in the frames → `y` loads it (hook observable) → a later session does not re-prompt
- [ ] 4.2 e2e: `n` refuses + logs; Enter/Esc deny; other keys leave it pending; input inert while pending
- [ ] 4.3 e2e: the project `mcp.json` question (same surface), approved with `y`
- [ ] 4.4 Regressions: print/ACP non-interactive refusals unchanged; the approval flow unchanged

## 5. Gate + record

- [ ] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md
- [ ] 5.2 Archive, PR, CI green
