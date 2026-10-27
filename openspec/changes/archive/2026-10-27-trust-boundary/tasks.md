# Tasks

## 1. Risk and approvers

- [x] Derive `ReadOnly` only for a plain read-only tool; `Execute` for no annotations, destructive or open-world
- [x] ACP approver bypasses only `ReadOnly`
- [x] `NonInteractiveApprover` never allows `Execute` under any policy

## 2. High-risk calls

- [x] One classification for `lunate trust|install|uninstall` through bash and state-root targeting
- [x] Always prompt under ask and auto-edit; allow-always does not cover it
- [x] `--yolo` prints one startup line saying the approval boundary is off

## 3. Trust answers belong to the user

- [x] A project trust handler is a veto
- [x] `trust --yes` requires a terminal; `--yes --digest` does not

## 4. Documents

- [x] ADR 0017 and its addendum, the guide's security section, the extension docs
- [x] State the limits: panel is the boundary, `--yolo` removes it, a shell can write the state root, Windows has no sandbox

## 5. Gate

- [x] Targeted suites and format checks
- [x] Full `scripts/verify.sh` (queue 2)
- [x] Archive (queue 2, final commit of the PR)
