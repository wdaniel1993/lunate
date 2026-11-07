## 1. Prompt surface

- [ ] 1.1 `ISecretPrompt` gains `ReadTextAsync` delegating to `ReadAsync` by default (scripted prompts unchanged); `TuiSecretPrompt` implements masked and visible reads through one core state machine, visible pastes stripping only line breaks; `SecretPromptState` carries `Masked` and drives rendering, the masked prompt never posting plaintext to the live state.
- [ ] 1.2 The wizard's typed credential uses the masked read and every other wizard typed value uses the visible read; `/auth`'s custom row reads id, base URL and model id visibly and the key masked; `/login` keeps the masked read.

## 2. Tests

- [ ] 2.1 `SecretMaskingAuditTests` walks the wizard's custom path, the credential-reference branch, `/auth` custom and preset, and `/login` through a recording `ISecretPrompt` and asserts `masked == label.StartsWith("API key")`; visible checks pin that pasted spaces survive and a newline is never Enter.
- [ ] 2.2 Existing secret-prompt and auth-containment suites updated where their doubles or assertions changed; focused runs green.

## 3. Gate

- [ ] 3.1 Release build, touched test projects under default and de-AT culture, `dotnet csharpier check .`, `openspec validate mask-credential-prompt-only --strict` all pass; archive lands as the final commit.
