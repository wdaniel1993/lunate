## What this is

<!-- One paragraph: the change, the OpenSpec change it implements, and why it exists. -->

## What lands

<!-- Bullet list of the concrete artifacts. Call out anything the change did not ask for. -->

## Review map

<!-- Where to look first, in order; name the load-bearing diffs and the review rounds. -->

## Verification

- [ ] `scripts/verify.sh` green (build, tests, publish, budgets, formatting, docs lint, public API)
- [ ] `openspec validate <change> --type change --strict` green
- [ ] Gate self-tests green (`scripts/gate-tests.sh`)
- [ ] Tests added or updated for every behaviour change
- [ ] No public API change unless the change allowed it
- [ ] No new package without an ADR

## Notes

<!-- Follow-ups, surprises, and anything the reviewer should know. -->
