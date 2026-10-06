## 1. Factory

- [x] 1.1 `ChatClientFactory`: shared `ResolveApiKey(model, variable)` — declared endpoint → placeholder credential; default endpoint → environment key or actionable error; both `CreateOpenAiClient` and `CreateAnthropicClient` use it (symmetric)
- [x] 1.2 `DefaultRecordingPath` → `~/.lunate/recordings/<timestamp>-<rand>.jsonl` (`SpecialFolder.UserProfile`)

## 2. Tests

- [x] 2.1 Update `Create_anthropic_client_disables_transport_retries` (custom endpoint now asserts the placeholder, not the environment key) and add the default-endpoint counterpart asserting the environment key
- [x] 2.2 New: custom endpoint + `OPENAI_API_KEY` set → placeholder (resolution seam); custom endpoint + no `ANTHROPIC_API_KEY` → placeholder, no throw; default endpoint + no key → actionable error (both providers covered)
- [x] 2.3 New: the recordings test pins the user-scoped path (the older `artifacts/` assertion updated in place; the `.gitignore` half dropped — the path is outside the repository now)

## 3. Spec

- [x] 3.1 `ai-layer` delta: pipeline order (with accumulator) + recordings default path + credential boundary, with scenarios
- [x] 3.2 `ai-layer` spec Purpose line corrected (pipeline order mention)

## 4. Close

- [x] 4.1 `scripts/verify.sh` green (203 tests, 2 live-skipped); `openspec validate fix-factory-safety --type change --strict` valid; self-review pass
