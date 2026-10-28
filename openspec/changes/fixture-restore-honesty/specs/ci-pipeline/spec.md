## ADDED Requirements

### Requirement: Fixture restores in the gate are bounded and honest

The test suite's fixture restores SHALL restore XML solutions (`.slnx`) only and SHALL refuse a
legacy `.sln` by name, because MSBuild's legacy parser restores a solution by generating a metaproject
on disk under the temp directory while an XML solution is expanded in memory — the gate SHALL NOT depend
on a generated file it does not control. A fixture restore SHALL run at most one at a time across every
test process on the machine, on one MSBuild node, with every folder the child can write to inside the
fixture it restores. A restore failure SHALL NOT be reported as silence that did not happen: a child
stream that has not closed within the drain budget SHALL be reported as a stream that had not closed,
and a failed child whose output carries no error SHALL be reported with the last MSBuild target it
entered, so a zero-error solution failure still says where the restore gave up.

#### Scenario: A fixture solution is an XML solution

- **GIVEN** the fixtures under `tests/fixtures/solutions/`
- **WHEN** the gate restores one
- **THEN** the solution file is `.slnx`, and a `.sln` passed to the restore helper is refused with a
  message naming the XML replacement and the generated metaproject it avoids

#### Scenario: One restore at a time, on one node

- **GIVEN** several test assemblies restoring fixtures at once on a loaded machine
- **WHEN** the restores run
- **THEN** at most one fixture restore child is running at a time, it runs with a single MSBuild node
  and no build server, and its temp directory, package folder, CLI home and HTTP cache are inside the
  fixture

#### Scenario: A stalled stream is not a silent child

- **GIVEN** a restore child that exited but left a stream open
- **WHEN** the drain budget expires
- **THEN** the failure report says the stream had not closed and that its output is missing, and never
  says the child printed nothing

#### Scenario: A zero-error failure names the target it was in

- **GIVEN** a solution restore that failed after entering `ValidateSolutionConfiguration` and printed
  `Build FAILED. 0 Warning(s) 0 Error(s)` with no per-project restore line
- **WHEN** the failure is reported
- **THEN** the report carries the child's own output and names `ValidateSolutionConfiguration` as the
  last target it entered
