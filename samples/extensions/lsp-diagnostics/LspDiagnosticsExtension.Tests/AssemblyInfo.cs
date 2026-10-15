using Xunit.Sdk;
using Xunit.v3;

// Bounded, not serial: full parallel collections starve the dotnet language-server start on the
// small Windows CI runners (run 38794819725, 2026-10-15: attachment and process-timeout tests
// failed on both Windows legs, different sets per leg — load-dependent). Two concurrent
// collections keep the suite parallel without the self-contention. Evidence protocol and ladder:
// the parallel-sample-suites change (design D2).
[assembly: Parallelization(Mode = ParallelMode.Collections, MaxThreads = 2)]
