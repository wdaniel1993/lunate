## Context

A digest is a promise that the same input is the same bytes everywhere. Three things break that
promise independently, and the audit below separates them:

1. **The serializer followed the platform.** `JsonSerializerOptions.NewLine` defaults to
   `Environment.NewLine`, so an indented document is CRLF on Windows and LF on POSIX.
2. **A digest covered bytes the checkout chooses.** `core.autocrlf` decides whether a working tree
   file has CRLF or LF, so a digest over a user-owned source file was a digest over a git setting.
3. **A writer followed the platform.** `StreamWriter.NewLine` is also `Environment.NewLine`, so a
   recorded fixture was not the same file on two machines.

## Goals / Non-Goals

**Goals.** Every digest that is compared across machines computes over bytes that do not depend on
the platform; every digest over user-owned text computes over text that does not depend on the
checkout's line endings; the write and edit tools never corrupt a file the user owns.

**Non-Goals.** No session format change (the writer already emits LF); no new file format for a
digest that has no file; no attempt to detect whether a byte sequence "is text" — the
normalization is byte-level and documented as such.

## Decisions

### D1: Pin `NewLine = "\n"` at every serializer whose output is hashed

`ExtensionLockJson`, `TrustInventory`, `McpConfigTrustHash` and `FixtureFormat` all get the pin.
Where the document is unindented the pin is invisible today — and it is exactly what keeps the
digest stable the day someone writes the document indented, or the framework changes a default.
`TrustInventory` and `McpConfigTrustHash` share one pin through `CanonicalJson`, so the two cannot
drift apart.

### D2: Normalize user-owned text to LF before hashing

`ExtensionTrustStore.ComputeContentHash(directory)` and `ComputeFileContentHash(path)` — the
directory hash behind project-extension and project-skill trust, and the single-file source hash —
normalize the CRLF pair to LF before appending. `SingleFileExtensionDigest` does the same for the
source bytes, so `ComputeSource`, `ComputeUnrestored` and `Compute` all agree with a Windows
checkout. `McpConfigTrustHash` normalizes the definition's string values (name, command, args, env
values, cwd); the file's own formatting never entered that hash because it parses first.

The accepted consequence, stated in the docs and in the spec: a pure line-ending change does not
re-prompt for trust. That is the intent — the user approved the text, not the checkout's
`autocrlf`.

### D3: `WriteTool` preserves what it rewrites, and asks a policy only for a new file

An existing file is read with `ReadRaw` (text plus byte-order mark flag), rewritten with its
dominant ending and its BOM. A new file resolves its ending from the nearest `.editorconfig`
(`end_of_line`), then `.gitattributes` (`eol=`), else `Environment.NewLine`, and its BOM from
`.editorconfig` `charset = utf-8-bom`. `EditTool` already did the first half; the dominant ending
is now counted over the whole file rather than read off the first line, so "dominant" means what
the word says on a mixed file.

### D4: A test per hashed format, and a committed fixture per hashed format

Each serializer has a test that its canonical bytes carry no carriage return and that
write → read → write is byte-identical. The formats with a committed document get one fixture
under `tests/fixtures/digests/`, written with LF, that every CI leg reads and asserts a digest
over — so three operating systems verify the same bytes, not three reconstructions of them. The AI
fixture format and the session format already have committed fixtures (`tests/fixtures/streams/`,
`tests/fixtures/sessions/`); those suites gained the LF assertion instead of a second fixture.

## The audit

Every place a digest is computed, what it covers, whether its bytes can differ across platforms,
and what happened to it.

| Digest | Where | Input | Platform-dependent today? | Verdict |
| --- | --- | --- | --- | --- |
| Install record `integrity` | `PackageInstallRecordJson` | SHA-256 of the indented document with `integrity` emptied, plus trailing `\n` | No — `NewLine = "\n"` was pinned in #169 | Already correct; left alone, covered by `PackageInstallRecordTests` |
| Extension lock bytes and hash | `ExtensionLockJson.Write`, `GraphBytes`, `ToBytes`, `Hash` | Indented JSON document, UTF-8 without BOM | **Yes** — `WriteIndented = true` with the default `NewLine` emitted CRLF on Windows, and `GraphBytes` is the trust digest's input | **Fixed**: `NewLine = "\n"` pinned; test asserts no `\r` and byte-identical write → read → write |
| Lock toolset `integrity` | `ExtensionLockJson.Integrity` | SHA-256 of the toolset block's indented text | **Yes** — same serializer | **Fixed** by the same pin |
| Trust inventory digest | `TrustInventory.ComputeDigest` | `JsonArray.ToJsonString()`, unindented, UTF-8 without BOM, content only (no paths, no repo identity) | No — an unindented document emits no newline, and string values escape control characters | Correct, but unpinned; now pinned through `CanonicalJson` and covered by `CanonicalJsonTests` |
| MCP config hash | `McpConfigTrustHash.Compute`, `ComputeDefinition` | SHA-256 of the name-sorted definition JSON, unindented, raw values | No — the document is unindented; the file's formatting is parsed away before hashing | Correct, but unpinned, and it hashed user text verbatim; now pinned and normalized |
| AI fixture request digest | `FixtureFormat.ComputeRequestDigest` | SHA-256 of `ComputeDigestInput`: messages, model id, tool names, unindented | No — unindented, and control characters inside strings are escaped, not emitted | Correct, but unpinned; now pinned |
| Single-file source digest | `SingleFileExtensionDigest` | `sha256(tag \| id \| packages as written \| source bytes [\| graph bytes])` | **Yes** — the source bytes came straight from disk, so a Windows checkout with `autocrlf` hashed differently | **Fixed**: source normalized to LF; CRLF and LF sources now share a digest |
| Extension / skill directory content hash | `ExtensionTrustStore.ComputeContentHash` | Each file's relative path and bytes, ordinal-sorted, NUL-separated | **Yes** — same reason; this is the project-extension and project-skill trust hash | **Fixed**: CRLF normalized to LF |
| Single-file content hash | `ExtensionTrustStore.ComputeFileContentHash` | File name plus the file's bytes | **Yes** | **Fixed**: CRLF normalized to LF |
| Session naming | `SessionPaths.ForProject`, `ForRepository` | SHA-256 of the absolute path or repository identity, first 8 hex | Yes, by design — the identity *is* machine-local | Correct: not a cross-machine digest. A path cannot contain a newline, so no pin is needed |
| Session file bytes | `SessionFiles` + `Session.Append` | One JSON record per line, `+ "\n"` | No — the writer appends `\n` explicitly and encodes UTF-8 without BOM | Already correct; guard test asserts every record ends with LF and a CRLF-written file parses identically |
| AI fixture file bytes | `RecordingChatClient` | One JSON line per record, written with `StreamWriter.WriteLine` | **Yes** — `StreamWriter.NewLine` follows `Environment.NewLine`, so a fixture recorded on Windows was CRLF | **Fixed**: `writer.NewLine = "\n"` |
| Image bytes | `FixtureFormat.DigestContent`, `SessionImages` | SHA-256 of the image bytes | No, and must not be normalized — bytes are the artifact | Correct as is |
| Package bytes (`sha512`) | `PackageContentStager`, `PackageContentVerifier`, `PackageInstallRecord` | SHA-256 / SHA-512 of the nupkg and of each staged file | No — binary | Correct as is |
| Build cache key | `SingleFileBuildCache.Compute` | NUL-separated hex fields, including the source hash | No — hex and version strings; it inherits the source hash's normalization | Correct as is |
| Models.dev catalog fingerprint | `ModelsDevCatalog.Generate` | SHA-256 of the downloaded JSON text | No — the same remote bytes | Correct as is |
| Bash change tracking | `BashChangeTracker` | SHA-256 of a file's bytes, to notice a change during a turn | No — same machine, same bytes | Correct as is |

**Conclusion.** Three digests were broken across platforms: the extension lock (and its toolset
block, which shares the serializer), the single-file source digest, and the extension/skill
content hashes. Two more were correct only because their documents are unindented, and are now
pinned so that a future `WriteIndented` cannot reopen the class. One writer outside the digest
path — the fixture recorder — also followed the platform and is pinned.

## Risks / Trade-offs

- **A line-ending-only change no longer re-prompts.** Accepted and documented: the approved thing
  is the text, and the trust record now means "this text", not "this checkout's line endings".
  A binary that happens to contain `0x0D 0x0A` is normalized too; that can only make two files that
  differ *solely* in that pair hash alike, never two files that differ in anything a compiler reads.
- **Existing trust records for multi-line MCP values or CRLF extension sources** do not match the
  new digest shape. Such a record re-prompts once, which is the safe direction; single-line values
  (every value in practice) hash exactly as before.
- **A new file now follows a policy instead of the content as given.** A model writing CRLF text
  into a project that sets `end_of_line = lf` gets LF. That is the rule; the previous behaviour was
  the corruption the proposal describes.
