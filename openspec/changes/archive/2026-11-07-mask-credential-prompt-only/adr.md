# ADR Review Manifest

- Status: completed
- Review date: 2026-11-07

## Review Summary

ADR review completed for this change: a prompt-interaction fix scoped to the setup wizard, `/auth`
and `/login`. No durable architectural decision beyond what is already recorded.

## In-Force ADRs Reviewed

- None apply - the change touches the internal prompt interaction (`ISecretPrompt`,
  `TuiSecretPrompt`, `SecretPromptState`); no ADR records the prompt surface.
- ADR-0003 (the agent loop runs tools) checked as context: unaffected — no tool or loop behaviour
  changes.

## New Durable ADRs Created

- None - no major durable architectural decisions were introduced.
