## Context

`Lunate.TestSupport.FixtureRestore` restores a copied fixture solution once per content-addressed tree
and shares the artifacts with every test that restores the same tree. #259 (`e6e9bfef`, "bound restores
machine-wide and isolate every shared folder") closed the concurrency half of the flake: a machine-wide
gate, one node per restore, no MSBuild server, a private temp directory, an explicit `--packages`
folder, the CLI home and the HTTP cache inside the fixture.

What survives #259 is a fixture that still restores through MSBuild's legacy solution parser, and a
diagnostic path that can still lie. This change closes both, and deliberately does not add a retry or a
longer timeout: the retry #259 already has proves the failure is deterministic on the newer runner
image, so another attempt would only make the same failure slower to read.

## Goals / Non-Goals

**Goals**

- Remove the generated on-disk solution metaproject from the gate's restore path.
- Make a `.sln` fixture impossible to restore by accident, with the reason in the message.
- Never report a stalled pipe as a silent child.
- Make a zero-error restore failure name the MSBuild target it was in.
- Keep the gate's restore cost where it is: these fixtures are package-free, and the cache is untouched.

**Non-Goals**

- Adding a retry, a longer timeout, or a third attempt of any kind.
- Changing what the fixtures contain, or how the Roslyn backend loads a solution.
- Dropping `.sln` support from the product: `SolutionDiscovery` still accepts both extensions, and its
  unit tests still cover legacy `.sln` discovery with synthesized files.

## Decisions

**The fixture becomes `.slnx`, and `Restore` refuses `.sln` by name.** The probe above shows the legacy
format is the only one that writes a generated metaproject into a temp directory; that file is the one
part of the restore the suite does not control, and a metaproject that never became readable is the only
explanation on record for `ValidateSolutionConfiguration` failing with zero errors and no project line.
The `.slnx` neighbour passed on the same runner in the same run. Converting is therefore removing the
suspect path, not retrying around it. The refusal is what keeps the decision: a future fixture that is a
legacy `.sln` fails at its first `Restore` call with the reason and the fix, on every machine, instead of
on a loaded runner in CI.

**Keep `-maxcpucount:1`, the machine gate and the private folders from #259.** They are the other half:
the probe confirms the second MSBuild node appears only when the node count is not pinned, and every
temporary folder the child can write to must stay inside the fixture.

**Report an expired drain instead of an empty stream.** `DotnetCli.Drain` waits five seconds for a
stream to close after the child exits. Returning `string.Empty` on expiry makes `Describe` print "the
child printed nothing", which is a statement about the child the helper cannot make. The marker says the
stream had not closed and that output is missing, which is what a reader needs to know before trusting
the rest of the report. `FixtureRestore`'s silent-retry condition keeps testing for two genuinely empty
streams, so a stalled pipe does not trigger a pointless second attempt.

**Name the last target in a failed child's report.** MSBuild prints `TargetName:` as it enters a target
(prefixed with the node when it fans out). Reading the last such line out of the child's own stdout is
one pass over a string the report already holds, and it turns `Build FAILED. 0 Error(s)` into
`Build FAILED. 0 Error(s)` **plus** the target the child was in — the only fact a zero-error failure
leaves behind. Only failed children get the line: a successful restore enters many targets and naming
one would be noise.

## Risks / Trade-offs

- **The suite no longer loads a legacy `.sln` through MSBuildWorkspace.** Discovery of a legacy `.sln` is
  still pinned by `SolutionDiscoveryTests` on synthesized files; the load itself is Roslyn's behaviour,
  not ours, and the format is exercised by users, not by the gate. If a future Roslyn update regresses it,
  the first report will be a user's workspace, not a red fixture — accepted, because the alternative is a
  gate that fails on a loaded runner for a format none of our tests need.
- **The `.slnx` fixture has no project GUIDs or configuration matrix**, so it cannot catch a regression in
  handling a solution whose configuration name does not match a project's. Nothing in the suite asserted
  that through the restore path; the Roslyn backend reads whatever solution it is given.
- **A `.sln` refusal is a behavioural change in test support.** Any out-of-tree caller restoring a legacy
  fixture now gets an `ArgumentException`; there are none in the repository, and the message names the fix.

## Migration

No product migration. The fixture rename and the twelve call sites move together in one commit; the
restore cache is content-addressed and keyed by solution file name, so the old `.sln` entries are simply
never read again.
