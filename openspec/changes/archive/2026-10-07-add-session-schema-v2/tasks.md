# Tasks

## 1. Schema 2 format (TDD, red-first)

- [x] 1.1 `SessionFormat.SchemaVersion` = 2; loader accepts 1 and 2 (v1 loads unchanged); a newer schema still fails with a message naming schema and file
- [x] 1.2 Header: optional `repo` and `worktree` fields (omitted when null; v1 headers parse with nulls); `Session.Create(path, cwd, repo: null, worktree: null)` writes them
- [x] 1.3 `SessionPaths.ForRepository(repoIdentity, worktreePath)` → `~/.lunate/sessions/<hash8(repo)>/<hash8(worktree)>/`; `ForProject` stays as the non-repository fallback
- [x] 1.4 New entries: `SessionActiveToolsEntry` (`activeTools`), `SessionPromptSectionEntry` (`promptSection`), `SessionChildSessionEntry` (`childSession`), `SessionNestedCallsEntry` (`nestedCalls`)
- [x] 1.5 `nestedCalls` bounds enforced at append: args truncated at 200 characters with a `…` marker, at most 32 calls per entry, status in `ok | error | cancelled`
- [x] 1.6 `SessionExtensionEntry`: type `ext/<extension-id>/<type>`, envelope + opaque payload preserved as raw JSON text and re-emitted byte-for-byte
- [x] 1.7 Unknown content: message round-trip keeps the original node when re-serialization is not deep-equal (`RawMessageJson` fallback)
- [x] 1.8 `Session` append methods: `AppendActiveTools`, `AppendPromptSection`, `AppendChildSession`, `AppendNestedCalls`; `Load` handles v1 and v2 and every entry kind
- [x] 1.9 Goldens: v2 header (repo/worktree), each new entry, ext entry with a byte-tricky payload, unknown-content message; v1 fixtures re-asserted as load-compatibility
- [x] 1.10 ADR-0018 (golden change per AGENTS.md) in both copies, byte-identical

## 2. Harness wiring (nestedCalls)

- [x] 2.1 `RunNestedToolAsync` records `(name, capped args, status, duration)` per nested call; `RunToolAsync` appends one bounded `nestedCalls` entry per top-level call that had nested calls — never results
- [x] 2.2 Tests: caps applied; no entry when there were no nested calls; the entry survives `Load`

## 3. Close

- [x] 3.1 `PublicAPI.Unshipped.txt` for the new surface; `dotnet csharpier format .`
- [x] 3.2 `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-session-schema-v2 --type change --strict`
- [x] 3.3 Self-review; commit per group
