## Why

A hash is only a contract if its input is the same everywhere. The install record learned this the hard way: `JsonSerializerOptions.NewLine` follows `Environment.NewLine`, so the same record was CRLF on Windows and LF on POSIX — and the `integrity` field is a SHA-256 over exactly those bytes. A record written by one machine would fail on another. The same shape sits in the extension lock, which is hashed and gates trust, and in the trust inventory and the MCP config hash.

The class is broader than hashing. Files Lunate owns (locks, the install record, `trust.json`, sessions, fixtures, exports, logs) are machine state and must be byte-identical wherever they are produced. Files the user owns are the opposite: a tool that rewrites someone's CRLF file as LF, or drops its BOM, corrupts a file it was asked to edit. `EditTool` already preserves both; `WriteTool` does not.

The repository also has no `.gitattributes` at all, so nothing stops a Windows checkout from handing a golden frame CRLF bytes it was never committed with.

## What Changes

- **The rule becomes a stated contract** in `AGENTS.md`: files Lunate owns or generates are LF and UTF-8 without BOM on every platform; files the user owns keep their line endings and BOM; a new file follows `.editorconfig`, then `.gitattributes`, else the platform default; a digest over user-owned content normalizes line endings to LF before hashing.
- **Every digest is audited and pinned.** The lock, the install record (already fixed), the trust inventory, the MCP config hash and the AI fixture digest compute over canonical bytes with `NewLine = "\n"`; a test per format proves the bytes carry no `\r` and that re-writing is byte-identical.
- **Digests over user-owned content normalize.** The single-file extension source digest is computed over text with `CRLF` mapped to `LF`, so a digest pinned on Linux still matches a Windows checkout with `autocrlf`. The consequence is accepted and documented: a pure line-ending change does not re-prompt.
- **`WriteTool` preserves what it rewrites**: an existing file keeps its dominant line ending and its BOM; a new file follows `.editorconfig` (then `.gitattributes`), else the platform default. `EditTool`'s existing behaviour is stated as the rule it already follows, including that a mixed-ending file is unified to its dominant ending.
- **`.gitattributes` pins the bytes git hands out**: LF for text that is hashed, compared or generated — including every golden frame — and `-text` for the edit corpus, whose CRLF files are the subject under test and must never be converted. All committed text fixtures outside that corpus were checked to be LF before the attribute landed.

## Capabilities

### Modified Capabilities

- `repo-quality`: canonical bytes for files Lunate owns, normalization for digests over user-owned content, and the repository-level attribute rules.
- `agent-tools`: the write and edit tools preserve the line endings and BOM of the file they change.

## Impact

- `src/Lunate.Extensibility/ExtensionLockJson.cs`, `src/Lunate.Coding/{TrustInventory,McpConfigTrustHash,WriteTool}.cs`, `src/Lunate.Ai/FixtureFormat.cs`, the session writer, the single-file source digest, `.gitattributes`, `AGENTS.md`.
- No public API change; no new package; no behaviour change for a user who works on one machine with one line ending — which is exactly why this class survives until two machines meet.
