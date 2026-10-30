using Xunit.Sdk;
using Xunit.v3;

// Serialized, with evidence — the parallel-sample-suites change, design D2 ladder:
// - Full parallel (run 38794819725): attachment and process-timeout tests failed on both Windows
//   legs (bash 1 error, PowerShell 3 errors — different sets per leg, load-dependent).
// - Bounded MaxThreads = 2 (run 38797312304, attempt 3): the Windows bash leg passed, the
//   PowerShell leg still starved the per-test `dotnet` language-server start (attachment notices
//   never arrived).
// Two concurrent server starts still exceed the PowerShell leg's budget; serializing removes the
// self-contention. Revisit if the runner images change or the suite gains per-test server reuse.
[assembly: Parallelization(Mode = ParallelMode.None)]
