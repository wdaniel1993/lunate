Every task is test-first and a review checkpoint: add the named failing test, run it to confirm the
failure, implement the smallest change, and rerun the focused test in Release. A task is complete
only when its focused tests pass.

## 1. Public verifier contract and manifest

- [x] 1.1 In `tests/Lunate.Extensibility.Tests/WriteVerifierContractTests.cs`, add failing tests for
      `writeVerifiers` parsing (`name`, multiple patterns, nested basename matching), invalid or
      duplicate patterns, undeclared registration, and declared-but-unregistered verifiers. Run
      the focused class and confirm failure first; then implement `IWriteVerifier`,
      `WriteVerifierRegistration`, immutable `WriteVerificationRequest`,
      `WriteVerificationResult`/`WriteVerificationStatus`, `IExtensionContext.RegisterWriteVerifier`,
      manifest parsing/cross-checks, `ExtensionApi.Current = 1.13.0`, and `PublicAPI.Unshipped.txt`.
      Rerun `dotnet test tests/Lunate.Extensibility.Tests/Lunate.Extensibility.Tests.csproj -c Release --filter FullyQualifiedName~WriteVerifierContractTests`; all tests must pass.

## 2. Host call between approval and apply

- [x] 2.1 In `tests/Lunate.Coding.Tests/WriteVerifierPipelineTests.cs`, add failing tests proving
      approval completes before invocation, the verifier receives exact normalized baseline/staged
      text, `VerifierCannotReplaceStagedContent`, refusal leaves bytes unchanged, and non-matching
      files keep existing behavior. Confirm failure first; then implement selection/invocation after
      approval between read/normalize and apply/write. Keep `ICSharpBackend` out of core; rerun the
      focused class and require all tests to pass.
- [x] 2.2 Add failing pipeline tests for a verifier exceeding 10 s using an injected deadline/fake
      clock, and Esc during verification. Confirm failure first; implement timeout as
      unverified-with-reason plus write, and distinguish run cancellation as a cancelled run with no
      write. Rerun `dotnet test tests/Lunate.Coding.Tests/Lunate.Coding.Tests.csproj -c Release --filter FullyQualifiedName~WriteVerifierPipelineTests`; all tests must pass.

## 3. C# verifier and error comparison

- [x] 3.1 In `tests/Lunate.Roslyn.Tests/VerifiedWriteVerifierTests.cs`, add fixture projects under
      `tests/fixtures/verified-edits/generated-regex/` and `multi-target/`, plus failing tests
      `EditIntroducingGeneratedRegexCallIsVerifiedNotRefused`, `ChecksOnlyWorkspaceLoadedTfm`, and
      `UnloadedWorkspaceReturnsUnverifiedWithoutLoad`. Confirm failure first; implement the
      in-memory source-generator run, loaded-TFM selection, and unloaded-workspace unverified
      result without triggering a load. Rerun the focused Roslyn class; all tests must pass.
- [x] 3.2 Add failing Roslyn tests `IncreasedDuplicateErrorCountIsRefused`,
      `EqualErrorMultisetPassesDespiteLineMovement`, and `DependentProjectErrorsDoNotRefuse`. Confirm
      failure first; implement baseline/staged counts per `(code, message)`, line-independent
      comparison, staged-file filtering and dependent-project exclusion. Rerun
      `dotnet test tests/Lunate.Roslyn.Tests/Lunate.Roslyn.Tests.csproj -c Release --filter FullyQualifiedName~VerifiedWriteVerifierTests`; only increased compiler-error counts may refuse.

## 4. Verification setting and refusal policy

- [x] 4.1 In `tests/Lunate.Coding.Tests/SettingsStoreVerifyTests.cs` (new file — `SettingsStore` lives in
      `src/Lunate.Coding`, so its tests do too; `tests/Lunate.Agent.Tests/` has no `SettingsStoreTests.cs`), add failing
      tests `VerifyDefaultsToBlock`, `VerifyAcceptsWarnAndOff`, and
      `InvalidVerifySettingNamesAcceptedValues`. Confirm failure first; implement top-level
      `verify` in `~/.lunate/settings.json` (no environment override), then rerun
      `dotnet test tests/Lunate.Coding.Tests/Lunate.Coding.Tests.csproj -c Release --filter FullyQualifiedName~SettingsStoreVerifyTests`; all tests must pass.
- [x] 4.2 In `tests/Lunate.Coding.Tests/WriteVerifierPipelineTests.cs`, add failing tests
      `WarnWritesUnverifiedWithDiagnostics`, `OffSkipsVerifier`, and
      `ThirdAttemptWritesUnverifiedAfterTwoRefusals`. Confirm failure first; implement block/warn/off
      outcomes and per-canonical-file, per-run refusal counting. Assert refusal text lists errors
      without advertising the bypass and the result carries diagnostics in `StructuredContent`;
      rerun the focused coding tests and require all to pass.

## 5. TUI progress and Esc

- [x] 5.1 In `tests/Lunate.Tui.Tests/LiveAreaTests.cs`, add failing `VerificationShowsElapsedLine`
      and narrow-width tests using `TestScheduler`. Confirm failure first; implement the live
      `verifying… <elapsed>` activity line and rerun `dotnet test tests/Lunate.Tui.Tests/Lunate.Tui.Tests.csproj -c Release --filter FullyQualifiedName~LiveAreaTests`; all tests must pass.
- [ ] 5.2 Add a failing TUI-to-host cancellation test proving Esc uses normal run cancellation and
      produces no write while verification is active. Confirm failure first; pass the existing run
      cancellation token through the verifier call and rerun the focused TUI and coding tests.

## 6. Calibrate and pin the performance ceiling

- [ ] 6.1 Measure the warm changed-file verifier on the documented fixture/workload; apply ADR-0005
      (median plus about 50% headroom, rounded up) with a maximum ceiling of 10 s. Add the measurement to the
      existing performance suite as `WriteVerificationBudget`, so it fails if the calibrated ceiling
      is exceeded. Run that perf case and record the measurement, environment and resulting budget.

## 7. Documentation and final verification

- [ ] 7.1 After the behavior and tests are green, update `docs/guide.md`, `docs/architecture.md`,
      and the extension manifest/authoring pages to describe only shipped verifier, settings,
      timeout, cancellation and TUI behavior. Run `python3 scripts/docs-sync --check` and
      `npx --yes markdownlint-cli2@0.23.3`; both must pass.
- [ ] 7.2 Run `openspec validate verified-edits-implementation --type change --strict`,
      `dotnet build Lunate.slnx -c Release`, the touched test projects in Release, and
      `dotnet csharpier check .`. Do not run `scripts/verify.sh` in this unattended run.
