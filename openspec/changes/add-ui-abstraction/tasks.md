# Tasks: UI abstraction across modes (T-40)

## 1. Contract (Extensibility.Abstractions)

- [x] 1.1 `TextSegment` + `TextStyle`, `ToolResultView`, `StatusView` (STJ round-trip tests)
- [x] 1.2 `IToolResultRenderer`, `IStatusWidget`; `IExtensionContext.RegisterToolResultRenderer`/`RegisterStatusWidget`; API 1.4.0

## 2. Registries + host plumbing (Extensibility)

- [x] 2.1 Renderer + widget registries (registration-time only; duplicate/last-wins semantics; failure policy omit+log)
- [x] 2.2 Host access surface for the Coding UI host

## 3. The seam + per-mode implementations (Coding / Tui)

- [x] 3.1 `IUserInteraction` + records (internal)
- [x] 3.2 `TuiUserInteraction` (live area, footer + widgets, styled blocks)
- [x] 3.3 `PrintUserInteraction` (stderr; no-ops) and `AcpUserInteraction` (log)
- [x] 3.4 `InteractiveSession` routes notices/footer through the seam; renderer blocks on `ToolCallResult`; `PrintMode` notices; `AcpMode` log impl

## 4. Tests (the interaction matrix)

- [x] 4.1 One test file per mode (TUI, print, JSON, ACP)
- [x] 4.2 Contract round-trip + registration semantics + API version
- [x] 4.3 Failure degradation (omitted + logged, next update retries)

## 5. Gate

- [ ] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md
