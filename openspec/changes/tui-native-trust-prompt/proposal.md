# Proposal: tui-native-trust-prompt

## Why

The project trust prompt is the first question a session can ask, and it currently reads a raw stdin line before the TUI exists (`ConsoleTrustPrompt`; `ConsoleMcpConfigTrustPrompt` for `mcp.json`). Daniel's directive: it must be TUI-native — rendered in the live area, answered with keys through the normal key path.

## What

- A `TrustPromptModel` + live-area slot + renderer, on the approval-prompt pattern (`[y]es` `[n]o`; deny by default on `n`/Enter/Esc; other keys do not decide).
- The session composes with the live area and key pump running, so session-start questions are answered through the same key path as approvals; ordinary input stays inert until composition completes.
- Both trust prompts move to the TUI (`TuiExtensionTrustPrompt`, `TuiMcpConfigTrustPrompt`); the line-based console v1s are deleted (interactive mode already requires a terminal — there is no fallback to keep them for).
- Print and ACP keep today's non-interactive refusal behavior, unchanged.

## Impact

- `Lunate.Tui`: `TrustPromptModel`, renderer branch, `SetTrustPrompt`.
- `Lunate.Coding`: session restructure (pump during compose), trust TCS + modal branch, the two TUI prompts, deletions.
- Specs: extensions (trust prompt surface), tui (new trust-prompt requirement; interactive-session composition).

## Out of scope

- The per-server MCP approval from the standards card (separate change).
- Any change to trust records, hashing or the non-interactive refusals.
