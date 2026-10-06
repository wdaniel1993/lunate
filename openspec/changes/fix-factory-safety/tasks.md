## 1. Factory

- [ ] 1.1 `ChatClientFactory`: shared `ResolveApiKey(model, variable)` — declared endpoint → placeholder credential; default endpoint → environment key or actionable error; both `CreateOpenAiClient` and `CreateAnthropicClient` use it (symmetric)
- [ ] 1.2 `DefaultRecordingPath` → `~/.lunate/recordings/<timestamp>-<rand>.jsonl` (`SpecialFolder.UserProfile`)

## 2. Tests

- [ ] 2.1 Update `Create_anthropic_client_disables_transport_retries` (custom endpoint now asserts the placeholder, not the environment key) and add the default-endpoint counterpart asserting the environment key
- [ ] 2.2 New: custom endpoint + `OPENAI_API_KEY` set → placeholder (resolution seam); custom endpoint + no `ANTHROPIC_API_KEY` → placeholder, no throw; default endpoint + no key → actionable error (both providers covered)
- [ ] 2.3 New: `DefaultRecordingPath` is user-scoped (under the profile's `~/.lunate/recordings/`, not the working directory)

## 3. Spec

- [ ] 3.1 `ai-layer` delta: pipeline order (with accumulator) + recordings default path + credential boundary, with scenarios
- [ ] 3.2 `ai-layer` spec Purpose line corrected (pipeline order mention)

## 4. Close

- [ ] 4.1 `scripts/verify.sh` green; `openspec validate fix-factory-safety --type change --strict`; self-review pass
