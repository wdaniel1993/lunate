# Tasks: ACP permissions, editor file system and resource links (T-28)

## 1. Seams (Coding)

- [x] 1.1 `ITextFileAccess` + `LocalTextFileAccess`; Read/Write/Edit tools take it (optional ctor param, default local; PublicAPI entries)
- [x] 1.2 `AcpApprover` (policy-first composition over `NonInteractiveApprover.IsAllowed`) + composition tests

## 2. Client-backed implementations (Protocols)

- [x] 2.1 `AcpSessionContext` + factory widening in `IAcpServer`/`LibAcpServer` (adapter builds the context per session)
- [x] 2.2 `ClientApprover` (request_permission; outcomes; always-memory; cancel/error → decline)
- [x] 2.3 `ClientTextFileAccess` (fs capability-gated; read/write through the client)
- [x] 2.4 `ResourceLinkContent` mapping in `PromptText` (`@relpath` / `name (uri)`)

## 3. Wiring (Coding)

- [x] 3.1 `AcpMode`: widened factory; approver + file access into harness options and tools; initialize capability read

## 4. Tests

- [x] 4.1 Permission round trips (allow once/always, reject once/always, AutoEdit skip, Execute prompts, cancel-during-request)
- [x] 4.2 fs routing (buffer read, client write, no-capability fallback)
- [x] 4.3 Resource-link mapping (inside/outside workspace, order)
- [x] 4.4 Composition + tool-seam defaults

## 5. Gate

- [x] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md
