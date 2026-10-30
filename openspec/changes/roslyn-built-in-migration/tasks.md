# Tasks — Roslyn built-in migration

## 1. Freeze the pre-migration baseline

- [ ] 1.1 Before changing composition, capture the current five `cs_*` names, exact tool outputs and error flags, published startup measurements, idle RSS, and Roslyn RSS after reference-solution load and ten edit-to-diagnostics cycles. Record commit, SDK/runtime, OS/RID, fixture identity and reproducible commands; add deterministic result fixtures under `tests/fixtures/`.
- [ ] 1.2 Verify the baseline fixtures reproduce the recorded tool results and that the existing startup and memory budgets are represented without changing their thresholds.
- [ ] 1.3 Commit: `test(roslyn): capture the pre-migration parity baseline`.

## 2. Load Roslyn as a built-in

- [ ] 2.1 Add the internal, non-packable `Lunate.Roslyn.BuiltIn` adapter with the `roslyn` built-in manifest, typed `IExtensionFactory` and host-supplied descriptor. Test descriptor validation, manifest cross-checks and built-in lifecycle without a collectible load context.
- [ ] 2.2 Supply the descriptor through the existing host seam in interactive, print, ACP and child-run composition, including when `--no-extensions` is set. Test that the built-in is present while third-party discovery remains disabled.
- [ ] 2.3 Add a startup test that asserts no `Microsoft.CodeAnalysis.*` or `Microsoft.Build.*` assembly is loaded before the first C# tool use.
- [ ] 2.4 Keep the single-file build-host arrangement intact while constructing and loading the built-in.
- [ ] 2.5 Commit: `feat(coding): load Roslyn through the built-in extension path`.

## 3. Acquire the Roslyn workspace service through the contract

- [ ] 3.1 Register the `roslyn` service through `IExtensionContext`; acquire a lease for the call's `ToolContext.WorkingDirectory` in each of the five handlers. Keep creation lazy, share the host registry and cap of two, and preserve `roslyn.maxLoadedWorkspaces` as the host override. Test per-root isolation, lease release and cap behavior.
- [ ] 3.2 Bridge file-change events through `SubscribeFileChanged`: route only to already-created live service values, mark changed C# files, and request reload for project/solution/build configuration changes. Test that events never create a service and that disposal/eviction removes its event lookup.
- [ ] 3.3 Test service disposal on eviction and shutdown, pinned-call safety, and child-run acquisition through the same registry and cap.
- [ ] 3.4 Commit: `feat(roslyn): use the extension workspace service contract`.

## 4. Use the manifest activation rule in chat and MCP

- [ ] 4.1 Declare the existing root rule (`*.sln`, `*.slnx` or `*.csproj`) in the built-in manifest. Test matching and non-matching roots with the host `ToolActivationEvaluator`.
- [ ] 4.2 Route chat, print, ACP, child-run and `lunate mcp roslyn` composition through the same evaluator; MCP uses declared client roots or its launch directory when none are declared. Test that a non-matching root offers no C# tools and that changed roots re-evaluate and notify only when supported.
- [ ] 4.3 Keep MCP stdout protocol-only and test the stale-root call response when no roots-change notification was received.
- [ ] 4.4 Commit: `feat(coding): evaluate Roslyn activation from its manifest`.

## 5. Preserve the five `cs_*` aliases

- [ ] 5.1 Declare aliases for exactly `cs_diagnostics`, `cs_find_symbol`, `cs_find_references`, `cs_outline` and `cs_rename`. Test canonical identity versus model-facing alias, global collision handling, and that every alias targets a declared and registered tool.
- [ ] 5.2 Compare all five names, exact `ToolResult.Output` values and `IsError` flags with the frozen fixtures; do not update fixtures to hide a behavior change.
- [ ] 5.3 Commit: `feat(roslyn): preserve the existing cs tool aliases`.

## 6. Remove direct C# tool composition

- [ ] 6.1 Route the existing coding composition through the loaded built-in and its activation filter, then delete `HarnessFactory.AddCSharpTools` and the parallel hard-coded root gate. Test parity of available tools and results against the baseline.
- [ ] 6.2 Commit: `refactor(coding): remove direct Roslyn tool composition`.

## 7. Remove the direct project dependency

- [ ] 7.1 Remove the `Lunate.Coding` reference to `Lunate.Roslyn` and the corresponding `LayeringChecker` allowance. Remove the superseded core `WorkspaceServices` implementation and event subscription after the extension service tests cover their behavior.
- [ ] 7.2 Extend the layering test to enforce dependency closure for both `Lunate.Roslyn` and `Lunate.Roslyn.BuiltIn`: only `Lunate.Extensibility.Abstractions`, Roslyn/MSBuild packages and the BCL are allowed.
- [ ] 7.3 Add a single-edge test asserting `Lunate.Coding` uses `Lunate.Roslyn.BuiltIn` only to supply the host's list of built-in factories and consumes no other adapter type.
- [ ] 7.4 Verify the solution graph and layering tests contain no direct `Lunate.Coding` → `Lunate.Roslyn` dependency; confirm both coding and MCP paths still load the same built-in.
- [ ] 7.5 Commit: `refactor(coding): remove the direct Roslyn project reference`.

## 8. Document, compare and verify the migration

- [ ] 8.1 Update `docs/guide.md`, `docs/architecture.md` and `docs/mcp-roslyn.md` to describe the built-in, activation, workspace service, `/extensions` status and unchanged `--no-extensions` behavior. Document restart/resume and rollback for pre-migration sessions; do not imply a session-format change.
- [ ] 8.2 Restore the `docs/guide.md` positioning sentence “built only on the public extension API” only after dependency closure, the single-edge test, assembly-level lazy-loading test, and unchanged compatibility/golden/e2e tests are green; record that #237 may publish `Lunate.Extensibility.Abstractions` as stable only after this sentence is restored.
- [ ] 8.3 Capture the migrated results using the baseline commands and fixtures; record before/after tool parity, startup and memory measurements, and confirm existing budgets and the two-workspace cap still hold.
- [ ] 8.4 Run the relevant tests, `openspec validate roslyn-built-in-migration --type change --strict`, the Release solution build, formatting checks and `scripts/verify.sh`. Stop and report any golden-session diff; do not rewrite goldens unless an approved proposal changes the session format.
- [ ] 8.5 Commit: `docs(coding): document the Roslyn built-in migration`.
