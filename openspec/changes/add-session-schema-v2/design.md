# Design: add-session-schema-v2

## Context

Schema 1 (`add-session-store`, T-11) is frozen by golden tests: header line + `message`, `compaction`, `modelChange` entries, LF always, UTC `O` timestamps, envelope field order `type, id, parentId, timestamp`, compact JSON. This change extends it to schema 2 without breaking v1 files. Sources of truth: `docs/spec/extensibility.md` (sessions section), the worktree addendum's landing map (`add-worktree-support`), and ADR-0017.

## Decisions

### Schema bump and migration

- `SessionFormat.SchemaVersion = 2`. New files write `"schema":2`.
- The loader accepts schemas 1 and 2. A schema 1 file loads with: no `repo`/`worktree`, no new entry kinds — i.e. v1 semantics are a subset of v2. A schema greater than 2 fails with the existing actionable error naming schema and file ("created by a newer Lunate").
- Migration is read-side only: no rewrite on load; the next appended entry makes the file mixed v1-header/v2-lines, which is legal — the header records the version the file was created with, not the version of its newest line.

### Header

- `SessionHeaderEntry` gains `string? Repo` and `string? Worktree`. Line shape (fields appended after `meai`): `{"type":"header","schema":2,"id":"s_…","cwd":"…","created":"…","meai":"…","repo":"…","worktree":"…"}` with `repo`/`worktree` omitted when null (v2 non-repository sessions omit both; v1 headers parse with nulls).
- `Repo` carries the repository identity string (normalized `GitCommonDir`, or remote URL when available — chosen by the caller, not by the session layer); `Worktree` carries the worktree root path. The session layer never runs git itself; it records what the caller passes.
- `Session.Create(string path, string cwd, string? repo = null, string? worktree = null)`.

### Session locations (grouping)

- New: `SessionPaths.ForRepository(string repoIdentity, string worktreePath)` → `~/.lunate/sessions/<hash8(repoIdentity)>/<hash8(worktreePath)>/`. Both hashes are SHA-256 of the full path/identity string, lowercase hex, first 8 characters (same recipe as today).
- `SessionPaths.ForProject(cwd)` stays unchanged as the non-repository fallback: `~/.lunate/sessions/<hash8(cwd)>/`.
- `SessionFileName` unchanged.

### New core entries

Envelope identical to existing entries (`type, id, parentId, timestamp`; `parentId` always written, null allowed). Payload fields follow in fixed order:

| Type | Record | Payload fields (fixed order) |
| --- | --- | --- |
| `activeTools` | `SessionActiveToolsEntry` | `tools` — array of tool names, in exposure order |
| `promptSection` | `SessionPromptSectionEntry` | `section` — section id; `text` — section text |
| `childSession` | `SessionChildSessionEntry` | `childSessionId`; `runId` |
| `nestedCalls` | `SessionNestedCallsEntry` | `callId` — the parent tool call id; `calls` — bounded array |

- `SessionNestedCall(string Name, string Args, string Status, int DurationMs)`, serialized as `{"name":…,"args":…,"status":…,"durationMs":…}`.
- Bounds (enforced at append, documented here as the contract): `Args` truncated at 200 characters with a trailing `…` marker; at most 32 calls per entry (extra calls dropped, no marker entry); `Status` in `ok | error | cancelled`; durations are integer milliseconds.
- `nestedCalls` never records results — names, capped arguments, status, duration only.

### Extension entries

- `SessionExtensionEntry(string Id, string? ParentId, DateTimeOffset Timestamp, string ExtensionType, string PayloadJson)`.
- `ExtensionType` is the full namespaced type `ext/<extension-id>/<type>`; the line's `type` field is that string. Validation: must start `ext/` and have at least three `/`-separated segments; otherwise `InvalidDataException`.
- `PayloadJson` is the payload as **raw JSON text** — the source of truth. On load it is taken from the line with `GetRawText()` (exact bytes, including any interior whitespace/ordering the writer chose). On serialize it is embedded with `WriteRawValue` — the payload is byte-for-byte preserved across load and rewrite. A convenience `JsonNode Payload` property parses on access; writing from a parsed node is the caller's explicit choice, not the default path.
- `Session.AppendExtension(string extensionType, string payloadJson)` appends one; consumers own the payload shape.

### Unknown content preservation (messages)

- `SessionMessageEntry` gains `public string? RawMessageJson { get; init; }`.
- On load: parse the message node to `ChatMessage` (as today). Then re-serialize that `ChatMessage` and compare with the original node via `JsonNode.DeepEquals`. If equal → `RawMessageJson = null` (normal path). If not equal (content kinds the core cannot round-trip, e.g. provider-hosted tool results) → keep the original node's raw text in `RawMessageJson`.
- On serialize: `RawMessageJson != null` → embed raw (byte-for-byte); else serialize the live `ChatMessage` (today's path). New appends always serialize from the live message.

### Line writing

- `SessionFormat.Serialize` switches from object-templating to a `Utf8JsonWriter` path so raw-preserved parts can be embedded with `WriteRawValue` while every other field is written with the same deterministic shapes as schema 1 (compact JSON, LF, UTC `O` stamps, `CultureInfo.InvariantCulture`). Byte-for-byte golden assertions cover both paths.

## Harness wiring (nestedCalls only)

- `RunNestedToolAsync` (from `add-extension-formats`) already knows each nested call's name, arguments, outcome and timing; it accumulates a bounded `SessionNestedCall` list per top-level call. `RunToolAsync` appends one `nestedCalls` entry per top-level call that had nested calls (never for clean calls), via the session when one is attached.
- The other new entries are format + API only in this change; their producers are later cards: `activeTools` and `promptSection` with prompt assembly / T-36 trust; `childSession` with the subagent cards.

## Testing strategy

- Red-first per group; every new shape gets a golden line; every golden line asserts parse → serialize → byte-identical.
- v1 fixtures keep their exact bytes and are re-asserted as load-compatibility (schema 1 header, no new fields).
- Caps: 200-char truncation (boundary at 200/201), 32-call cap (33rd dropped), status set validation.
- Ext payload preservation: golden with interior whitespace, unicode and unsorted keys; load → rewrite → byte-identical.
- Unknown-content: a message with an unknown `$type` content element round-trips; a known message round-trips with `RawMessageJson = null`.
- Grouping: same repo, two worktrees → same repo folder, distinct worktree folders; no repo → fallback unchanged.
- Both cultures (the suite already runs under de-AT).

## Risks

- `Utf8JsonWriter` rewrite of `Serialize` must reproduce schema 1 lines byte-identically — guarded by the existing goldens (they run unchanged).
- `DeepEquals` comparison cost on load: one extra serialization per message; bounded by session size and covered by the startup/perf budget.
