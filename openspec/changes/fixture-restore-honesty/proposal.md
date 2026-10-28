## Why

Twelve `Lunate.Roslyn.Tests` restores failed together on the Ubuntu leg of CI 40681105446, every one of
them with `dotnet restore failed, silently, then failed again on a retry`. The retry said what the
first attempt could not: `ValidateSolutionConfiguration` was entered, then `Done Building Project ...
(Restore target(s)) -- FAILED` with `Build FAILED. 0 Warning(s) 0 Error(s)` and not one per-project
restore line. The restore graph produced nothing and blamed nothing.

Two facts narrowed it to the fixture rather than the runner:

- Every failing test restored the **legacy `.sln`** fixture (`lib-with-tests`). The `.slnx` fixture
  (`console-app`) passed in the same run, on the same machine, with the same SDK.
- The same tree passed at 10:56 on runner image `20261012.163.1` and failed at 11:34 on
  `20261025.179.1`, SDK and NuGet unchanged: the image was the only variable.

The legacy parser is the difference that survives inspection. MSBuild restores a `.sln` by generating a
solution metaproject **on disk** under the temp directory and building that file; `.slnx` is expanded in
memory. Probed in `mcr.microsoft.com/dotnet/sdk:10.0` (SDK 10.0.402), with `TMPDIR` pointed at an empty
directory and the same fixture restored once per format:

| restore | nodes in the log | temp directories written |
| --- | --- | --- |
| `LibWithTests.sln` | node 1, plus project `(3:5)` on **node 2** when `-maxcpucount` is not pinned | 2 per-restore directories (the metaproject's) |
| `LibWithTests.sln` with `-maxcpucount:1 -nodeReuse:false` | node 1 only | 2 |
| `LibWithTests.slnx` with `-maxcpucount:1 -nodeReuse:false` | node 1 only | none |

`-maxcpucount:1` — shipped in #259 — removes the second node, so that lead is already closed. It does
not remove the generated metaproject. A metaproject that never became readable produces exactly the CI
output: the configuration target is entered, no project is restored, and MSBuild has no error to log.

That leaves two defects on `main`, both in how the gate *reports* rather than how it retries:

1. `FixtureRestore` still restores the one legacy `.sln` in the suite, so the class stays live.
2. `DotnetCli.Drain` returns `string.Empty` when its five-second wait expires, so a pipe the child (or a
   build server it left) still holds open is reported as `the child printed nothing on stdout or
   stderr` — a false diagnosis of silence, pointing the next reader at a crash that never happened.

## What Changes

- `lib-with-tests` becomes an XML solution, `LibWithTests.slnx`, like its neighbour that kept passing.
  Every Roslyn gate test restores it; none of them needs the legacy parser.
- `FixtureRestore.Restore` refuses a `.sln` argument by name, with the reason and the fix in the message,
  so the class cannot come back through a new fixture without someone reading why.
- `DotnetCli.Drain` reports an expired wait as a stream that had not closed, instead of an empty string
  that `ChildProcessResult.Describe` turns into "the child printed nothing".
- `ChildProcessResult.Describe` names the last MSBuild target a failed child entered, so a
  `Build FAILED. 0 Error(s)` account carries the one fact that says where the restore gave up.
- Tests for all three, and the existing gate tests run against the converted fixture.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `ci-pipeline`: the gate's fixture restores gain a requirement — XML solutions only, one restore at a
  time on one node with every writable folder inside the fixture, and a failure report that never
  invents silence and names the target a zero-error failure happened in.
