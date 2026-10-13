using Xunit.Sdk;
using Xunit.v3;

// The suite runs a real extension host per test (real loader, real session files). Serializing the
// suite keeps the file-system and event-ordering assertions deterministic on small CI runners.
[assembly: Parallelization(Mode = ParallelMode.None)]
