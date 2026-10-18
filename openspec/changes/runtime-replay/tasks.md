# Tasks — runtime replay seam

## Implementation

- [x] 1. `ChatClientFactory`: read `LUNATE_REPLAY` (path); when set and no recording is requested, build the pipeline with `ReplayChatClient(fixture)` as the inner client — before credential resolution and without constructing a provider client.
- [x] 2. Precedence: `LUNATE_RECORD=1` (env or an explicit recorder decorator) wins over replay; document the rule at the seam.
- [x] 3. `docs/architecture.md`: one line next to the recording note naming `LUNATE_REPLAY`.

## Tests

- [x] 4. Factory-level: replay wired from the env var; no provider client constructed and no credential required (fixture-only run succeeds with an empty credential environment); recording wins when both are set.
- [x] 5. Session-level: one scripted interactive session replays a committed fixture keyless through the real harness and renders its turn.

## Gate

- [x] 6. `scripts/verify.sh` green on the final tree.
- [x] 7. Spec updated (`ai-layer`); `openspec validate --all --strict` green.

## Date pin (added after the first gate — the demo breaks daily without it)

- [x] 8. `SystemPrompt`: `LUNATE_DATE` (ISO date) replaces the prompt's UTC date; precedence parameter > variable > clock; invalid value fails actionably.
- [x] 9. Tests: pinned date applied; explicit `now` wins; invalid value errors.
- [x] 10. Spec: `agent-loop` delta; re-gate on the final tree.
