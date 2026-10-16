# Tasks

## 1. Draft + sign-off

- [ ] 1.1 Draft proposal/design/tasks/spec deltas; `openspec validate --all --strict` green.
- [ ] 1.2 Branch `change/standard-mcp-locations`; commit + push the draft.
- [ ] 1.3 Sign-off card (D6 decisions) + the same summary in chat; fold the answers into design.md as SIGN-OFF notes before applying.

## 2. Apply

- [ ] 2.1 Parser: `${VAR}` / `${VAR:-default}` expansion in `command`, `args`, `env` (raw definitions preserved for hashing); unset-without-default warning naming server + variable, literal kept.
- [ ] 2.2 Composition: read `~/.agents/mcp.json`, `.agents/mcp.json`, root `.mcp.json`; precedence + merge per D1; collision log lines naming both files.
- [ ] 2.3 Per-server approval: canonical definition hash; trust-store section keyed by repository identity + definition hash; compose-time prompts (full command, one per server, n-of-m); non-interactive refusal with the existing hint.
- [ ] 2.4 `lunate trust`: new project files as pending decisions; per-server approvals in inspect/`--yes`/digest/`--clear`; docs (Cli help).
- [ ] 2.5 Tests for every behaviour change (parser/expansion, precedence, collisions, store keying, prompt flow, trust command, `/mcp` provenance + the missing-variable warning); `docs/spec/` update if the file mirrors this surface.

## 3. Gate

- [ ] 3.1 `scripts/verify.sh` green (both cultures); csharpier/style clean.

## 4. Review

- [ ] 4.1 Adversarial review; fix findings; focused re-review on fixes.

## 5. Archive + PR

- [ ] 5.1 Archive; tick tasks; push.
- [ ] 5.2 PR (self-contained body: the problem, what lands, review map, verification); watch CI to green; closing comment; the maintainer merges.
