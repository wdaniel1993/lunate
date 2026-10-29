using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The registry lifecycle behind the extension contract (spec: Per-workspace extension services,
/// ADR-0021 generalized): LRU eviction of unpinned entries, a pinned lease never evicted while an
/// unpinned entry is, an all-pinned acquisition that waits rather than unloading under a live call,
/// and session end that disposes every entry and refuses later acquisitions.
/// </summary>
public sealed class ExtensionWorkspaceServiceLifecycleTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_third_root_evicts_the_least_recently_used_value_and_disposes_it()
    {
        using var temp = new TempDirectory();
        var (loader, extension) = await LoadedAsync(temp, ("tracker", null));
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");
        string c = WorkspaceServiceSupport.CreateRoot(temp, "c");

        (await Acquire(extension, a)).Dispose();
        (await Acquire(extension, b)).Dispose();
        (await Acquire(extension, c)).Dispose();

        Assert.Equal(
            [GitIdentity.Canonicalize(a)],
            loader.WorkspaceServices.Evictions("svc", "tracker")
        );
        Assert.Equal(
            [GitIdentity.Canonicalize(b), GitIdentity.Canonicalize(c)],
            loader.WorkspaceServices.LoadedRoots("svc", "tracker")
        );
        Assert.Equal([GitIdentity.Canonicalize(a)], extension.Factory("tracker").Disposals);
    }

    [Fact]
    public async Task A_pinned_lease_is_never_evicted_while_an_unpinned_entry_is()
    {
        using var temp = new TempDirectory();
        var (loader, extension) = await LoadedAsync(temp, ("tracker", null));
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");
        string c = WorkspaceServiceSupport.CreateRoot(temp, "c");

        // a carries a live lease (pinned); b's lease was released, so b is the only unpinned
        // entry — and a is also the least-recently-used one, so the pin is what saves it.
        using var leaseA = await Acquire(extension, a);
        (await Acquire(extension, b)).Dispose();
        await Acquire(extension, c);

        Assert.Equal(
            [GitIdentity.Canonicalize(b)],
            loader.WorkspaceServices.Evictions("svc", "tracker")
        );
        Assert.Equal(
            [GitIdentity.Canonicalize(a), GitIdentity.Canonicalize(c)],
            loader.WorkspaceServices.LoadedRoots("svc", "tracker")
        );
    }

    [Fact]
    public async Task An_all_pinned_cap_waits_and_proceeds_when_a_lease_is_disposed()
    {
        using var temp = new TempDirectory();
        var (_, extension) = await LoadedAsync(temp, ("tracker", 1));
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");

        IWorkspaceServiceLease<TrackedValue> pinned = await Acquire(extension, a);

        Task<IWorkspaceServiceLease<TrackedValue>> waiting = Task.Run(
            async () => await Acquire(extension, b).ConfigureAwait(false),
            Ct
        );
        await Task.Delay(150, Ct);
        Assert.False(waiting.IsCompleted, "an all-pinned cap must wait for a lease to be disposed");

        pinned.Dispose();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Contains(GitIdentity.Canonicalize(a), extension.Factory("tracker").Disposals);
    }

    [Fact]
    public async Task An_all_pinned_wait_is_cancellation_aware()
    {
        using var temp = new TempDirectory();
        var (_, extension) = await LoadedAsync(temp, ("tracker", 1));
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");

        IWorkspaceServiceLease<TrackedValue> pinned = await Acquire(extension, a);
        using var cancellation = new CancellationTokenSource();
        Task<IWorkspaceServiceLease<TrackedValue>> waiting = Task.Run(
            async () =>
                await extension
                    .Context!.AcquireWorkspaceServiceAsync<TrackedValue>(
                        "tracker",
                        b,
                        cancellation.Token
                    )
                    .ConfigureAwait(false),
            Ct
        );
        await Task.Delay(150, Ct);
        Assert.False(waiting.IsCompleted, "the wait must not finish before a lease is disposed");

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        Assert.Empty(extension.Factory("tracker").Disposals);

        pinned.Dispose();
        await Acquire(extension, b);
        Assert.Equal(2, extension.Factory("tracker").Creates.Count);
    }

    [Fact]
    public async Task Session_end_disposes_every_loaded_value_and_refuses_later_acquisitions()
    {
        using var temp = new TempDirectory();
        var (loader, extension) = await LoadedAsync(temp, ("tracker", null));
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");
        _ = await Acquire(extension, a);
        _ = await Acquire(extension, b);

        await loader.EndSessionAsync(Ct);

        Assert.Equal(2, extension.Factory("tracker").Disposals.Count);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await Acquire(extension, b));
    }

    [Fact]
    public async Task A_waiter_released_by_session_end_fails_with_object_disposed()
    {
        using var temp = new TempDirectory();
        var (loader, extension) = await LoadedAsync(temp, ("tracker", 1));
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");
        _ = await Acquire(extension, a);

        Task<IWorkspaceServiceLease<TrackedValue>> waiting = Task.Run(
            async () => await Acquire(extension, b).ConfigureAwait(false),
            Ct
        );
        await Task.Delay(150, Ct);

        await loader.EndSessionAsync(Ct);
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => waiting);
    }

    [Fact]
    public void Unregistering_an_extension_disposes_its_loaded_values_and_drops_its_names()
    {
        var disposals = new List<string>();
        var registry = new ExtensionWorkspaceServiceRegistry(null);
        registry.Register(
            "svc",
            "tracker",
            typeof(TrackedValue),
            root => new TrackedValue(root, root => disposals.Add(root)),
            new ExtensionWorkspaceService("tracker", 2)
        );
        registry.Acquire("svc", "tracker", typeof(TrackedValue), "/root/a", Ct).Dispose();
        registry.Acquire("svc", "tracker", typeof(TrackedValue), "/root/a", Ct);

        registry.Unregister("svc");

        Assert.Equal(["/root/a"], disposals);
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
            registry.Acquire("svc", "tracker", typeof(TrackedValue), "/root/a", Ct)
        );
        Assert.Contains("is not registered", refused.Message, StringComparison.Ordinal);
    }

    private static async Task<(
        ExtensionLoader Loader,
        ServicefulExtensionFactory Extension
    )> LoadedAsync(TempDirectory temp, (string Name, int? MaxLoaded) service)
    {
        var factory = new ServicefulExtensionFactory([service.Name]);
        ExtensionLoader loader = WorkspaceServiceSupport.Loader(
            temp,
            WorkspaceServiceSupport.Manifest("svc", [service]),
            factory
        );
        loader.Discover(temp.Root, "repo");
        await loader.Load(
            "svc",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: false),
            Ct
        );
        return (loader, factory);
    }

    private static async Task<IWorkspaceServiceLease<TrackedValue>> Acquire(
        ServicefulExtensionFactory extension,
        string root
    ) =>
        await extension
            .Context!.AcquireWorkspaceServiceAsync<TrackedValue>("tracker", root, Ct)
            .ConfigureAwait(false);
}
