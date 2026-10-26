# Tasks

## Rule and repository attributes

- [ ] 1.1 `AGENTS.md`: the approved line-ending rule, verbatim — LF and UTF-8 without BOM for files Lunate owns or generates; preserved endings and BOM for files the user owns; a new file follows `.editorconfig`, then `.gitattributes`, else the platform default; digests over user-owned content normalize to LF.
- [ ] 1.2 `.gitattributes`: `eol=lf` for machine-owned text and every golden frame; `-text` for `tests/fixtures/edit-corpus/**`; state that all committed text fixtures outside that corpus were verified LF before the pin.

## Digests

- [ ] 2.1 Audit every digest: list the input, whether its bytes can differ across platforms, and the fix. Cover the install record (already fixed in #169), extension lock, trust inventory, MCP config hash, AI fixture digest, session naming, and the single-file source digest.
- [ ] 2.2 Pin `NewLine = "\n"` on every serializer whose output is hashed (`ExtensionLockJson`, `TrustInventory`, `McpConfigTrustHash`, `FixtureFormat`); a test per format asserts the bytes carry no `\r` and that re-writing is byte-identical.
- [ ] 2.3 Normalize user-owned text to LF before hashing (single-file extension source first; content-hashed skills and the MCP configuration where they cover user text); a test proves CRLF and LF sources yield the same digest.
- [ ] 2.4 Session file: a test that each record ends with `\n` on every platform and that a session written on one and read on another parses identically.

## Tools

- [ ] 3.1 `WriteTool`: preserve the target's dominant line ending and BOM when the file exists; tests: CRLF file stays CRLF, BOM survives a rewrite, a new file follows `.editorconfig`.
- [ ] 3.2 `WriteTool`: a new file resolves its line ending from `.editorconfig`, then `.gitattributes`, else the platform default; a test covers the `.gitattributes` step and the fallback.
- [ ] 3.3 `EditTool`: keep the existing dominant-ending behaviour and add the mixed-ending test (unified to dominant, no other bytes changed).

## Proof

- [ ] 4.1 A committed fixture per hashed format, written on POSIX, verified on all three CI legs.
- [ ] 4.2 The trust documentation states that a pure line-ending change does not re-prompt.
- [ ] 4.3 Gate: `scripts/verify.sh` passes on this tree.
