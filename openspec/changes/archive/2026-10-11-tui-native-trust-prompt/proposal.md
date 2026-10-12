# Proposal: tui-native-trust-prompt

## Why

The project trust prompt is the first question a session can ask, and it currently reads a raw stdin line before the TUI exists (`ConsoleTrustPrompt`; `ConsoleMcpConfigTrustPrompt` for `mcp.json`). Daniel's directive: it must be TUI-native — rendered in the live area, answered with keys through the normal key path.

## What

- A `TrustPromptModel` + live-area slot + renderer, on the approval-prompt pattern (`[y]es` `[n]o`; `y`/`Y` approve; `n`/Enter/Esc deny; Ctrl+C neither — the quit window only; other keys do not decide).
- The session composes with the live area and key pump running, so session-start questions are answered through the same key path as approvals; ordinary input stays inert until composition completes and the footer shows a `loading project…` status. Keys received before a question is shown never answer it (type-ahead cannot approve).
- Several pending decisions are asked one at a time, labeled `n of m`.
- Question content: an extension question names id, version, scope and directory — and says so when it re-prompts because files changed; the `mcp.json` question names the path, its server names and their truncated commands with a count (approving starts processes).
- After any answer, one notice: the decision is remembered for this repository, and `/trust` is how to change it (a minimal `/trust` command lists this repository's records; `/trust clear` clears them so the next load asks again).
- Both trust prompts move to the TUI (`TuiExtensionTrustPrompt`, `TuiMcpConfigTrustPrompt`); the line-based console v1s are deleted (interactive mode already requires a terminal). Print and ACP keep today's non-interactive refusal behavior; whether print/CI needs a deliberate pre-trust path is a follow-up card, not a revived console prompt.

## Impact

- `Lunate.Tui`: `TrustPromptModel`, renderer branch, `SetTrustPrompt`.
- `Lunate.Coding`: session restructure (pump during compose), trust TCS + modal branch + sequence stamping, the two TUI prompts, `/trust`, deletions.
- `Lunate.Extensibility`: two small trust-store queries (recorded-before; clear a repository) + a pending-trust count over discovery.
- Specs: extensions (trust prompt surface, re-prompt wording, revisability), tui (trust prompt; interactive-session composition; `/trust` in slash commands).

## Out of scope

- The per-server MCP approval keyed by the server-definition hash (standards card Change 1 — it reuses this question shape).
- A non-interactive pre-trust path for CI (follow-up card).
- Any change to trust records, hashing or the non-interactive refusals.
