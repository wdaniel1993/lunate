## 1. Format

- [ ] 1.1 Red: a session with no `activeLeaf` loads and digests exactly as today.
- [ ] 1.2 Red: `activeLeaf` and `branchSummary` round-trip byte for byte in a new golden.
- [ ] 1.3 Red: an `activeLeaf` naming an unknown id is refused naming the file and id.
- [ ] 1.4 Entry records, serializer/deserializer, header `origin`/`branched`; `schema: 3` on write.

## 2. Context

- [ ] 2.1 Red: the request carries only the active path.
- [ ] 2.2 Red: a compaction off the path is ignored; one on the path still applies.
- [ ] 2.3 Leaf resolution in one place (`SessionTree`), used by the request builder and the viewers.
- [ ] 2.4 Digest equality test against the single-path fixtures.

## 3. Summary on leave

- [ ] 3.1 Red: enabled writes one `branchSummary` with `parentId` = target leaf and no `replaces`.
- [ ] 3.2 Red: disabled makes no model call and appends only `activeLeaf`.
- [ ] 3.3 Prompt shows estimate + "one model call"; usage recorded in session accounting.

## 4. Commands

- [ ] 4.1 `/tree` over the transcript model: `j`/`k`, `/` filter, `Enter` restores without sending.
- [ ] 4.2 `/fork <entry>`: new file, `origin`, prefix entries, confirmation naming both paths.
- [ ] 4.3 `/clone`: full copy, `origin`, confirmation.
- [ ] 4.4 Refusal while running for fork/clone/rewind, before any file work; `/tree` still opens.
- [ ] 4.5 Help rows and guide rows; no Alt bindings.

## 5. ADR and gate

- [ ] 5.1 ADR-0018 addendum: tree model, leaf rule, `origin`, summary placement.
- [ ] 5.2 AGENTS.md golden exception named for this change only.
- [ ] 5.3 `openspec validate --strict`; csharpier; Release build; focused suites; full `scripts/verify.sh`; `ci-ok`; archive as the final commit of the same PR.
