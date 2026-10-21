## Why

Lunate can only read text. Screenshots, diagrams and UI captures are the normal way people explain a coding task, and every comparable agent accepts them. Today `read` refuses an image as a binary file and a pasted image path is just text, so the user has to describe what they can see.

The approved design keeps the feature inside the existing primitives: the `read` tool gains an image path, the user input gains `@<path>` references, and the session file gains a content-addressed image part. No new NuGet package, no downscaling and no inline terminal rendering in v1.

## What Changes

- **Image input**: `@<path>` (quoted for spaces) and a pasted lone image path resolve to an attachment; `@clipboard` reads the clipboard through a platform helper. PNG, JPEG, GIF and WebP are sniffed by magic bytes, never by extension.
- **Vision gate**: an image is sent only when the active model's catalog entry declares `supportsVision`; otherwise the input or the `read` call is refused with the model id named.
- **`read` tool**: a supported image returns `[image <w>x<h>]` plus the image content; limits are 5 MB, 4096 px per side and 4 images per message. Oversize is refused with "downscaling is not available yet".
- **Session schema 3**: message parts gain `{"$type":"image","digest","mime","width","height"}`; blobs live beside the session under `images/<sha256>.<ext>`, deduplicated and swept on open. Schema 1 and 2 files load byte-identically; the new files write schema 3.
- **Provider boundary**: images travel as MEAI `DataContent` in the request. A tool result's image is followed by a user-role message carrying the image, because the OpenAI chat-completions adapter does not map image content in a tool-role message.
- **Recording**: the request digest includes the image's SHA-256 (not its base64), so fixtures stay small and a changed image still changes the digest.
- **TUI**: the `[image <w>x<h>]` placeholder is visible in the user block and in a successful `read` block; terminal-inline images stay out of scope.

## Capabilities

### New Capabilities

- `images`: image references in submitted text (`@path`, pasted path, `@clipboard`), magic-byte formats, limits and refusals, vision gating, and the placeholder contract shared by the TUI and print mode.

### Modified Capabilities

- `agent-sessions`: schema 3 image parts, the per-session blob store, the orphan sweep, compaction retention and the unchanged listing/header path.
- `agent-tools`: `ToolResult` can carry image attachments next to its model-facing text.
- `agent-files`: the `read` tool reads supported images and refuses them clearly when the model has no vision.
- `agent-loop`: image parts are hydrated into requests, a read image becomes a follow-up user turn, and compaction counts and renders images without sending their bytes.
- `ai-layer`: the model catalog gains `supportsVision`; the request digest replaces image bytes with their SHA-256.
- `tui`: image placeholders render in the user block and in successful `read` blocks; no inline terminal images.

## Impact

- **Code**: `Lunate.Agent` (session format/store, tool result, harness request build, compaction), `Lunate.Ai` (catalog field, digest), `Lunate.Coding` (`read` tool, input resolver, clipboard helper, print/interactive wiring), `Lunate.Tui` (read block and placeholder rendering).
- **Public API**: `ModelInfo.SupportsVision`, `ToolResult.Images`, `ToolContext.Model`, `AgentHarnessOptions.Model`, `UserInput`/`ImageAttachment`, and the `ImageContentReference` content type; all appear in `PublicAPI.Unshipped.txt`.
- **Files**: new `<sessionId>/images/` sidecar directory; schema-3 golden session and a binary fixture.
- **Providers**: no new package; the Anthropic adapter already maps `DataContent` images (tool results and user messages) and the OpenAI adapter maps user-message images. Live contract coverage stays opt-in.
- **Out of scope**: downscaling, SVG, animated-image frame handling, inline terminal images, images in ACP, and images in steering or skill turns.
