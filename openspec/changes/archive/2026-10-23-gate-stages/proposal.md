## Why

`AGENTS.md` says `scripts/verify.sh` must pass "before a change is done", and every agent has read that as "before the next edit is committed". The real practice — and the only one that fits a 20-minute four-leg CI gate — is a fast gate while iterating and the full gate before review. The gap is not academic: tonight it produced a session that ran the full gate nine times to move nine small commits, and an agent that could not tell whether it was allowed to push.

## What Changes

- `AGENTS.md` states the two stages explicitly: the fast gate while iterating, the full gate before ready-for-review and before done, `ci-ok` green before merge.
- A `repo-quality` requirement records the same rule where the specs can validate it.
- No change to what either gate runs; `scripts/verify.sh` stays the definition of the full gate.

## Impact

- `AGENTS.md` (protected: the maintainer approves the exact text in `design.md`).
- `openspec/specs/repo-quality/spec.md` (one requirement added).
