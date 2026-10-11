# Design: tui-native-trust-prompt

## Context

compose-extension-host shipped the project trust prompt as a line-based console v1: `ConsoleTrustPrompt` reads a line from `Console.In` before the live area starts. add-mcp-config added `ConsoleMcpConfigTrustPrompt`, the same pattern for the project `mcp.json`. Daniel's directive: the prompt must be TUI-native — the question rendered in the live area, answered with keys through the normal key path.

Facts the design leans on:

- Interactive mode already requires a terminal (`lunate: interactive mode needs a terminal` in `Cli.cs`), and `SystemConsoleIO.ComputeIsInteractive` treats `TERM=dumb` as not a terminal. The console v1's "dumb-terminal fallback" role is unreachable, so v1 can be deleted rather than kept.
- The approval flow is the precedent: a model (`ApprovalPromptModel`) with `Decide(KeyEvent)`, a live-area slot (`_live.SetApproval`), a `TaskCompletionSource` in the session, and a `HandleKeyAsync` modal branch.
- Today composition runs **before** the live area starts and **before** the key pump runs; a prompt cannot be answered through the key path until that ordering changes.

## Decisions

### D1 — The trust question is a live-area modal on the approval pattern

`TrustPromptModel` (Tui): a title and detail lines, plus `Decide(KeyEvent)` — a bare `y` approves; `n`, `Enter` and `Esc` deny (nothing loads by accident — the approval "Enter denies" precedent); every other key returns no decision. `LiveArea.SetTrustPrompt(model)` + a renderer branch mirroring the approval line (yellow question marker, `[y]es` `[n]o` hint, all user text markup-escaped). The session keeps `_trustPrompt` + `_trustDecision`; `HandleKeyAsync` gets a modal branch like approvals (the quit window still acts).

### D2 — The key pump runs while the session composes

`RunAsync` restructure: the live area starts first; the key pump starts as a task; `ComposeAsync` runs on the main flow (trust questions await their completion source, resolved by the pump). After composition: `_composed` flips, notices flush, status updates, then the pump is awaited. While composing, non-modal keys are inert — the input line accepts no text, `SubmitAsync` is gated on `_composed`; only the quit window acts (the "approval blocks steering" precedent). Composition-time notices keep buffering and flush as one joined message (unchanged behavior, deliberately minimal here).

### D3 — Both prompts move; the console v1s are deleted

`TuiExtensionTrustPrompt` (`IExtensionTrustPrompt`) and `TuiMcpConfigTrustPrompt` (`IMcpConfigTrustPrompt`) both delegate to one session-level `AskTrustAsync(model, ct)`. Print and ACP keep the non-interactive refusals unchanged. `ConsoleTrustPrompt` + `ConsoleMcpConfigTrustPrompt` are deleted; their key-semantics tests move to the model tests (the semantics now live in the model) plus the session e2e.

### D4 — Question content

The extension question names the id, version, scope and directory (`Load project extension 'hello' 0.1.0 from <directory>?`); the config question names the path (`Start servers from project mcp.json '<path>'?`). The trust record's repository identity (a git-common-dir path) is not shown — it is an implementation detail; the directory line carries the user-facing context.

## Alternatives considered (devil's advocate)

- **A private key-read loop inside the prompt** (no restructure): rejected — not the normal key path; duplicates modal routing, quit-window and Ctrl+C semantics; a second consumer on the key stream.
- **Keep the console v1 for `TERM=dumb`**: rejected — unreachable (interactive requires a terminal).
- **Render the question via scrollback above the live area**: rejected — the question must stay visible while it waits; the live-area slot is the approval precedent.
- **Defer composition until the first submit**: rejected — the harness, tools and system prompt must exist before the first turn; session start is composition.
- **A dedicated pre-compose mini-loop reading keys from `IConsoleIO`**: rejected — two enumerators on the key channel and divergent Esc/Ctrl+C behavior; the pump-owned TCS keeps one consumer and one routing table.

## Risks

- **Ordering change (pump during composition)**: exactly one key consumer (the pump) — the prompt never reads keys itself. Quit during composition cancels composition through the linked token; `RunAsync` keeps its current cancellation and error handling.
- **Frame timing**: the live area paints its initial frame before composition (today it starts after). Tests that assert the first frame adapt.
- **`_composed` gate**: submissions during composition are inert, matching "approval blocks steering".

## Deviations

*(the apply records deviations here)*
