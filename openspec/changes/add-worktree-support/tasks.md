# Tasks — add-worktree-support

## 1. Docs

- [ ] 1.1 ADR-0017: "Workspaces and repository identity" decision block (workspace object, identity, sessions, trust, services, approval, discovery, restore hint, extension territory)
- [ ] 1.2 `docs/spec/extensibility.md`: new section + fitness suite row `worktree-tasks` + out-of-process card renumbered to T-52
- [ ] 1.3 Guide: series gains T-51 (worktree-tasks) and T-52 (out-of-process); fitness list updated
- [ ] 1.4 Proposal/design/tasks for this change; `.openspec.yaml` with `skip_specs: true`

## 2. Close

- [ ] 2.1 markdownlint clean; `bash scripts/verify.sh` green; `openspec validate add-worktree-support --type change --strict`; self-review (ADR-0017 flip already on `main`; no contradictions with ADR-0006/T-25/T-39 cards)
