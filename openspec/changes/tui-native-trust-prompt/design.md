# Design: tui-native-trust-prompt

## Context

compose-extension-host shipped the project trust prompt as a line-based console v1: `ConsoleTrustPrompt` reads a line from `Console.In` before the live area starts. add-mcp-config added `ConsoleMcpConfigTrustPrompt`, the same pattern for the project `mcp.json`. Daniel's directive: the prompt must be TUI-native — the question rendered in the live area, answered with keys through the normal key path.

Facts the design leans on:

- Interactive mode already requires a terminal (`lunate: interactive mode needs a terminal` in `Cli.cs`), and `SystemConsoleIO.ComputeIsInteractive` treats `TERM=dumb` as not a terminal. The console v1's "dumb-terminal fallback" role is unreachable, so v1 can be deleted rather than kept.
- The approval flow is the precedent: a model (`ApprovalPromptModel`) with `Decide(KeyEvent)`, a live-area slot (`_live.SetApproval`), a `TaskCompletionSource` in the session, and a `HandleKeyAsync` modal branch.
- Today composition runs **before** the live area starts and **before** the key pump runs; a prompt cannot be answered through the key path until that ordering changes.
- The trust store's `Evaluate` returns `Prompt` for both first-time and changed hashes (and `NewWorktree` auto-records); a recorded-repository query is needed to distinguish them.

## Decisions

### D1 — The trust question is a live-area modal on the approval pattern

`TrustPromptModel` (Tui): a title and detail lines, plus `Decide(KeyEvent)` — a bare `y` or `Y` approves; `n`, `Enter` and `Esc` deny (nothing loads by accident — the approval "Enter denies" precedent); every other key returns no decision. Ctrl+C is not a decision: it routes to the quit window exactly as during an approval. `LiveArea.SetTrustPrompt(model)` + a renderer branch mirroring the approval line (yellow question marker, `[y]es` `[n]o` hint, all user text markup-escaped). The session keeps `_trustPrompt` + `_trustDecision`; `HandleKeyAsync` gets a modal branch like approvals.

### D2 — The key pump runs while the session composes; type-ahead cannot answer

`RunAsync` restructure: the live area starts first; the key pump starts as a task; `ComposeAsync` runs on the main flow (trust questions await their completion source, resolved by the pump). After composition: `_composed` flips, notices flush, status updates, then the pump is awaited. While composing, non-modal keys are inert — the input line accepts no text, `SubmitAsync` is gated on `_composed`; only the quit window acts (the "approval blocks steering" precedent). The footer shows a dim `loading project…` status (ASCII fallback `loading project...`) until the real status replaces it.

Type-ahead guard: the pump stamps every key with a read-time sequence number; a trust question records the sequence at arm time, **after** its render is posted. The modal branch resolves only keys stamped after that sequence, so keys read before the question existed are inert — a type-ahead `yes …` can never approve. Composition-time notices keep buffering and flush as one joined message (unchanged behavior, deliberately minimal here).

### D3 — Both prompts move; the console v1s are deleted

`TuiExtensionTrustPrompt` (`IExtensionTrustPrompt`) and `TuiMcpConfigTrustPrompt` (`IMcpConfigTrustPrompt`) both delegate to one session-level `AskTrustAsync(model, ct)`. Print and ACP keep the non-interactive refusals unchanged. `ConsoleTrustPrompt` + `ConsoleMcpConfigTrustPrompt` are deleted; their key-semantics tests move to the model tests (the semantics now live in the model) plus the session e2e. No deliberate pre-trust path exists for print/CI today — that gap becomes a follow-up card, not a revived console prompt.

### D4 — Question content and sequencing

- **Extension**: id, version, scope and directory (`Load project extension 'hello' 0.1.0 from <directory>?`); a re-prompt because the files changed says so (`… — its files changed since this repository trusted it. Load the new version?`). The gate distinguishes first-time from changed via a recorded-repository query on the store.
- **Config**: the path plus the servers — `Start N servers from project mcp.json '<path>'?` with one detail line per server: `name — command args` (each truncated). Approving starts processes; the per-server approval from the standards card reuses this shape later.
- **Sequencing**: the session precomputes the pending-question count at composition start — project extensions whose trust evaluates to `Prompt` (discovery + content hash + store query, no assembly load, via the loader's `Discover`) plus one for an untrusted project config — and labels each question `n of m`. Strictly one question at a time.
- **After any answer**: one notice — the decision is remembered for this repository and `/trust` is how to change it. `/trust` lists this repository's records (extension trust + config, short hashes); `/trust clear` clears them so the next load asks again. The trust record's repository identity is never shown in the question.
- The store gains two small APIs: a recorded-before query and a repository clear (both sections), plus the pending-count helper.

## Alternatives considered (devil's advocate)

- **A private key-read loop inside the prompt** (no restructure): rejected — not the normal key path; duplicates modal routing, quit-window and Ctrl+C semantics; a second consumer on the key stream.
- **Keep the console v1 for `TERM=dumb`**: rejected — unreachable (interactive requires a terminal).
- **Render the question via scrollback above the live area**: rejected — the question must stay visible while it waits; the live-area slot is the approval precedent.
- **Defer composition until the first submit**: rejected — the harness, tools and system prompt must exist before the first turn; session start is composition.
- **A dedicated pre-compose mini-loop reading keys from `IConsoleIO`**: rejected — two enumerators on the key channel and divergent Esc/Ctrl+C behavior; the pump-owned TCS keeps one consumer and one routing table.
- **Answer from buffered keys** (queue type-ahead until a question arms): rejected — precisely what the type-ahead guard forbids; queued keys must never approve.
- **Defer `/trust` to its own card**: rejected — the post-answer notice must be actionable now; list + clear is small and closes the loop.

## Risks

- **Ordering change (pump during composition)**: exactly one key consumer (the pump) — the prompt never reads keys itself. Quit during composition cancels composition through the linked token; `RunAsync` keeps its current cancellation and error handling.
- **Sequence stamping**: keys are stamped at read time in the pump; the arm point is set after the render post. The ≤33 ms frame-sampling window between arm and paint is humanly untypable; the guard's job is the loading-phase type-ahead, which it fully covers.
- **Count precompute vs actual**: a load failure can skip a question after counting (an extension breaks between discovery and load); `m` is the planned count — acceptable, and the questions remain strictly sequential.
- **Frame timing**: the live area paints its initial frame before composition (today it starts after). Tests that assert the first frame adapt.
- **`_composed` gate**: submissions during composition are inert, matching "approval blocks steering".

## Deviations

- **Store API shape**: the "recorded-before query" landed as `ExtensionTrustStore.Records(repositoryIdentity)` returning `TrustRecord(Section, WorktreePath, ContentHash)` for both sections (the loader derives "recorded before" from the Extensions section; `/trust` lists all of them), plus `ClearRepository(repositoryIdentity)` clearing both sections. Same two APIs, richer query.
- **Extension seam**: `IExtensionTrustPrompt.ApproveAsync` now takes `ExtensionTrustRequest(Descriptor, FilesChanged)` (a request record rather than an extra parameter); `IMcpConfigTrustPrompt.ApproveAsync` takes the parsed `IReadOnlyList<McpServerOptions>`, so `McpComposition` parses the project file before the trust gate (errors are still reported only when trusted). `ExtensionLoader` computes `FilesChanged` from `Records`.
- **Denial notice**: nothing is recorded on a denial (the gate only records approvals), so the denial notice says "nothing was recorded, so the next load asks again" and points at `/trust`; only an approval states the decision is remembered. The spec's "after any answer … remembered" is read as the approval case.
- **`n of m` label**: shown only when more than one question is pending (`m > 1`); a single question reads without a label.
- **Loading status**: `LiveArea.SetLoading(text)` renders a dedicated line in the footer slot (the status model has no free-text field); `RunAsync` clears it and posts the real footer when composition completes.
- **mcp.json question**: "server"/"servers" is pluralized by count; detail commands are truncated at 60 characters with `…`.
- **Existing e2e tests**: the interactive tests that type ordinary input immediately after `RunAsync` now wait for `Session.IsComposed` (inert type-ahead is the specified behavior; only `McpInteractiveEndToEndTests` needed the wait).
- **PublicAPI**: `Lunate.Extensibility` has no PublicApiAnalyzers/`PublicAPI.*` files (only the contract assembly tracks its surface), so no Extensibility entries were added; the new Tui surface is in `PublicAPI.Unshipped.txt` and `PublicApiTests.DocumentedSurface`.
