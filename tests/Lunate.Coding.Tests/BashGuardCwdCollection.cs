namespace Lunate.Coding.Tests;

/// <summary>
/// The guard's process-directory test changes <c>Environment.CurrentDirectory</c>, a process-wide
/// mutation, so it never runs in parallel with another collection (the same shape as Lunate.Ai's
/// <c>ChatClientFactoryEnvCollection</c>); the chdir window stays inside one test and is restored
/// in a finally.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BashGuardCwdCollection
{
    public const string Name = "bash-guard-cwd";
}
