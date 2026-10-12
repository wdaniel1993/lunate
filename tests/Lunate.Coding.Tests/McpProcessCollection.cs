namespace Lunate.Coding.Tests;

/// <summary>
/// Tests that spawn MCP test-server processes run one at a time: spawn storms on loaded CI runners
/// starve process startup and make composition time out. Serializing them keeps them off other
/// collections' backs.
/// </summary>
[CollectionDefinition("mcp-processes", DisableParallelization = true)]
public sealed class McpProcessCollection;
