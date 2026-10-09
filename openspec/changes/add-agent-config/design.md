# Design: agent config, system prompt and model discovery (T-16)

## System prompt and project instructions

- **Template**: `src/Lunate.Coding/system-prompt.md`, embedded resource (`Lunate.Coding.system-prompt.md`). Starts from the guide's draft; refines wording, keeps it well under budget. Placeholders are filled by the composer, not by the template engine — plain `string.Replace` on named tokens (`{cwd}`, `{os}`, `{shell}`, `{tools}`, `{date}`, `{agents}`), no formatting engine (braces in prose must not throw).
- **Runtime facts**: OS (`RuntimeInformation.OSDescription`), shell (the `ShellResolver` display name — same resolution the bash tool uses), working directory (canonical `Workspace.WorktreeRoot`), date (UTC, `yyyy-MM-dd`, invariant). The tool list comes from the registered `ITool` names at compose time.
- **AGENTS.md chain**: from `Workspace.RepoRoot ?? Workspace.WorktreeRoot` down to the working directory, one file per directory (skip missing), each block introduced by its relative path. Concatenated in root→leaf order; leaf wins in prose ("closest to the working directory is most specific").
- **Budget**: the composed prompt must stay **under 1,000 tokens**, asserted with a documented heuristic (`tokens ≈ chars / 4`, the standard English rule of thumb; no tokenizer dependency). The assertion covers the template + runtime facts + an empty AGENTS.md — user AGENTS.md content is runtime input and out of scope, but the empty case must fit with headroom.
- **Integration**: the composer's output is passed via the existing `AgentHarnessOptions.SystemPrompt`; the harness already promotes it to a `system` `AgentPromptSection`. One integration test runs the harness against a fake client and asserts the composed text arrives as the request's leading system message.

## Configuration files (`~/.lunate/`)

- **settings.json** (`SettingsStore`): `{ "schemaVersion": 1, "model": "...", "approval": "ask" | "auto", "output": { "toolResultLimit": 30000 } }`. All fields optional; missing file = defaults. Validation collects **all** problems into one clear error (unknown keys, bad types, invalid enum values). Defaults: approval `ask`, toolResultLimit 30000.
  - `approval` semantics: `ask` (default) — Execute-risk tool calls require approval once the approval flow lands (T-21+ consumes it); `auto` — approved automatically. Resolution lands here; the consumer is a later card (noted in the spec).
  - **Output limit is made real now**: `AgentHarnessOptions.ToolOutputLimit` (default 30,000) threads to both `ToolOutput.Truncate` call sites in `AgentHarness.Tools.cs`; test asserts a small limit produces the marker.
- **auth.json** (`AuthStore`): `{ "schemaVersion": 1, "keys": { "<name>": "<secret>" } }`. `Save` writes with owner-only permissions on POSIX (`File.SetUnixFileMode`: `UserRead | UserWrite`; Windows: documented best-effort, no mode). Secrets are never logged or echoed in errors (errors name the key, never the value).
- **Precedence**: environment wins over files. Provider defaults: `OPENAI_API_KEY` / `ANTHROPIC_API_KEY` override `keys.openai` / `keys.anthropic`. settings fields: `LUNATE_MODEL`, `LUNATE_APPROVAL`, `LUNATE_TOOL_OUTPUT_LIMIT` override file values. Custom named keys have no environment override in v1 (documented) — they exist for explicit per-model references.
- Paths are injectable everywhere (tests never touch the real `~/.lunate`).

## Per-model credential references (ai-layer)

- `ModelInfo` gains optional `AuthRef` (`"auth": "<name>"` in `models.json`, additive, null default; schema stays version 1 — unknown-field tolerance already exists).
- `ChatClientFactory` gains an optional credential-source seam (`Func<string, string?>? namedKeySource = null`); `ResolveApiKey` order:
  1. `model.AuthRef` set → resolve the named key via the seam; missing → clear error naming the auth.json key. **This is also the explicit opt-in for custom endpoints** — a custom endpoint with an `AuthRef` gets that key (the prior placeholder rule is refined, not weakened: without a reference it still gets the placeholder and still must not fail).
  2. No `AuthRef`, no custom endpoint → provider env var, else `keys.<provider>` via the seam, else the existing actionable error (text updated: mentions auth.json now that it exists).
  3. No `AuthRef`, custom endpoint → placeholder (unchanged).
- Seam default null ⇒ previous behavior exactly (environment only); the CLI wires `AuthStore` once frontends exist (T-17); tests wire fakes.

## Model discovery (`--discover`)

- `lunate --discover <name-or-url>`: a catalog model/endpoint name resolves via `ModelCatalog` (endpoint + optional `AuthRef` for the key); an `http(s)` URL is used directly (`OPENAI_API_KEY`-style resolution for its provider default, or no key for localhost). Then `GET {base}/models` (OpenAI shape `{ "data": [ { "id": ... } ] }`; tolerate extra fields, missing `data` = clear error), request timeout 30 s, injectable `HttpMessageHandler` (tests are fully offline).
- Output: a `models.json`-shaped draft (`schemaVersion` + `models` with `id`, `provider` when known, `endpoint` when custom, `supportsTools`/`contextWindow` omitted — unknown from the listing, curated metadata stays curated per the guide) printed to **stdout**; nothing is written to disk. Non-zero exit with a clear message on HTTP/auth/JSON errors. "Draft" stays a draft: the user pastes it into `~/.lunate/models.json`.

## Scope and hygiene

- No new packages; `System.Net.Http` and `System.Text.Json` only.
- PublicAPI tracking: `Lunate.Ai` (ModelInfo + factory) and `Lunate.Agent` (`ToolOutputLimit` option) are tracked — update `PublicAPI.Unshipped.txt` in both.
- Culture: all output and parsing invariant; tests also run under de-AT.
- Files: `SystemPrompt.cs`, `AgentsInstructions.cs`, `system-prompt.md`, `SettingsStore.cs`, `AuthStore.cs`, `DiscoverCommand.cs` (all `Lunate.Coding`), fetches via `HttpClient`; `Cli.cs` gains `--discover` + `--help` text.

## Deviations

- The composer takes the `Workspace` (so `RepoRoot ?? WorktreeRoot` and the canonical working directory come from one source). When the repository root is not an ancestor of the working directory (a linked worktree), only the working directory's `AGENTS.md` is read; the main checkout's file is not injected.
- The provider missing-key error text no longer mentions "T-16"; the two placeholder tests now assert that it names `auth.json` (the debt is closed by this change).
- `--discover` resolves Anthropic provider defaults and sends `x-api-key`/`anthropic-version` for symmetry; the design described OpenAI-compatible endpoints only.
- `AuthStore`/`SettingsStore` live in `Lunate.Coding`, which has no PublicAPI tracking; only `Lunate.Ai` and `Lunate.Agent` needed `PublicAPI.Unshipped.txt` updates.
