## 1. The fixture

- [x] 1.1 Replace `tests/fixtures/solutions/lib-with-tests/LibWithTests.sln` with `LibWithTests.slnx`
      listing the same two projects.
- [x] 1.2 Update every `Fixtures.Restore` call site in `Lunate.Roslyn.Tests` to the `.slnx` name; the
      Roslyn gate tests (references, rename, find-symbol, backend load) are the proof the converted
      fixture still loads both projects across a `ProjectReference`.
- [x] 1.3 `FixtureRestore.Restore` refuses a non-`.slnx` solution by name, with the generated-metaproject
      reason and the fix in the message, before it writes anything into the fixture.

## 2. The diagnostics

- [x] 2.1 `DotnetCli.Drain`: an expired wait returns a marker saying the stream had not closed, not an
      empty string; the silent-retry condition in `FixtureRestore` keeps meaning "two genuinely empty
      streams".
- [x] 2.2 `ChildProcessResult.Describe`: a failed child names the last MSBuild target it entered, read
      from its own `TargetName:` lines; a successful child names none.

## 3. The tests

- [x] 3.1 `FixtureRestoreTests.A_legacy_solution_is_refused_by_name`: a `.sln` argument throws with
      `.slnx` and the metaproject reason in the message, and the fixture is left untouched.
- [x] 3.2 `FixtureRestoreEnvironmentTests.A_stream_that_does_not_close_is_not_reported_as_silence`: a
      task that never completes drains to a marker, and the message built from it never says the child
      printed nothing.
- [x] 3.3 `FixtureRestoreEnvironmentTests.A_failure_that_reported_no_error_names_the_target_it_was_in`:
      the CI output verbatim reports `last target: ValidateSolutionConfiguration` next to `0 Error(s)`.
- [x] 3.4 `FixtureRestoreEnvironmentTests.A_successful_child_names_no_failing_target`.
- [x] 3.5 The existing Roslyn suite (208 tests before this change) stays green against the `.slnx`
      fixture, including the machine-gate test in `RestoreGateTests`.

## 4. Verification

- [x] 4.1 `dotnet csharpier check .` on the changed files.
- [x] 4.2 `openspec validate fixture-restore-honesty --strict`.
- [x] 4.3 `Lunate.Roslyn.Tests` and `Lunate.Coding.Tests` locally, and `Lunate.Roslyn.Tests` in
      `mcr.microsoft.com/dotnet/sdk:10.0` as the Ubuntu proxy.
