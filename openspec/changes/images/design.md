# Images — design

Status: draft for review (maintainer-approved direction; recon facts below are new).

## Goal

Let a person show Lunate a screenshot, diagram or UI capture, and let the model see it, without adding a package, a second message type, or a new renderer architecture. The user-facing contract is `@<path>`, a dropped path and `@clipboard`; the internal contract is one new session content part and one new `ToolResult` field.

## Recon findings

### 1. The session store

- `SessionFormat.SchemaVersion` is `2` (`src/Lunate.Agent/SessionFormat.cs:17`); the write path uses one `Utf8JsonWriter` (`src/Lunate.Agent/SessionFormat.Serialize.cs:16-30`) and emits the embedded `ChatMessage` through `AIJsonUtilities.DefaultOptions` (`SessionFormat.Serialize.cs:92-106`).
- The read path parses a line by `type` (`SessionFormat.cs:40-117`), deserializes the message with `AIJsonUtilities.DefaultOptions` and preserves raw JSON when the round-trip is not byte-identical (`SessionFormat.cs:119-178`). An unknown entry `type` fails with an actionable error (`SessionFormat.cs:110-114`).
- `Session.Load` accepts schema `1` or `2` only (`src/Lunate.Agent/Session.cs:127-132`), appends with `File.AppendAllText` (`Session.cs:414-418`), and writes the header with `File.WriteAllText` (`Session.cs:403-412`). There is no per-session resource directory today.
- `ToHistory()` returns messages after the last compaction (`Session.cs:355-391`); the full history stays in `_entries`, so compaction never deletes entries.
- `Session.List` reads only the first line (`src/Lunate.Agent/Session.List.cs:94-111`), so listing already never touches a blob directory. The session file name is `<id>.jsonl` (`src/Lunate.Agent/SessionPaths.cs:34-51`).
- Goldens pin the bytes (`tests/Lunate.Agent.Tests/SessionGoldenTests.cs:11-34`) and schema-2 header tests construct schema 2 explicitly (`tests/Lunate.Agent.Tests/SessionFormatV2Tests.cs:14-52`). A schema-3 golden would be new; no existing golden changes.

### 2. The `read` tool

- `ReadTool` probes the first 8,192 bytes and refuses a NUL byte as binary (`src/Lunate.Coding/ReadTool.cs:106-147`), then formats numbered lines (`ReadTool.cs:149-208`). `ToolResult` today has `Output`, `IsError`, UI-only `Details`, `StructuredContent` and `Usage` (`src/Lunate.Agent/ToolResult.cs:14-27`); there is no model-content carrier.
- `ToolContext` carries the working directory, events, run/call ids, nested execution, progress, mutation queue, file changes, subagents and model access (`src/Lunate.Agent/ToolContext.cs:6-55`) but no active model. `AgentHarnessOptions` has `ModelCatalog` and `ModelId` (`src/Lunate.Agent/AgentHarnessOptions.cs:63-72`), and tools are composed before the model resolves (`src/Lunate.Coding/InteractiveSession.cs:409-440`), so the model must reach the tool through the context, not the constructor.
- The loop truncates the text and appends a tool-role `FunctionResultContent` (`src/Lunate.Agent/AgentHarness.Tools.cs:62-69`). A `DataContent` placed there would be dropped by the OpenAI chat-completions adapter (see finding 5).

### 3. The TUI renderers

- The user block is `UserEchoRenderer.Render(text, width, color)` (`src/Lunate.Tui/UserEchoRenderer.cs:24-58`); the interactive session commits the same processed text into the transcript (`src/Lunate.Coding/InteractiveSession.cs:1015-1035`, `src/Lunate.Tui/Transcript/TranscriptEntry.cs:9-10`). A placeholder embedded in that text renders with no new renderer.
- Tool results become `ToolBlockModel` and are committed by `InteractiveSession.Events.CommitToolBlock` (`src/Lunate.Coding/InteractiveSession.Events.cs:211-237`). A successful `read` block deliberately hides its output (`src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs:121-123`), and `read`'s header range counts output lines (`src/Lunate.Tui/ToolBlocks/ToolArgsSummary.cs:143-169`). An image placeholder therefore needs a read-block exception and must not count as a line.
- The approval panel is the existing per-tool detail seam: `ApprovalDetail.Build` prepares headline/detail lines from the tool name and arguments (`src/Lunate.Coding/ApprovalDetail.cs:22-90`) and the TUI model carries them (`src/Lunate.Tui/Interaction/ApprovalPrompt.cs:23-29`). It is not reused for images; it confirms that per-tool display data belongs in the wiring layer, which is the pattern for the image placeholder.

### 4. The composer

- Every submission reaches `SubmitPromptAsync`, which runs the input hook chain, records the processed text and commits the user block (`src/Lunate.Coding/InteractiveSession.cs:991-1039`), then calls `RunOnceAsync` → `_harness.RunAsync(text)` (`InteractiveSession.cs:1073-1091`). Print mode calls `harness.RunAsync(options.Prompt)` (`src/Lunate.Coding/PrintMode.cs:295`).
- `@` already has a token rule for autocomplete: a token ending at the cursor that starts with `@` (`src/Lunate.Coding/InteractiveSession.Autocomplete.cs:293-327`), and accepted file rows keep the `@` (`InteractiveSession.Autocomplete.cs:249`, `273`). There is no submission-time path mini-language; `@` is only an autocomplete trigger today.
- `InputPipeline` runs extension hooks before a run or queue (`src/Lunate.Coding/InputPipeline.cs:19-43`). Image resolution belongs after it, so a hook's transformed text is what resolves.

### 5. The MEAI image plumbing

- `DataContent` exists in the pinned `Microsoft.Extensions.AI.Abstractions` 10.10.1 (`DataContent(ReadOnlyMemory<byte>, string)`), and `AIJsonUtilities.AddAIContentType<T>(options, "$type")` is the supported way to add a custom content type to the polymorphic `AIContent` map.
- The OpenAI chat-completions adapter (`Microsoft.Extensions.AI.OpenAI` 10.10.1, `OpenAIChatClient.ToOpenAIChatMessages`) maps `DataContent` images in user/system/assistant content via `ToOpenAIChatContent` → `ChatMessageContentPart.CreateImagePart`, but for a tool-role message it only reads `FunctionResultContent.Result` and serializes it to a string. **An image in a tool-role message is therefore not portable to the OpenAI path.**
- The Anthropic adapter (`Anthropic` 12.53.0, `Microsoft.Extensions.AI.AnthropicClientExtensions`, `CreateMessageParams`) maps `DataContent` with an image media type to `ImageBlockParam` in user/assistant messages, and maps `FunctionResultContent.Result` that is an `AIContent` or `IEnumerable<AIContent>` to image blocks in a tool result. It supports both placements, but the portable one is the user message.
- Lunate's wiring points are `ChatClientFactory.CreateProviderClient` (`src/Lunate.Ai/ChatClientFactory.cs:118-135`, OpenAI at 122-125, Anthropic at 127-130) and the prompt-cache copy path, which today copies only text, reasoning and function content (`src/Lunate.Ai/AnthropicPromptCache.cs:173-207`). That copy path must learn about image content, or a last-content-block cache marker silently disappears on an image-ending turn.

### 6. Tests that pin the shape

- Session bytes: `tests/Lunate.Agent.Tests/SessionGoldenTests.cs:11-34`, `SessionFormatV2Tests.cs`, `tests/fixtures/sessions/golden-*.jsonl` (schema 2 header at `golden-v2-header.jsonl:1`). A new `golden-v3-image.jsonl` is additive.
- Tool result rendering: `tests/Lunate.Tui.Tests/ToolBlockRendererTests.cs`, `ToolArgsSummaryTests.cs`, `ToolBlocksFrameGoldenTests.cs` and the `tests/Lunate.Tui.Tests/fixtures/toolblocks/read-ok-*.txt` goldens. A read-image golden is additive.
- Read tool: `tests/Lunate.Coding.Tests/ReadToolTests.cs` pins the text behavior and errors.
- The request digest: `tests/Lunate.Ai.Tests/FixtureFormatTests.cs:188-210` and `RecordingChatClientTests.cs`.

### 7. Listing and headers

`Session.List` reads only the header (`src/Lunate.Agent/Session.List.cs:94-111`). Images must not change `SessionSummary`, the picker order, the 20-row bound or `--continue`; the image directory is a sibling of the JSONL and is never enumerated by `List`.

## Decision

### D1. Input surface

- `@<path>` is resolved after the input hook chain and before the run, in both interactive and print mode. `@"<path with spaces>"` is supported; an unquoted token ends at whitespace.
- A submission whose trimmed text is exactly one existing supported image path (the drag/drop paste shape) is treated as one reference.
- `@clipboard` is the clipboard trigger, not a slash command: it keeps the input pipeline and the command table unchanged and works in print mode. The helper chain is `pngpaste` (macOS), `wl-paste` (Wayland), PowerShell `Get-Clipboard -Format Image` (Windows). Helpers write to a temporary file because the existing process seam decodes text (`src/Lunate.Coding/ProcessRunner.cs:21-60`); the temp file is deleted after the bytes are read.
- Only existing supported images become attachments. A non-image `@file` stays text (the model can `read` it). A token with an image extension that does not exist is a refusal naming the path.
- An absent clipboard helper produces one notice per interactive session, and an error in print mode. No new slash command and no new key binding.

Rejected: a `/image` command (needs a TUI command-table change and a pending-attachment state), and a Ctrl+V probe (terminals deliver bracketed paste as text; a reliable image detection would need a new input path).

### D2. Vision capability

`ModelInfo` gains `bool SupportsVision = false` as the last optional positional parameter (`src/Lunate.Ai/ModelInfo.cs:9-17`), `ModelCatalog.ModelEntry` gains `SupportsVision` (`src/Lunate.Ai/ModelCatalog.cs:158-163`), and `models.json` sets it for the built-ins that have vision (`src/Lunate.Ai/models.json:4-7`). `AgentHarnessOptions` gains `ModelInfo? Model` and `ToolContext` gains `ModelInfo? Model`; Coding sets both after `ResolveSessionModel` (`src/Lunate.Coding/InteractiveSession.cs:437-440`). Extension/virtual models default to false until P2 exposes the capability.

**P2 dependency, recorded honestly.** The brief assumed P2 supplies the flag; there is no P2 change or `supportsVision` field in this worktree (recon: `ModelInfo.cs:9-17`, `ModelCatalog.cs:158-163`, `models.json:4-7`). Without a flag the read path cannot satisfy the approved behavior, so this change adds the minimal catalog field and treats it as the P2 seam. If P2 lands a different shape (for example on `ModelCapabilities`), that change owns the reconciliation.

### D3. Tool result carrier

`ToolResult` gains `IReadOnlyList<ImageAttachment>? Images` (init-only, so existing positional construction is unchanged). `ImageAttachment` is `(string MimeType, int Width, int Height, ReadOnlyMemory<byte> Data)`. `read` sniffs magic bytes before the binary check, requires `ctx.Model.SupportsVision`, parses the header dimensions, enforces the limits, rejects `offset`/`limit`, and returns `Output = "[image <w>x<h>]"` with `Images = [attachment]`.

The loop stores the bytes, appends the tool-role message with the placeholder, then appends **one user-role message** with the `DataContent` and the text `Image from read: [image <w>x<h>].` This is the portable placement: the OpenAI adapter drops tool-role image content, while both adapters accept user-message images. Anthropic's native tool-result image support is intentionally not used in v1 because it would fork the behavior by provider.

Rejected: `FunctionResultContent.Result = IEnumerable<AIContent>` (correct for Anthropic, silently downgraded to a JSON string by the OpenAI adapter) and a custom `DelegatingChatClient` that rewrites tool messages per provider (more moving parts than the loop already owns).

### D4. Session format and storage

- New content type `ImageContentReference(string Digest, string MimeType, int Width, int Height) : AIContent`, JSON `{"$type":"image","digest":"<lowercase sha256>","mime":"image/png","width":1024,"height":768}` (`mime` via `[property: JsonPropertyName("mime")]`).
- `SessionJson.Options` = a copy of `AIJsonUtilities.DefaultOptions` with `AIJsonUtilities.AddAIContentType<ImageContentReference>(options, "image")`. `SessionFormat` uses it for message parse and write; existing content serializes identically.
- Schema 3: `SessionFormat.SchemaVersion = 3`; `Session.Load` accepts 1, 2 and 3; new sessions write 3. Schema 1 and 2 files take the exact existing paths. Unknown `$type` still fails with file and line.
- Blob path: `<sessions-dir>/<session-id>/images/<sha256>.<ext>`, where `<sessions-dir>` is the folder holding the JSONL (for example `~/.lunate/sessions/<repo>/<worktree>/`). The JSONL stays `<session-id>.jsonl`; the folder is a sibling sidecar. This is the literal `<session-dir>/images/` shape with `<session-dir>` being the session's own directory, chosen so the orphan sweep is local and cannot delete another session's image.
- Content-addressed writes: hash bytes, write a temp file in the same directory, rename; an existing digest is a no-op (dedupe within the session). Cross-session duplicate bytes are accepted in v1.
- Sweep: after a successful `Session.Load`, collect digests from typed image parts **and** from raw-preserved message JSON, delete only blob-named files whose digest is unreferenced, and delete temp files older than one day. `Session.List` never sweeps. `Session.Create` starts with an empty directory. A missing referenced blob is detected when the harness is built for a resumed run, before the first request, and fails naming the digest and expected path.
- Compaction keeps everything: `_entries` still holds the old messages and `SessionImages` is never called by compaction.
- No-session runs use an in-memory store; nothing is written.

### D5. Request digest

`FixtureFormat.ComputeRequestDigest` replaces every image `DataContent` with `{ mediaType, digest }` (SHA-256 hex) before serializing the digest input (`src/Lunate.Ai/FixtureFormat.cs:50-77`). The image identity is included deliberately; base64 never enters the digest or a fixture. Existing fixtures contain no images, so their digests are unchanged. Hashing a 5 MB image per model call is bounded and measured by the existing latency budget, not a new one.

### D6. Compaction

`CompactionReducer.RenderContents` renders an image reference as `[image <w>x<h>]` (`src/Lunate.Agent/CompactionReducer.cs:264-294`) and `Utf8Length` counts `max(256, width*height/750)` tokens instead of the bytes. Without this, image-heavy histories would look free and never trigger compaction. The summarization request never carries image bytes. Blobs survive compaction because entries survive.

### D7. TUI

- A successful image `read` shows its placeholder output line; a text `read` keeps hiding output. `ToolBlockRenderer.ShowsOutput` (`src/Lunate.Tui/ToolBlocks/ToolBlockRenderer.cs:121-123`) gets the image exception, and the placeholder is styled muted. The header's read range (`src/Lunate.Tui/ToolBlocks/ToolArgsSummary.cs:143-169`) treats `[image <w>x<h>]` as no line count.
- The user block needs no new renderer: the processed text already contains the placeholder, and both the scrollback and the transcript entry carry it (`src/Lunate.Coding/InteractiveSession.cs:1015-1035`). No inline terminal image escape is emitted anywhere.
- Goldens: a new read-image block at 120, 80 and 50 columns; the existing read-ok goldens stay unchanged.

### D8. Print mode and ACP

Print mode uses the same resolver before `harness.RunAsync` (`src/Lunate.Coding/PrintMode.cs:295`); a refusal prints to stderr and exits 2. ACP is out of scope: editor-provided content blocks are not mapped to `UserInput`, and the `@path` text syntax still works if the editor sends it as text.

### D9. Limits and formats

Magic bytes: PNG `89 50 4E 47 0D 0A 1A 0A`, JPEG `FF D8 FF`, GIF `GIF87a`/`GIF89a`, WebP `RIFF....WEBP`. 5 MB per file, 4096 px per side, four images per message. Refusals name the actual value, the limit, and the supported formats; oversize says `downscaling is not available yet`. Header dimensions are used as stored (EXIF orientation is not applied in v1; noted as a risk). Animated GIF/WebP are sent as-is; providers may reject or sample them.

## C4-style view (component/dynamic, ASCII)

```text
                       +--------------------------------------+
  @path / dropped      |  Lunate.Coding                       |
  path / @clipboard -->|  ImageInputResolver                  |
                       |   - magic-byte sniff + dimensions    |
                       |   - limits + vision gate             |
                       |   - clipboard helper (temp file)     |
                       +------------------+-------------------+
                                          | UserInput (text + ImageAttachment[])
                                          v
+--------------------------+     +--------+---------+     +---------------------+
| Lunate.Coding            |     | Lunate.Agent     |     | Lunate.Ai           |
| read tool                |     | AgentHarness     |     | ChatClientFactory   |
|  - image -> Images       |---->|  - store blobs   |---->|  - OpenAI adapter   |
|  - placeholder output    |     |  - append refs   |     |  - Anthropic adapter|
+--------------------------+     |  - hydrate       |     +----------+----------+
                                 |    DataContent   |                |
                                 +--------+---------+                v
                                          |                  provider request
                                          v
                              +--------------------------+
                              | Session (JSONL, schema 3)|
                              |  message parts = refs    |
                              |  <id>/images/<sha256>.*  |
                              +--------------------------+
```

The tool path and the user path meet at `AgentHarness`: both produce `ImageAttachment`, both go through the same store, and only the request copy contains `DataContent`.

## Migration

- **Schema 2 → 3.** New files write `"schema":3`. `Session.Load` accepts 1, 2, 3; a schema-3 header on a build that only knows 1/2 still fails with the existing actionable schema error. The brief's "a schema-2 reader on a 3 file skips unknown parts" cannot be delivered by an already-shipped schema-2 binary: it rejects the header before parsing a part. This build delivers the compatible direction — schema 1 and 2 files are byte-identical, and the schema-3 reader recognizes the image part. If forward-opening newer files is wanted, it needs a separate compatibility change that relaxes the header check and defines skip semantics.
- **Goldens.** `tests/fixtures/sessions/golden-v3-image.jsonl` is added. Every existing `golden-*.jsonl` stays byte-identical; a schema-2 reader test loads each existing golden unchanged.
- **ADR.** `adr/0018-addendum-images.md` records schema 3, the part shape, the digest rule and the storage layout as an addendum to ADR-0018.

## Risks

1. **Blob GC.** The sweep is per-session, but a future feature that shares blobs across sessions, or a process that opens the same session concurrently, can race it. Mitigations: strict blob-name match, refs from typed and raw JSON, never sweep on list, single-writer assumption (already documented for sessions). A deleted blob is caught before a resumed run.
2. **Base64 in fixtures/replays.** Session messages store refs, never base64; fixtures store responses only; the digest hashes image bytes to SHA-256. The remaining exposure is a test or extension that serializes a hydrated request with `AIJsonUtilities.DefaultOptions`, which would write base64; the digest transform is the only place that sees hydrated content in the core.
3. **Memory and latency.** 5 MB × 4 images per message, plus hydration on every request. The store keeps a bounded decode cache (16 MB) and re-reads on a miss; `Utf8Length` uses the token estimate, not bytes. A 4096×4096 image is still a large provider payload and may be rejected by the provider; the refusal text does not promise provider acceptance.
4. **Provider asymmetry.** OpenAI tool-role images are unsupported in the pinned adapter, hence the follow-up user message. Anthropic supports both; the uniform path is deliberately less native. Live contract tests (gated by `LUNATE_LIVE=1`) are the only proof against a provider change.
5. **P2 gap.** `SupportsVision` is added here because P2 is absent. If P2 adds a second capability surface, two sources of truth must be reconciled; the design keeps the field on `ModelInfo` as the single read path.
6. **Schema-2 readers.** They reject schema 3 at the header. The alternative (relaxing the header check in old binaries) is impossible retroactively; a future forward-compatibility change must define skip semantics for parts and entries.
7. **Clipboard helpers.** `pngpaste` and `wl-paste` may be absent; PowerShell's `System.Drawing` availability varies. The one-time notice and the print-mode error are the fallback; no helper is a hard dependency.
8. **Animation and orientation.** GIF/WebP animation is not sampled, and EXIF orientation is ignored; a rotated photo keeps its stored dimensions. Both are documented v1 limits, not silent behavior.

## Out of scope

Downscaling, SVG, inline terminal images, image input in ACP, images in steering or `/skill` turns, cross-session blob dedupe, and a general attachment UI.
