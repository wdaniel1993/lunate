using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The cap contract behind the per-workspace services (spec: Per-workspace extension services,
/// tasks 4.3–4.4): the cap is min(declared, host limit) so the manifest cannot raise it past the
/// host budget and the host may lower it, eviction disposes the value but never sweeps or deletes
/// its files, and the manifest cross-check keeps the tool rule asymmetry for services.
/// </summary>
public sealed class ExtensionWorkspaceServiceCapTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_host_lowers_the_cap_and_evicts_the_first_loaded_roots_in_lru_order()
    {
        using var temp = new TempDirectory();
        var factory = new ServicefulExtensionFactory(["tracker"]);
        (string Name, int? MaxLoaded)[] services = [("tracker", 3)];
        var loader = Loader(temp, factory, services, capLimit: 1);
        await LoadedAllAsync(temp, loader);
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");
        string c = WorkspaceServiceSupport.CreateRoot(temp, "c");

        (await Acquire(factory, a)).Dispose();
        (await Acquire(factory, b)).Dispose();
        (await Acquire(factory, c)).Dispose();

        Assert.Equal(
            [GitIdentity.Canonicalize(a), GitIdentity.Canonicalize(b)],
            loader.WorkspaceServices.Evictions("svc", "tracker")
        );
        Assert.Equal(
            [GitIdentity.Canonicalize(c)],
            loader.WorkspaceServices.LoadedRoots("svc", "tracker")
        );
        Assert.Equal(
            [GitIdentity.Canonicalize(a), GitIdentity.Canonicalize(b)],
            factory.Factory("tracker").Disposals
        );
    }

    [Fact]
    public async Task The_manifest_cap_survives_a_larger_host_limit()
    {
        using var temp = new TempDirectory();
        var factory = new ServicefulExtensionFactory(["tracker"]);
        (string Name, int? MaxLoaded)[] services = [("tracker", 1)];
        var loader = Loader(temp, factory, services, capLimit: 5);
        await LoadedAllAsync(temp, loader);
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");

        (await Acquire(factory, a)).Dispose();
        (await Acquire(factory, b)).Dispose();

        // The manifest declared one loaded workspace; the host limit may not raise it, so acquiring
        // the second root evicts the first even though the host allowed five.
        Assert.Equal(
            [GitIdentity.Canonicalize(a)],
            loader.WorkspaceServices.Evictions("svc", "tracker")
        );
        Assert.Equal(
            [GitIdentity.Canonicalize(b)],
            loader.WorkspaceServices.LoadedRoots("svc", "tracker")
        );
    }

    [Fact]
    public async Task Eviction_disposes_the_value_but_never_deletes_its_files()
    {
        using var temp = new TempDirectory();
        var factory = new ServicefulExtensionFactory(["tracker"]);
        var loader = Loader(temp, factory, [("tracker", 2)]);
        await LoadedAllAsync(temp, loader);
        factory.Factory("tracker").CreatesFile = true;
        string a = WorkspaceServiceSupport.CreateRoot(temp, "a");
        string b = WorkspaceServiceSupport.CreateRoot(temp, "b");
        string c = WorkspaceServiceSupport.CreateRoot(temp, "c");
        string durable = Path.Combine(
            GitIdentity.Canonicalize(a),
            WorkspaceServiceSupport.DurableFileName
        );

        (await Acquire(factory, a)).Dispose();
        (await Acquire(factory, b)).Dispose();
        (await Acquire(factory, c)).Dispose();

        // The first root's value was evicted and disposed — but the durable file it wrote stays on
        // disk: the registry only calls Dispose; it never sweeps or deletes root content.
        Assert.Equal([GitIdentity.Canonicalize(a)], factory.Factory("tracker").Disposals);
        Assert.Equal(
            [GitIdentity.Canonicalize(a)],
            loader.WorkspaceServices.Evictions("svc", "tracker")
        );
        Assert.Equal("durable data", await File.ReadAllTextAsync(durable, Ct));
    }

    [Fact]
    public async Task A_registered_but_undeclared_service_refuses_the_load()
    {
        using var temp = new TempDirectory();
        // The manifest declares no workspaceServices at all, yet the factory registers one.
        var factory = new ServicefulExtensionFactory(["tracker"]);
        var loader = Loader(temp, factory, []);

        loader.Discover(temp.Root, "repo");

        ExtensionLoadException refused = await Assert.ThrowsAsync<ExtensionLoadException>(
            async () =>
                await loader.Load(
                    "svc",
                    temp.Root,
                    "repo",
                    new FakeExtensionTrustPrompt(approve: false),
                    Ct
                )
        );
        Assert.Contains(
            "workspace service 'tracker' is registered by code but not declared in the manifest "
                + "'workspaceServices' list",
            refused.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_declared_but_unregistered_service_is_logged_and_dropped()
    {
        using var temp = new TempDirectory();
        var log = new RecordingExtensionLog();
        var factory = new ServicefulExtensionFactory([]);
        (string Name, int? MaxLoaded)[] services = [("tracker", null)];
        var loader = Loader(temp, factory, services, log: log);

        loader.Discover(temp.Root, "repo");
        await loader.Load(
            "svc",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: false),
            Ct
        );

        string warning = Assert.Single(
            log.Messages,
            message =>
                message.Contains("tracker", StringComparison.Ordinal)
                && message.Contains("no registration matched", StringComparison.Ordinal)
        );
        Assert.StartsWith("warn:", warning, StringComparison.Ordinal);

        // The dropped name was never registered, so acquiring it is refused with the usual message.
        IExtensionContext context =
            factory.Context ?? throw new InvalidOperationException("the factory never ran");
        InvalidOperationException acquisition = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await context.AcquireWorkspaceServiceAsync<TrackedValue>("tracker", temp.Root, Ct)
        );
        Assert.Contains(
            "workspace service 'tracker' is not registered",
            acquisition.Message,
            StringComparison.Ordinal
        );
    }

    /// <summary>
    /// A loader with one built-in extension whose manifest declares one workspace service per entry
    /// (an empty list declares none), with the host cap limit and log the test passes through.
    /// </summary>
    private static ExtensionLoader Loader(
        TempDirectory temp,
        ServicefulExtensionFactory factory,
        (string Name, int? MaxLoaded)[] services,
        int? capLimit = null,
        IExtensionLog? log = null
    ) =>
        WorkspaceServiceSupport.Loader(
            temp,
            WorkspaceServiceSupport.Manifest("svc", services),
            factory,
            workspaceServiceCapLimit: capLimit,
            log: log
        );

    private static async Task<IWorkspaceServiceLease<TrackedValue>> Acquire(
        ServicefulExtensionFactory extension,
        string root
    ) =>
        await extension
            .Context!.AcquireWorkspaceServiceAsync<TrackedValue>("tracker", root, Ct)
            .ConfigureAwait(false);

    private static async Task LoadedAllAsync(TempDirectory temp, ExtensionLoader loader)
    {
        loader.Discover(temp.Root, "repo");
        await loader.Load(
            "svc",
            temp.Root,
            "repo",
            new FakeExtensionTrustPrompt(approve: false),
            Ct
        );
    }
}
