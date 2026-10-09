# Tasks

## 1. ShellResolver (TDD, red-first)

- [x] 1.1 Resolution per the guide table (Unix + Windows orders, WSL exclusion, override seam, display names, caching); injectable probes; no-shell → null
- [x] 1.2 `ShellResolverTests`: every branch on every OS via probes

## 2. BashTool (TDD, red-first)

- [x] 2.1 Tool per design (schema, description naming the shell, risk Execute, cwd = worktree root, stdin closed)
- [x] 2.2 Execution + capture (separate bounded streams, cap marker), result text + exit semantics, timeout tree kill, cancel tree kill, spawn-failure path
- [x] 2.3 `BashToolTests`: all design cases incl. grandchild-pid proof for timeout/cancel kill, UTF-8, cap
- [x] 2.4 CI gate: tests green on all three runners (observed in the PR run)

## 3. Close

- [x] 3.1 `dotnet csharpier format .`; `bash scripts/verify.sh` green (incl. de-AT); `openspec validate add-bash-tool --type change --strict`; self-review; commit per group
