# Design: UI abstraction across modes (T-40)

## Structure

- `src/Lunate.Coding/IUserInteraction.cs` (new, internal): the seam — `Notify(UserNotice)`, `UpdateStatus(UserStatus)`, `RenderExtension(ExtensionBlock)`. `UserNotice(NoticeLevel Level, string Message)` with `NoticeLevel { Info, Warning, Error }`; `UserStatus(Model, TokensUsed, ContextWindow, WorkingDirectory, GitBranch, IReadOnlyList<TextSegment> Widgets)`; `ExtensionBlock(string ToolName, IReadOnlyList<TextSegment> Segments)`.
- `src/Lunate.Coding/PrintUserInteraction.cs` (new, internal): `Notify` → `lunate: {message}` on stderr; status/renderer → no-op. Used by print mode in both text and JSON variants (stdout stays pure in both).
- `src/Lunate.Coding/AcpUserInteraction.cs` (new, internal): everything → the adapter's log action (stderr); the protocol has no notice frame (T-27 behaviour, now explicit).
- `src/Lunate.Tui/TuiUserInteraction.cs` (new, internal): `Notify` → `LiveArea.SetNotice`; `UpdateStatus` → `LiveArea.SetFooter` (core fields + widget segments); `RenderExtension` → styled lines above the live area through the existing technical-text path.
- `src/Lunate.Extensibility.Abstractions/`: `Ui/TextSegment.cs` (`TextSegment(string Text, TextStyle Style)`, `enum TextStyle { Plain, Dim, Accent, Warning, Error }`), `Ui/IToolResultRenderer.cs` (`IReadOnlyList<TextSegment> Render(ToolResultView view)`; view = tool name, call id, result text, isError), `Ui/IStatusWidget.cs` (`IReadOnlyList<TextSegment> Segments(StatusView view)`); `IExtensionContext` gains `RegisterToolResultRenderer` / `RegisterStatusWidget`; `ExtensionApi.Current` 1.3.0 → 1.4.0.
- `src/Lunate.Extensibility/`: renderer + widget registries (registration-time only, like every other registration), exposed to the Coding host; failures omit + log (hook failure policy precedent: "never crash the run; report through the host log sink").
- Wiring: `InteractiveSession` routes its existing notice/footer call sites through `IUserInteraction`; its event loop feeds `Notify` (retry, compaction, step limit, run failure) and `UpdateStatus` (footer data + widgets); a `ToolCallResult` event queries the renderer registry and calls `RenderExtension` on a match. `PrintMode` routes its diagnostics through `PrintUserInteraction` and gains retry/compaction/step-limit notices on stderr (they exist as events but were dropped). `AcpMode` constructs `AcpUserInteraction` where it logs unsent events.

## Degradation matrix (the contract)

| Interaction | TUI | print / JSON | ACP |
| --- | --- | --- | --- |
| `Notify` | live-area notice | `lunate: …` on stderr | host log (stderr) |
| `UpdateStatus` | footer: model, tokens/context + widgets | no-op | no-op |
| `RenderExtension` | styled block above the live area | no-op | no-op (logged) |
| Widget/renderer failure | contribution omitted + logged | — | — |

Machine modes stay clean by design: stdout carries only the answer (print) or the event stream (JSON); extension UI is presentation, and its data stays available through the hook payloads and events.

## Pins

- **Segments are the contract**: everything crossing the extension boundary is JSON-serializable (the extensibility directive) — extensions return `TextSegment`s, never native renderables; every new DTO round-trips through `System.Text.Json` (conformance-tested, like every hook DTO). The TUI maps `TextStyle` to its existing palette and degrades with the terminal-capability fallbacks (`NO_COLOR`, ASCII).
- **Approvals stay separate**: `IToolApprover` keeps its per-mode chain (TUI prompt, non-interactive policy, ACP permission round trip). Considered: folding approvals into `IUserInteraction` — rejected (churn on three reviewed surfaces, no behavioural gain, and the approval chain's semantics differ: decisions, not output).
- **No input channel in v1**: extensions contribute output only; user input stays with the approval chain and the input line. Seam, not promise.
- **Widget cadence**: `IStatusWidget.Segments` is called on each status update (cheap, in-process, trusted — same trust model as hooks); a throwing widget is omitted for that update and reported once per session, then retried on the next update (no permanent disable).
- **Renderer matching**: a renderer declares the tool name it renders (ordinal match); several renderers for one name = last registration wins, logged. Unmatched tool results render as today.
- **JSON mode**: no new frame types — the event stream is the machine interface; `PrintUserInteraction`'s stderr notices carry the human-readable side. (Reconsider only if a consumer needs host notices in-stream.)
- **Print notices**: retry/compaction/step-limit now appear on stderr in print mode (new behaviour, stdout unaffected) — this is the "clean degradation" of the TUI's ambient notices.

## Tests (the matrix)

- One file per mode: `TuiInteractionTests` (notice → live-area state; status → footer incl. widget segments; renderer → rendered block; failure → omitted), `PrintInteractionTests` (notice → stderr; status/renderer no-op; stdout byte-pure), `JsonInteractionTests` (same + event stream untouched), `AcpInteractionTests` (notices → log; nothing new on stdout; frame purity intact).
- Contract: `TextSegment`/views round-trip through STJ; registration is registration only; duplicate renderer names logged + last wins; API version 1.4.0 compatibility matrix.
- Failure policy: throwing widget/renderer → omitted + logged, UI continues, next update retries.
- Regression: existing TUI/approval/status-footer suites unchanged and green.

## Alternatives considered (devil's advocate)

- **Extensions return native renderables (Spectre objects)**: rejected — breaks the JSON-serializable boundary, couples the contract to a UI library version, and cannot degrade in headless modes.
- **No extension UI (T-50 becomes TUI-only)**: rejected — the guide pins the sample and the degradation story is the point of T-40.
- **Public `IUserInteraction`**: rejected for now — no external implementer exists (modes are ours); internal keeps the public API freeze cheap. Revisit if a host wants to embed Lunate with its own UI.
- **Unify approvals under the seam**: rejected (see pins).

## Deviations

- **`TuiUserInteraction` lives in `src/Lunate.Coding/`, not `src/Lunate.Tui/`.** `Lunate.Tui` references no other Lunate project (architecture rule, layering-tested), so it cannot see the Coding-internal `IUserInteraction` nor the `Lunate.Extensibility.Abstractions` segment types; only Coding can implement the seam. Coding already references Tui and Tui exposes its internals to the `lunate` assembly (`InternalsVisibleTo`), so the TUI implementation of the seam sits in Coding and uses the TUI's internal palette (`SpanStyle`, `StyledLine`) directly. Behaviour matches the pinned `TuiUserInteraction` entry exactly.
- **`IToolResultRenderer` declares `ToolName`.** The structure sketch listed only `Render`; the renderer-matching pin ("a renderer declares the tool name it renders") is realised as a `string ToolName { get; }` interface member.
- **Footer widget styles are text-only in the live area.** `UserStatus.Widgets` reach `LiveArea.SetFooter` as texts: the live-area footer line is plain today (even the core footer's dim styling only exists in the markup `Render` path the live area never calls), so widget style attributes have no styled surface there; append order and drop order follow the spec. The palette mapping applies to extension blocks, the styled surface.
- **Print mode's per-tool diagnostics stay direct stderr writes.** `PrintUserInteraction` carries composition/run errors and the new retry/compaction/step-limit notices (`lunate: …`); the `tool 'x' denied/failed` lines are not notices and keep their unprefixed byte-exact format. `PrintModeTests`' step-limit expectation now accounts for the new notice line (the pinned new behaviour).
- **Test fixture touched outside the design's list.** `tests/TestExtensions/HelloExtension/` registers a renderer and a widget (and logs invocations) to prove registration-through-`IExtensionContext` and registration-is-registration-only end to end. Footer widget append/drop asserts live in the existing `StatusFooterRendererTests` (the status-footer regression suite) rather than being duplicated in `TuiInteractionTests`.

## Seams

- The ACP mode's "user" is remote: `AcpUserInteraction` is the honest degradation (log), not a protocol feature; if ACP grows a notice surface, it slots behind the same seam.
- Widget/renderer output is presentation-only: never fed back to the model, never part of run state.
