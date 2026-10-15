// The suite spawns a real `dotnet` MCP server process (plus an extension host) per test.
// Running tests in parallel starves process startup on small CI runners, so round trips
// occasionally exceed the budget and tests fail with no result. Serializing the suite removes
// the self-contention; the Windows CI legs are the reason this exists.
