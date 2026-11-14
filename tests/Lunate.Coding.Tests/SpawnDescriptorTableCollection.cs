namespace Lunate.Coding.Tests;

/// <summary>
/// The parent's descriptor table is process-wide state. The spawn's own pipes are close-on-exec
/// (ADR-0034), so a concurrently spawned child never inherits them, but a descriptor that any other
/// code in this process opens without close-on-exec — a desktop-library spawn's child-side handle,
/// for one — is copied into every <c>posix_spawn</c> child that forks while it is open. A test that
/// reads a child's descriptor list against this process's therefore only holds when no other test is
/// running: the collection runs its own tests sequentially, and <c>DisableParallelization</c> makes it
/// exclusive with every other collection too — the same shape as the other process-global
/// collections (#862).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SpawnDescriptorTableCollection
{
    public const string Name = "spawn-descriptor-table";
}
