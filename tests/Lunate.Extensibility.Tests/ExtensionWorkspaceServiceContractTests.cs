using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The extension contract for per-workspace services (spec: Per-workspace extension services): an
/// extension registers its named service with a factory, acquires it for a worktree root, and
/// receives a pinned lease naming its root. Values are created lazily per canonical root; nothing
/// is created before the first acquisition.
/// </summary>
public sealed class ExtensionWorkspaceServiceContractTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_extension_registers_and_acquires_its_service_per_root_lazily()
    {
        using var temp = new TempDirectory();
        var factory = new ServicefulExtensionFactory(["tracker"]);
        var loader = WorkspaceServiceSupport.Loader(
            temp,
            WorkspaceServiceSupport.Manifest("svc", [("tracker", null)]),
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

        Assert.Empty(factory.Factory("tracker").Creates);
        IExtensionContext context =
            factory.Context ?? throw new InvalidOperationException("the factory never ran");

        string rootA = WorkspaceServiceSupport.CreateRoot(temp, "a");
        IWorkspaceServiceLease<TrackedValue> leaseA =
            await context.AcquireWorkspaceServiceAsync<TrackedValue>("tracker", rootA, Ct);

        Assert.Equal([GitIdentity.Canonicalize(rootA)], factory.Factory("tracker").Creates);
        Assert.Equal(GitIdentity.Canonicalize(rootA), leaseA.WorktreeRoot);
        Assert.Equal(GitIdentity.Canonicalize(rootA), leaseA.Value.Root);

        string rootB = WorkspaceServiceSupport.CreateRoot(temp, "b");
        IWorkspaceServiceLease<TrackedValue> leaseB =
            await context.AcquireWorkspaceServiceAsync<TrackedValue>("tracker", rootB, Ct);
        IWorkspaceServiceLease<TrackedValue> leaseAagain =
            await context.AcquireWorkspaceServiceAsync<TrackedValue>("tracker", rootA, Ct);

        Assert.Equal(
            [GitIdentity.Canonicalize(rootA), GitIdentity.Canonicalize(rootB)],
            factory.Factory("tracker").Creates
        );
        Assert.True(
            ReferenceEquals(leaseA.Value, leaseAagain.Value),
            "a loaded entry's value must be shared across acquisitions of the same root"
        );
        Assert.False(ReferenceEquals(leaseA.Value, leaseB.Value));
    }

    [Fact]
    public async Task A_registered_service_refuses_a_duplicate_name()
    {
        using var temp = new TempDirectory();
        var factory = new ServicefulExtensionFactory(["tracker", "tracker"]);
        ExtensionLoader loader = WorkspaceServiceSupport.Loader(
            temp,
            WorkspaceServiceSupport.Manifest("svc", [("tracker", null)]),
            factory
        );
        loader.Discover(temp.Root, "repo");

        ExtensionLoadException exception = await Assert.ThrowsAsync<ExtensionLoadException>(
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
            "workspace service 'tracker' was refused",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "the name is already registered",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Acquiring_a_service_the_extension_never_registered_is_refused_with_teaching()
    {
        using var temp = new TempDirectory();
        var factory = new ServicefulExtensionFactory(["tracker"]);
        var loader = WorkspaceServiceSupport.Loader(
            temp,
            WorkspaceServiceSupport.Manifest("svc", [("tracker", null)]),
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

        IExtensionContext context = factory.Context!;
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await context.AcquireWorkspaceServiceAsync<TrackedValue>("ghost", temp.Root, Ct)
        );
        Assert.Contains(
            "workspace service 'ghost' is not registered",
            exception.Message,
            StringComparison.Ordinal
        );
    }
}
