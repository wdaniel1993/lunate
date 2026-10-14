using Xunit.Sdk;
using Xunit.v3;

// The suite runs a real extension host and real git processes per test (temp repositories).
// Serializing the suite keeps worktree state and process spawns deterministic on small runners.
[assembly: Parallelization(Mode = ParallelMode.None)]
