# Tasks: ACP server (T-27)

## 1. ADR + package

- [x] 1.1 ADR-0020 finalized (status accepted on sign-off); LibAcp pinned in `Lunate.Protocols` (exact version)

## 2. Server core (Protocols)

- [x] 2.1 `IAcpServer` (public, PublicAPI entry) + `LibAcpServer` adapter: initialize (v1, honest capabilities, log-and-skip unsupported)
- [x] 2.2 session/new (harness per session; cwd → workspace; session id map)
- [x] 2.3 session/prompt: one run, text blocks only, streaming updates via the mapper, stop-reason mapping
- [x] 2.4 session/cancel: token cancel, cancelled stop reason, no post-response updates
- [x] 2.5 `AcpEventMapper` (pure) per the guide's table

## 3. CLI mode

- [x] 3.1 `lunate --acp`: stdio wiring, stdout purity, stderr logs, mutual exclusion with `-p`/TUI (usage error, exit 2)

## 4. Tests

- [x] 4.1 In-process client over a pipe pair: handshake, session/new, ordered streaming + `end_turn`, cancel mid-run, unknown method, non-text block
- [x] 4.2 Mapper goldens (byte-exact payloads)
- [x] 4.3 stdout purity on the built binary (publish-dependent pattern)

## 5. Gate

- [x] 5.1 `bash scripts/verify.sh` green; deviations recorded in design.md
