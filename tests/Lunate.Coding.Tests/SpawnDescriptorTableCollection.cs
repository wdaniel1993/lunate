namespace Lunate.Coding.Tests;

/// <summary>
/// The parent's descriptor table is process-wide state. The spawn's own pipes are close-on-exec
/// (ADR-0034), so a concurrently spawned child never inherits them; a desktop-library
/// <c>Process.Start</c> is not, and on Unix it keeps its child-side pipe ends inheritable in the
/// parent until it forks, so any <c>posix_spawn</c> that runs in that window copies them into its
/// child. A test that reads a child's descriptor list against this process's therefore only holds
/// when no other test is starting a process: the collection runs its own tests sequentially, and
/// <c>DisableParallelization</c> makes it exclusive with every other collection too — the same
/// shape as the other process-global collections (#862).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpawnDescriptorTableCollection
{
    public const string Name = "spawn-descriptor-table";
}
