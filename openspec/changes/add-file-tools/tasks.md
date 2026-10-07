# Tasks — add-file-tools (T-12)

## 1. Workspace (TDD)

- [x] 1.1 `Workspace` + `ResolvedPath` + `TryResolve` per design.md (canonicalization walk incl. symlinks, per-platform case comparison, extra roots, instructing errors; roots canonicalized too)
- [x] 1.2 Boundary tests: inside (relative + absolute); `..` escape refused; symlink inside→outside refused and inside→inside allowed (`Assert.Skip` when link creation is unavailable); case probe both ways; extra root; display form (forward slashes, relative)

## 2. `read` tool (TDD)

- [ ] 2.1 `ReadTool` + hand-written schema; internal `TextFile` helpers (binary check, UTF-8/BOM handling, line splitting)
- [ ] 2.2 Format tests: exact `{n,6}|{text}` lines, footer `[lines {first}–{last} of {total}, use offset to continue]`, paging, `limit` clamp at 2000, empty file `[empty file]`, CRLF/BOM display normalization
- [ ] 2.3 Error tests: outside workspace, missing, directory (suggests `bash ls`), binary (size named), `offset`/`limit` < 1, `offset` past end (total named)

## 3. `write` tool (TDD)

- [ ] 3.1 `WriteTool` + schema + `WriteDetails`; internal `LineDiff` (unified diff, 3 context lines)
- [ ] 3.2 Result tests: `(created)`/`(replaced)`, line counts (empty, with/without trailing newline), parent creation, relative display path
- [ ] 3.3 Exactness tests: CRLF preserved, no trailing newline added, non-ASCII, no BOM; replace keeps the file's permissions
- [ ] 3.4 Error tests: outside workspace, directory; `Details` diff correct for create and replace

## 4. Specs, ADR, close

- [ ] 4.1 Copy adr.md content to `adr/0016-workspace-boundary.md` at the repository root (status proposed)
- [ ] 4.2 `dotnet csharpier format .`, `bash scripts/verify.sh` green (incl. de-AT), `openspec validate add-file-tools --type change --strict`, self-review
