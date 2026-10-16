# Tasks — agent-skills

Design-first change: groups 1–7 are the implementation plan for the follow-up apply; group 8 is this design draft itself (its archive lands with the change).

## 1. Discovery and validation

- [x] 1.1 `SkillScanner` (Coding): walk worktree→repo root × {`.lunate`, `.agents`, `.claude`}/skills, then user scope; bounded (256/scope); returns descriptors with scope, path, validity, warnings.
- [x] 1.2 Frontmatter parsing: required `name`/`description`; name = directory, `[a-z0-9-]+`; `allowed-tools` parsed-and-ignored; `disable-model-invocation`; malformed → warning list.
- [x] 1.3 Collision resolution + one-time warnings (winner, shadowed path).
- [x] 1.4 Tests: walk-up, precedence, collisions, invalid cases, bounds, symlink boundary.

## 2. Index and prompt wiring

- [x] 2.1 Compose the index (`skills` section): lead line + `- name: description [path]` lines (descriptions truncated at 200 chars + `…`); order project > user > extension, each by name; cap 1,000 est. tokens; overflow footer `+N more skills (not indexed): <names>` (names only, comma-joins capped at 400 chars + `…`).
- [x] 2.2 Append at session start in `InteractiveSession` and `PrintMode` (parity test); built once, never rebuilt mid-run (cache-stability test); record `SessionPromptSectionEntry`; replay round-trip test.
- [x] 2.3 `/context` segment `skills: N (index ~M tokens)`.
- [x] 2.4 Amend the core-prompt rule documents/tests: base prompt assertion unchanged; index excluded and asserted separately (quote the exact guide wording in the change during apply).
- [x] 2.5 Read-tool allowance: per-run read-only access to every discovered skill directory incl. bundled files, crossing the ADR-0016 boundary for user/extension roots; child worktree runs compute their own allowance. Tests for both.
- [x] 2.6 Extension-line enforcement order: per-extension budget first (drop + log), then the global index cap.

## 3. `/skills` view

- [x] 3.1 `SkillsView` formatter: segments, markers (`command-only`, `shadows`, `(untrusted)`, `(invalid) — first sentence`), `skills: none found`.
- [x] 3.2 Router: `/skills` built-in (help, completion list, notice path).
- [x] 3.3 Tests: states matrix, empty, no file contents, no model call.

## 4. `/skill:<name>` invocation

- [x] 4.1 Router: `/skill:` prefix; name + args; submit the wrapped **user turn** (`<skill name dir>` + body) through the normal prompt submission path (same queue/steer behaviour while busy); record a user session entry carrying skill metadata; `$ARGUMENTS` substitution / `ARGUMENTS:` line.
- [x] 4.2 Notices: unknown name; untrusted (naming the fix: trust prompt / `lunate trust`); (invalid names resolve to unknown).
- [x] 4.3 Tests: wrapped body reaches the model + run starts; args; unknown; untrusted; busy-session queue parity; TUI E2E snapshot.

## 5. Trust

- [x] 5.1 `TrustInventory`: project-skills item (dir content hash, repo identity); question details list skill names.
- [x] 5.2 `/trust` list/clear + `lunate trust` non-interactive path include the skills record; untrusted exclusion + `(untrusted)` listing.
- [x] 5.3 Tests: ask/skip, changed-content re-prompt, clear re-asks, CLI parity.

## 6. Extension skill sources

- [x] 6.1 `ISkillSource` in `Lunate.Extensibility.Abstractions` (+ PublicAPI.Unshipped): id + enumeration of skill directories **on disk inside the extension's own folder** (v1 disk-only); registration path; tagging + per-extension budget + trust & disable-state inheritance; shadowing per project > user > extension.
- [x] 6.2 `adr/0017-addendum-skills.md` + `docs/spec/extensibility.md` catalogue entry (exact ADR wording in the change during apply).
- [x] 6.3 Tests: tagged index + readable files, over-budget drop+log, untrusted contributes nothing, shadowed copy warned.

## 7. Sample story, production composition, docs

- [x] 7.1 **Production composition test (host-level, not a test-double)**: through the real host, assert the skills index appears in the actual composed system prompt and that `/skill:<name>` content reaches the model — the "spec'd but not wired" guard.
- [x] 7.2 E2E: a Lunate session inside this repository discovers `.agents/skills`, the index carries the nine skills, one is used when asked (scripted), and `/skill:<name>` runs (groups 3/4 E2E cover the command side).
- [ ] 7.3 README/docs map + guide updates; `openspec validate --all --strict` clean; `scripts/verify.sh` green; **AGENTS.md untouched**; no new packages.

## 8. This draft

- [ ] 8.1 Fold the sign-off answers into `design.md` (SIGN-OFF notes) and the deltas; archive `agent-skills`; the follow-up apply card carries groups 1–7.
