# Tasks

Each group is one conventional commit. Work test-first through `senior-dev`; do not batch groups. Run the focused test project after each red/green cycle and `scripts/verify.sh` before the final group's commit.

## 1. Vision capability seam

- [x] 1.1 Add red tests: `ModelCatalog` reads `supportsVision` (default false, user override wins), and `ModelInfo` exposes it; `ToolContext.Model` is null outside a run and names the active model inside one.
- [x] 1.2 Add `ModelInfo.SupportsVision` as the last optional parameter (`src/Lunate.Ai/ModelInfo.cs`), `ModelCatalog.ModelEntry.SupportsVision` and the built-in `models.json` values; update `ModelRegistry.Compose` and the interactive-session extension-model construction. (Shipped init-only by provider-presets; extension/virtual models keep the default false.)
- [x] 1.3 Add `ModelInfo? Model` to `AgentHarnessOptions` and `ToolContext`; set it in `BuildHarness`/`InvokeToolAsync` and after `ResolveSessionModel`; set the workspace-tool factory through the same options.
- [x] 1.4 Update `PublicAPI.Unshipped.txt` in `Lunate.Ai` and `Lunate.Agent`; run the API analyzer test.
- [x] 1.5 Commit: `feat(ai): model vision capability`.

## 2. Session schema 3 image part

- [x] 2.1 Add red tests: an `ImageContentReference` part round-trips with `$type`, digest, mime, width and height; schema 1 and 2 goldens still round-trip byte-for-byte; an unknown `$type` still fails with file and line; a schema 2 header test is unchanged.
- [x] 2.2 Add `ImageContentReference` (digest, mime, width, height) and `SessionJson` (a copy of `AIJsonUtilities.DefaultOptions` with `AddAIContentType<ImageContentReference>(..., "image")`).
- [x] 2.3 Switch `SessionFormat` parse/write and `Session.Load` message handling to `SessionJson.Options`; bump `SchemaVersion` to 3 and accept 1, 2, 3 with an updated actionable error. (The version bump and 1/2/3 acceptance shipped with provider-presets.)
- [x] 2.4 Add `tests/fixtures/sessions/golden-v3-image.jsonl` and register it in `SessionGoldenTests`; prove every existing golden is untouched.
- [x] 2.5 Update `PublicAPI.Unshipped.txt`; commit: `feat(session): schema 3 image references`.

## 3. Image blob store and sweep

- [x] 3.1 Add red tests: two identical attachments produce one blob; a temp write is atomic; an unreferenced blob is removed on open; a referenced blob survives; a raw-preserved message's digest is honored; `Session.List` touches no blob; compaction leaves the blob; `--no-session` writes nothing. (The in-memory store proves nothing is written; the `--no-session` run wiring is verified in 4.1.)
- [x] 3.2 Add `SessionImages` (path resolution `<sessions-dir>/<id>/images/<sha256>.<ext>`, SHA-256, content-addressed atomic write, dedupe, strict blob-name validation, bounded decode cache) and an in-memory variant.
- [x] 3.3 Add the sweep on successful `Session.Load` (typed and raw-JSON digests, stale temp cleanup) and the missing-blob validation used by the harness; keep `Session.List` on the header path.
- [x] 3.4 Update `PublicAPI.Unshipped.txt`; commit: `feat(session): image blob store`. (No public surface changed: the store and `Session.Images` are internal.)

## 4. Harness images and request hydration

- [ ] 4.1 Add red tests: a user input with an image stores a reference and the request carries `DataContent`; a `read` result appends the tool message then one user image message; resume rehydrates; a missing blob fails before the first request; a no-session run uses memory.
- [ ] 4.2 Add `ImageAttachment`, `ImageLimits` and `UserInput`; add `ToolResult.Images` (init-only) without changing existing construction sites.
- [ ] 4.3 Wire `RunAsync(UserInput)` (the string overload delegates), store attachments before appending, and hydrate `ImageContentReference` to `DataContent` in `BuildRequest`; keep steering and skill runs text-only.
- [ ] 4.4 Teach `CompactionReducer` the `[image <w>x<h>]` rendering and the `max(256, w*h/750)` estimate; prove the summarizer receives no bytes and blobs survive.
- [ ] 4.5 Update `PublicAPI.Unshipped.txt`; commit: `feat(agent): image parts in requests`.

## 5. Image-aware request digest

- [ ] 5.1 Add red tests: identical image bytes in differently encoded messages produce one digest; a one-pixel change changes it; the digest input and fixture contain no base64; every existing fixture digest is unchanged.
- [ ] 5.2 In `FixtureFormat.ComputeRequestDigest`, replace image `DataContent` with `{mediaType, digest}` (SHA-256 hex) before serializing the digest input.
- [ ] 5.3 Commit: `feat(ai): image digest for recorded requests`.

## 6. `read` image path

- [ ] 6.1 Add red tests: PNG/JPEG/GIF/WebP magic bytes are recognized (extension ignored); the placeholder is exact; a no-vision model gets an error naming it; 5 MB/4096 px refusals say downscaling is unavailable; `offset`/`limit` are refused; non-image binary behavior is unchanged.
- [ ] 6.2 Add the hand-written header dimension parser and magic-byte sniffer (PNG IHDR, JPEG SOF, GIF logical screen, WebP VP8/VP8L/VP8X) under `Lunate.Coding`; prove it handles malformed/truncated headers without throwing.
- [ ] 6.3 Wire `ReadTool`: image path before the binary check, `ctx.Model.SupportsVision` gate, limits, `Images` carrier, `[image <w>x<h>]` output, schema description update.
- [ ] 6.4 Commit: `feat(coding): read image files`.

## 7. Image input resolver and clipboard

- [ ] 7.1 Add red tests: `@diagram.png`, `@"path with spaces"`, a lone pasted path, repeated references dedupe, a non-image `@file` stays text, a missing image path refuses, print mode exits 2; four-image and 5 MB/4096 px refusals; vision refusal names the model.
- [ ] 7.2 Add `ImageInputResolver` (token scan after `InputPipeline`, working-directory/absolute resolution, magic-byte validation, placeholder substitution) shared by interactive and print mode.
- [ ] 7.3 Add `IClipboardImageSource` and the platform helper chain (`pngpaste`, `wl-paste`, PowerShell) writing to a temp file and cleaned up; `@clipboard` with a one-time-per-session missing-helper notice.
- [ ] 7.4 Thread `UserInput` through `SubmitPromptAsync`/`RunOnceAsync` and `PrintMode`; keep the user block/transcript on the processed text and leave steering text-only.
- [ ] 7.5 Commit: `feat(coding): image input references`.

## 8. TUI placeholders

- [ ] 8.1 Add red tests: a successful image `read` shows `[image 1024x768]` while a text `read` still hides output; the header reports no line count for the placeholder; the block contains no image escape; the user block and transcript show the same placeholder.
- [ ] 8.2 Update `ToolBlockRenderer.ShowsOutput` and `ToolArgsSummary.ReadRange`; style the placeholder muted; add read-image goldens at 120, 80 and 50 columns.
- [ ] 8.3 Commit: `feat(tui): image placeholders`.

## 9. Verification

- [ ] 9.1 Run `export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"; scripts/verify.sh` and fix every failure without editing a golden to hide drift.
- [ ] 9.2 Run `openspec validate images --strict` and fix every scenario/requirement error.
- [ ] 9.3 Confirm `git status` contains only this change and its source/tests; one final commit if 9.1/9.2 produced follow-ups.
