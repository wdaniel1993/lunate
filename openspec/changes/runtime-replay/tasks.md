# Tasks — runtime replay seam

## Implementation

- [ ] 1. `ChatClientFactory`: read `LUNATE_REPLAY` (path); when set and no recording is requested, build the pipeline with `ReplayChatClient(fixture)` as the inner client — before credential resolution and without constructing a provider client.
- [ ] 2. Precedence: `LUNATE_RECORD=1` (env or an explicit recorder decorator) wins over replay; document the rule at the seam.
- [ ] 3. `docs/architecture.md`: one line next to the recording note naming `LUNATE_REPLAY`.

## Tests

- [ ] 4. Factory-level: replay wired from the env var; no provider client constructed and no credential required (fixture-only run succeeds with an empty credential environment); recording wins when both are set.
- [ ] 5. Session-level: one scripted interactive session replays a committed fixture keyless through the real harness and renders its turn.

## Gate

- [ ] 6. `scripts/verify.sh` green on the final tree.
- [ ] 7. Spec updated (`ai-layer`); `openspec validate --all --strict` green.
