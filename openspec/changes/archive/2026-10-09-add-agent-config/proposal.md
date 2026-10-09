# Proposal: agent config, system prompt and model discovery (T-16)

## Why

The loop works, the tools work, sessions persist — but nothing tells the model who it is or how this machine is set up, and nothing reads the user's configuration. Two debts say "T-16":

- `ChatClientFactory.ResolveApiKey` has a comment: *"explicit per-model key references arrive with T-16"* — and its missing-key error text promises "settings and auth.json support arrive with T-16".
- `openspec/specs/ai-layer` — "Local endpoints without a dummy key": *"Explicit per-model key references arrive with the settings work (T-16)."*

The guide pins the shape: one embedded `system-prompt.md` under 1,000 tokens (asserted), runtime facts and the `AGENTS.md` chain appended; `~/.lunate/settings.json` + `~/.lunate/auth.json` with environment overrides; a `--discover` helper that drafts user-catalog entries. Without this card, print mode (T-17) and the TUI would each have to invent their own prompt and config.

## What changes

- **System prompt and project instructions** (agent-loop delta, code in `Lunate.Coding`): embedded `system-prompt.md` template; a composer that fills OS, shell, working directory and date; the `AGENTS.md` chain from the repository root down to the working directory appended; the result feeds the existing `AgentHarnessOptions.SystemPrompt`. The prompt budget is asserted.
- **Configuration** (new `agent-config` capability): `~/.lunate/settings.json` (default model, approval, output limits) and `~/.lunate/auth.json` (named API keys, restrictive file permissions), both with documented environment overrides; the harness gains a `ToolOutputLimit` option threaded to the truncation call so the output-limit setting is real.
- **Per-model credential references** (ai-layer delta): `models.json` entries may name a key (`"auth": "<name>"`) resolved through `auth.json` — the explicit opt-in that lets real keys reach non-default endpoints, closing the T-16 debt.
- **Model discovery**: `lunate --discover <provider-or-url>` lists an OpenAI-compatible endpoint's models and prints a `models.json`-shaped draft for the user catalog (nothing is written silently).

## Done when

Prompt under 1,000 tokens (asserted). Plus: precedence and permission tests for both config files, credential-resolution tests on the factory, discovery parsing tests with a fake transport. No new packages.
