# Tasks — optional search tools

## 1. Settings and resolution

- [x] 1.1 Add red tests: `tools.extra` defaults to empty; a valid list resolves; an unknown name errors naming the accepted values; a core-tool name is rejected; `LUNATE_EXTRA_TOOLS` overrides the file; `--tools` overrides the env.
- [x] 1.2 Add the `tools.extra` setting (schema 2, additive, default empty), the env var and the `--tools` flag through the existing precedence chain.
- [x] 1.3 Commit: `feat(coding): resolve the optional search tools from settings, environment and flags`.

## 2. The tools

- [x] 2.1 Add red tests: `grep` happy path, cap + `[N more matches]`, invalid regex teaching error, path outside the workspace refused, `.gitignore` honoured, binary skipped; the fallback engine produces the same shape as ripgrep.
- [x] 2.2 Add red tests: `find` glob matching, `type: "dir"`, cap, case rules per platform; `ls` depth 1 and 3, directories-first ordering, entry cap and truncation line, `.git` always skipped.
- [x] 2.3 Implement `grep` (ripgrep when present, internal walker fallback), `find` and `ls` as read-only tools sharing the workspace's path rules.
- [x] 2.4 Commit: `feat(coding): add optional grep, find and ls tools`.

## 3. Registration and wiring

- [x] 3.1 Add red tests: nothing registered by default; each name registers exactly its tool; registration is additive and cannot shadow a core tool.
- [x] 3.2 Register conditionally in `HarnessFactory`; thread the resolved list from the CLI/session.
- [x] 3.3 Commit: `feat(coding): register the optional search tools when enabled`.

## 4. Verification

- [x] 4.1 Build, csharpier, the Coding and Agent suites, `openspec validate optional-search-tools --strict`.
- [x] 4.2 Update `docs/guide.md` (tool list + settings) and `README.md` if it enumerates tools.
