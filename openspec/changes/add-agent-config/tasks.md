# Tasks: agent config, system prompt and model discovery (T-16)

## 1. System prompt and project instructions

- [x] 1.1 `src/Lunate.Coding/system-prompt.md` (embedded resource) refined from the guide draft; `SystemPrompt.cs` composer (named-token replacement, runtime facts: OS, shell via `ShellResolver`, canonical cwd, UTC date, tool names); `AgentsInstructions.cs` chain (`RepoRoot ?? WorktreeRoot` → working dir, path-headed blocks)
- [x] 1.2 Budget assertion: composed prompt (template + facts + empty AGENTS.md) under 1,000 tokens via the documented chars/4 heuristic; template holds headroom
- [x] 1.3 Harness integration test: composer output passed as `AgentHarnessOptions.SystemPrompt` arrives as the leading system message of the request (fake client); chain test: nested AGENTS.md files concatenate root→leaf with the leaf most specific; missing files skipped

## 2. Configuration

- [ ] 2.1 `SettingsStore.cs`: schema v1 (`model`, `approval` ∈ {ask, auto}, `output.toolResultLimit`), defaults, all-problems-at-once validation, injectable path
- [ ] 2.2 Environment overrides (`LUNATE_MODEL`, `LUNATE_APPROVAL`, `LUNATE_TOOL_OUTPUT_LIMIT`); precedence tests file < env
- [ ] 2.3 `AgentHarnessOptions.ToolOutputLimit` (default 30_000) threaded to both `ToolOutput.Truncate` call sites; test with a small limit asserts the marker; PublicAPI.Unshipped updated (Agent)
- [ ] 2.4 `AuthStore.cs`: schema v1 named keys, `Save` with owner-only POSIX permissions, missing-key errors name the key but never print the value; env override for provider defaults; tests never touch the real home

## 3. Per-model credential references

- [ ] 3.1 `ModelInfo.AuthRef` ("auth" in models.json, additive null default); `ChatClientFactory` named-key seam; `ResolveApiKey` precedence (AuthRef → named key; default endpoint → env > auth.json; custom endpoint → placeholder unless AuthRef); existing placeholder tests stay green; new tests for the reference path; error text updated; PublicAPI.Unshipped updated (Ai)

## 4. Model discovery

- [ ] 4.1 `DiscoverCommand.cs`: target = catalog name or URL; `GET {base}/models` via injectable `HttpMessageHandler` (30 s timeout); OpenAI-shape parsing with clear errors; draft (schemaVersion + ids, provider/endpoint when known) printed to stdout; nothing written; `Cli.cs` wiring + help text
- [ ] 4.2 Tests: fake handler round-trip, missing `data`, non-200, auth via seam, URL target, name target; full offline

## 5. Close

- [ ] 5.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-agent-config --type change --strict`; self-review; commit per group; no push
