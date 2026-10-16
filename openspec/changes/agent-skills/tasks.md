# Tasks — agent-skills

Design-first change: groups 1–7 are the implementation plan for the follow-up apply; group 8 is this design draft itself (its archive lands with the change).

## 1. Discovery and validation

- [ ] 1.1 `SkillScanner` (Coding): walk worktree→repo root × {`.lunate`, `.agents`, `.claude`}/skills, then user scope; bounded (256/scope); returns descriptors with scope, path, validity, warnings.
- [ ] 1.2 Frontmatter parsing: required `name`/`description`; name = directory, `[a-z0-9-]+`; `allowed-tools` parsed-and-ignored; `disable-model-invocation`; malformed → warning list.
- [ ] 1.3 Collision resolution + one-time warnings (winner, shadowed path).
- [ ] 1.4 Tests: walk-up, precedence, collisions, invalid cases, bounds, symlink boundary.

## 2. Index and prompt wiring

- [ ] 2.1 Compose the index (`skills` section): lead line + `- name: description [path]`; cap 1,000 est. tokens; overflow line `+N more skills (see /skills)`.
- [ ] 2.2 Append at session start in `InteractiveSession` and `PrintMode` (parity test); record `SessionPromptSectionEntry`; replay round-trip test.
- [ ] 2.3 `/context` segment `skills: N (index ~M tokens)`.
- [ ] 2.4 Amend the core-prompt rule documents/tests: base prompt assertion unchanged; index excluded and asserted separately (quote the exact guide wording in the change during apply).

## 3. `/skills` view

- [ ] 3.1 `SkillsView` formatter: segments, markers (`command-only`, `shadows`, `(untrusted)`, `(invalid) — first sentence`), `skills: none found`.
- [ ] 3.2 Router: `/skills` built-in (help, completion list, notice path).
- [ ] 3.3 Tests: states matrix, empty, no file contents, no model call.

## 4. `/skill:<name>` invocation

- [ ] 4.1 Router: `/skill:` prefix; name + args; section edit `skill:<name>` (replace semantics) + entry; synthetic run turn; `$ARGUMENTS` substitution / `Arguments:` line.
- [ ] 4.2 Notices: unknown name; untrusted; (invalid names resolve to unknown).
- [ ] 4.3 Tests: body recorded + run starts; args; unknown; untrusted; re-invocation replaces; TUI E2E snapshot.

## 5. Trust

- [ ] 5.1 `TrustInventory`: project-skills item (dir content hash, repo identity); question details list skill names.
- [ ] 5.2 `/trust` list/clear + `lunate trust` non-interactive path include the skills record; untrusted exclusion + `(untrusted)` listing.
- [ ] 5.3 Tests: ask/skip, changed-content re-prompt, clear re-asks, CLI parity.

## 6. Extension skill sources

- [ ] 6.1 `ISkillSource` in `Lunate.Extensibility.Abstractions` (+ PublicAPI.Unshipped); service registration path; enumeration + tagging + per-extension budget + trust inheritance.
- [ ] 6.2 `adr/0017-addendum-skills.md` + `docs/spec/extensibility.md` catalogue entry (exact ADR wording in the change during apply).
- [ ] 6.3 Tests: tagged index, over-budget drop+log, untrusted contributes nothing; sample extension optional.

## 7. Sample story and docs

- [ ] 7.1 E2E: a Lunate session inside this repository discovers `.agents/skills`, the index carries the nine skills, one is used when asked (scripted), and `/skill:<name>` runs (groups 3/4 E2E cover the command side).
- [ ] 7.2 README/docs map + guide updates; `openspec validate --all --strict` clean; `scripts/verify.sh` green; **AGENTS.md untouched**; no new packages.

## 8. This draft

- [ ] 8.1 Fold the sign-off answers into `design.md` (SIGN-OFF notes) and the deltas; archive `agent-skills`; the follow-up apply card carries groups 1–7.
