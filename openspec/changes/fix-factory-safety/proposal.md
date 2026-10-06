## Why

A full-repo review (2026-10-06) found three factory issues: (1) the provider environment key is sent to whatever endpoint a model declares, so a real `OPENAI_API_KEY` / `ANTHROPIC_API_KEY` leaks to any third-party or home-lab endpoint added to `~/.lunate/models.json` — and the two adapters behave asymmetrically (Anthropic throws for a custom endpoint without a key, OpenAI uses a placeholder); (2) recordings default to `artifacts/recordings/` relative to the current directory, so enabling `LUNATE_RECORD=1` inside someone else's repository writes prompts and tool output into that repository; (3) the `ai-layer` spec's pipeline-order requirement omits the accumulator, contradicting the code and the spec's own accumulator requirement. This change fixes the factory and the spec; no product behaviour changes for default endpoints.

## What Changes

- **Credential boundary**: a shared resolver — the environment key is used only when the model uses the provider's **default** endpoint; a model with a declared endpoint always gets the placeholder credential, for both adapters, and never fails for a missing key. Explicit per-model key references remain T-16 scope.
- **Recordings default**: `~/.lunate/recordings/` (user-scoped), never a path inside the current working directory; `LUNATE_RECORD_PATH` still overrides.
- **Spec**: `ai-layer` gains the corrected pipeline order (with the accumulator), the recordings default-path rule and the credential-boundary requirement with scenarios; the spec Purpose line is corrected too.
- Out of scope: per-model key references and `auth.json` (T-16); no new packages; no ADR — the boundary is a policy captured by the spec (ADR-0010 already owns adapter construction).

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `ai-layer`: pipeline order includes the accumulator; recordings default is user-scoped; the environment key reaches only default endpoints (symmetric across adapters).

## Impact

- `src/Lunate.Ai/ChatClientFactory.cs` (credential resolution, default recording path), `tests/Lunate.Ai.Tests/ChatClientFactoryTests.cs` (updated + new cases), `openspec/specs/ai-layer/spec.md` (Purpose line). No public API change; no dependency change.
