## 1. Toolchain files

- [x] 1.1 Add `global.json` (SDK 10.0.103, rollForward latestFeature) and `.editorconfig`
- [x] 1.2 Add `Directory.Build.props` (net10.0, nullable, warnings-as-errors, deterministic, code-style enforcement)
- [x] 1.3 Create `lunate.sln` with six src projects + six test projects (empty, building)

## 2. Project wiring

- [x] 2.1 Wire project references to the downward-only graph (Ai ← Agent ← Protocols/Coding; Tui standalone)
- [x] 2.2 Add PublicApiAnalyzers + empty PublicAPI.Shipped/Unshipped baselines to the four libraries
- [x] 2.3 Add one placeholder xUnit v3 test per test project; prove `dotnet test` runs

## 3. Entry point and guards

- [x] 3.1 Implement `lunate --version` in Lunate.Coding (assembly version, exit 0)
- [x] 3.2 Add architecture test: checker + synthetic-violation unit test + real-graph assertion

## 4. Verification scripts

- [ ] 4.1 Add `scripts/verify.sh` (build → tests → publish → perf → format → API diff), platform-aware RID default
- [ ] 4.2 Add `scripts/perf.sh` (hyperfine, `BUDGET_MS`, jq)
- [ ] 4.3 Add `scripts/verify.ps1` mirroring verify.sh

## 5. Prove and close

- [ ] 5.1 Run `scripts/verify.sh` green on the skeleton; record measured startup median
- [ ] 5.2 Run `openspec validate add-repo-skeleton --type change --strict`
- [ ] 5.3 Commit the implementation (conventional commits per group); update the change's artifacts if reality diverged
