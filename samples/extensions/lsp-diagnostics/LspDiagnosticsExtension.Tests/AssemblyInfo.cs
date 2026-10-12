using Xunit.Sdk;
using Xunit.v3;

// The suite spawns a real `dotnet` language-server process (plus an extension host) per test.
// Running tests in parallel starves process startup on small CI runners, so round trips
// occasionally exceed the handler budget and tests fail with no attachment. Serializing the
// suite removes the self-contention; the Windows CI legs are the reason this exists.
[assembly: Parallelization(Mode = ParallelMode.None)]
