namespace Lunate.Roslyn.Tests;

/// <summary>
/// Tests touching the process-wide MSBuild locator, real workspaces or the bootstrap test seam
/// run one at a time: the locator registers once per process and the seam is global state.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RoslynIntegrationCollection
{
    public const string Name = "roslyn-integration";
}
