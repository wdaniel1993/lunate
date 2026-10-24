## Context

`SessionEntry` is a closed hierarchy and every content entry already carries `parentId`; `SessionCompactionEntry.Replaces` names the entries a compaction covers; the header is written once. Nothing in the format is linear by construction — only the readers are.

## Decisions

1. **One file per session; the tree lives inside it.** `/tree`, branch switches and in-place rewinds append to the session's own file. `/fork` and `/clone` write a new file with an `origin` header. A file per branch would turn `/tree` into a directory read and break "one session = one file" for copy, share and resume.

2. **Schema 3 for every new session, with two additions.** `SessionActiveLeafEntry(Id, ParentId, Timestamp, LeafId)` — written only when the leaf moves backwards; the last one wins. Header fields `origin: {sessionId, entryId}` and `branched: true` on fork/clone output. Existing 1/2/3 fixtures stay byte-identical; the new golden `golden-branch-tree.jsonl` covers the new kinds.

3. **The leaf rule, verbatim in the spec:** *the active leaf is the most recently appended entry, unless a later active-leaf entry points elsewhere.* Every reader asks one function; no reader asks "is this a tree session?".

4. **Path-scoped context and per-path compaction.** The request is the chain root→leaf. A compaction applies iff the compaction entry is on the chain; its `replaces` ids off the chain are ignored, not errors. Single-path digests are unchanged, which is what the existing goldens pin.

5. **The branch summary is a first-class entry on the new path, never a compaction.** `SessionBranchSummaryEntry(Id, ParentId = the leaf moved to, Timestamp, FromLeafId, Summary)`. Off by default; when on, one model call on the session's **active** model, with the token estimate and "one model call" shown before confirmation, and the usage recorded normally. No summary on `/fork` or `/clone`.

6. **Writes refused while a run is active.** Fork, clone and rewind refuse with a notice and write nothing; the check precedes any file work. `/tree` reads only and stays available.

7. **The fork point must be a message boundary.** Because writes are refused during a run, mid-run points are unreachable by construction rather than by a special case.

8. **`/tree` reuses the transcript viewer.** Same rendering, `j`/`k`, `/` filter, `Enter` puts the picked user message back in the editor and never resends. No Alt bindings, per the Termux rule.

## Golden files

This change updates goldens deliberately: `golden-branch-tree.jsonl` is added, and the schema-3 fixture gains the two new kinds only if it needs them for coverage. The PR description shows the golden diff. AGENTS.md's golden rule names this exception.

## Risks

- **Two notions of current.** Mitigated by decision 3 and by a regression test that a file with no `activeLeaf` behaves exactly as today.
- **Dead branches bloat a file.** Accepted: they are recoverable history, and compaction scoped to the active path is what keeps the *request* small.
- **A summary that reads like a compaction.** Distinct kind, no `replaces`; the spec says it explicitly.

## Out of scope

`/fork` at an assistant message (message boundary only), merging branches, and any tree support in ACP or print mode.
