## Context

Review findings: the environment key follows the endpoint (leak), the adapters are asymmetric, recordings land in the current directory, and the spec's pipeline order omits the accumulator. The catalog makes the fix clean: built-in models declare no endpoint, so `ModelInfo.Endpoint is not null` is an exact "custom endpoint" discriminator.

## Goals / Non-Goals

**Goals:** close the key leak with zero new configuration surface; make both adapters structurally symmetric (the AGENTS.md sibling rule); user-scoped recordings by default; spec and code agree on the pipeline order.

**Non-Goals:** per-model key references / `auth.json` (T-16); changing default-endpoint behaviour; touching the recorder itself (it already creates directories).

## Decisions

- **One shared resolver, both adapters**: `ResolveApiKey(model, variable)` — declared endpoint → placeholder credential (no environment key, no failure); default endpoint → environment key or an actionable error naming the variable and T-16. One code path makes the sibling adapters symmetric by construction and gives tests a single seam.
- **Custom endpoint = `Endpoint` declared**: built-ins declare none, so there is no guessing about default URLs. Consequence: a user who spells out the provider's own default URL gets the placeholder — documented in the spec; acceptable and explicit.
- **Recordings default under `~/.lunate/recordings/`**: `Environment.SpecialFolder.UserProfile` + `.lunate/recordings/`; the recorder creates the directory. `LUNATE_RECORD_PATH` still wins. Never relative to the working directory — prompts and tool output must not land in a user's repository.
- **Spec first for the order fix**: the pipeline-order requirement and Purpose line are corrected to include the accumulator (code, `architecture.md` and the guide already say so).

## Risks / Trade-offs

- [Users with custom endpoints who relied on the env key silently lose it] → that is the point of the fix; T-16's explicit references restore custom-endpoint auth deliberately; the spec states it.
- [A spelled-out default URL gets the placeholder] → documented; the discriminator stays trivial and predictable.

## Migration Plan

Not applicable — pre-release; custom endpoints that need auth wait for T-16's explicit references.

## Open Questions

- None blocking.
