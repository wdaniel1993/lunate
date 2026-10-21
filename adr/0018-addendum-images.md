# ADR-0018 addendum: Session schema 3 and image references

- Status: proposed — 2026-10-20
- Context: `images` (adds image input, the `read` image path and the schema-3 image part)
- Amends: ADR-0018 (session schema 2)

## Context

Schema 2 is frozen by golden tests, and the session format has no way to name binary content. Images are the first content kind that must survive a session without embedding a base64 copy: a 5 MB screenshot would make the append-only JSONL large, slow to parse and expensive to rewrite, and would put base64 into every request digest and every fixture that touches the message.

The providers disagree about placement. The Anthropic adapter maps `DataContent` images in user messages and in tool results; the OpenAI chat-completions adapter maps them only in user-role content and stringifies a tool result. A session format that records only a provider-neutral reference keeps both paths possible.

## Decision

- **Schema 3.** `SessionFormat.SchemaVersion` is 3; new files write `"schema":3`. The loader accepts 1, 2 and 3. Schema 1 and 2 files take their existing paths and stay byte-identical. A schema-2 binary rejects a schema-3 file at the header with the existing actionable error; it cannot skip a part it never parses. The schema-3 reader recognizes the image part and still fails on an unrecognized `$type`.
- **Image part.** Messages may contain `ImageContentReference`, serialized as `{"$type":"image","digest":"<lowercase sha256>","mime":"image/png|image/jpeg|image/gif|image/webp","width":<int>,"height":<int>}`. The type is registered with `AIJsonUtilities.AddAIContentType<ImageContentReference>(options, "image")` on a copy of the default options; the session write and parse paths use that copy. The bytes are never part of the message.
- **Storage.** Blobs live in the session's own sidecar directory: `<sessions-dir>/<session-id>/images/<sha256>.<ext>`, with `ext` derived from the sniffed media type. Writes are content-addressed, deduplicated within the session, and atomic (temp file in the same directory, then rename). Only `^[0-9a-f]{64}\.(png|jpg|gif|webp)$` names are blobs.
- **Sweep.** A successful `Session.Load` sweeps only that session's image directory: it collects digests from typed image parts and raw-preserved message JSON and deletes blob-named files whose digest is unreferenced, plus temporary files older than one day. `Session.List` never reads or sweeps the directory. Compaction never deletes blobs. A referenced blob that is missing fails a resumed run before the first request, naming the digest and path.
- **Request digest.** `FixtureFormat.ComputeRequestDigest` replaces each image `DataContent` with `{mediaType, digest}` (SHA-256 hex) before hashing. The image identity is deliberately included; base64 is deliberately excluded. Existing fixtures carry no images and keep their digests.
- **Goldens.** `golden-v3-image.jsonl` is added; every schema 1 and schema 2 golden keeps its exact bytes.

## Consequences

- The session file stays small and text-only, and an image can be resumed without a provider-specific encoding.
- The blob directory is a new part of a session's durable state. Copying a session file without its sidecar directory yields a loadable file whose resumed run fails with a clear missing-blob error; copying the directory keeps it whole.
- The sweep is per-session and cannot delete another session's blobs; cross-session duplicate bytes are accepted in v1.
- The image part is a custom `AIContent` subtype. Every reader of a session message must use the session JSON options, not the bare `AIJsonUtilities.DefaultOptions`; a future message reader that forgets this fails loudly on the unknown `$type`.
- A schema-2 binary cannot open a schema-3 session. Forward-opening newer files needs a separate compatibility change with explicit skip semantics; this ADR does not claim it.
- The OpenAI adapter's tool-role limitation is a provider fact, not a session fact; the loop's follow-up user message is the portable carrier and can be revisited if the adapter gains tool-result images.

## Alternatives considered

- **Embed `DataContent` (base64) in the message.** No new content type, but large JSONL files, base64 in digests and fixtures, and a full rewrite or dedupe layer later. Rejected.
- **Store a file path instead of a digest.** Breaks when the user moves or edits the file, leaks workspace paths into the session, and defeats content addressing. Rejected.
- **Shared `<sessions-dir>/images/` store.** Dedupes across sessions, but the orphan sweep would have to parse every session in the directory on every open, or risk deleting another session's image. Rejected for v1.
- **Provider-specific tool-result images.** Native for Anthropic, silently wrong for OpenAI. Rejected in favor of one portable placement.
