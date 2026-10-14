using Lunate.Agent;
using Lunate.Roslyn;

namespace Lunate.Coding.Tests;

/// <summary>
/// The per-workspace registry: the loaded-workspace cap and its LRU eviction, the touch rule, the
/// in-flight pin (an all-pinned cap waits) and disposal. Backend doubles keep the cap logic under
/// test without loading Roslyn.
/// </summary>
public sealed class WorkspaceServicesTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_roots_load_two_workspaces_and_the_cap_is_not_held_yet()
    {
        using var temp = new TempDirectory();
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));
        string a = CreateRoot(temp, "a");
        string b = CreateRoot(temp, "b");

        await services.BackendFor(a).LoadAsync(Ct);
        await services.BackendFor(b).LoadAsync(Ct);

        Assert.Empty(services.Evictions);
        Assert.Equal(2, services.LoadedWorkspaces.Count);
        Assert.Contains(GitIdentity.Canonicalize(a), services.LoadedWorkspaces);
        Assert.Contains(GitIdentity.Canonicalize(b), services.LoadedWorkspaces);
    }

    [Fact]
    public async Task A_third_root_evicts_the_least_recently_used_and_asserts_the_victim_root()
    {
        using var temp = new TempDirectory();
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));
        string a = CreateRoot(temp, "a");
        string b = CreateRoot(temp, "b");
        string c = CreateRoot(temp, "c");

        await services.BackendFor(a).LoadAsync(Ct);
        await services.BackendFor(b).LoadAsync(Ct);
        await services.BackendFor(c).LoadAsync(Ct);

        Assert.Equal([GitIdentity.Canonicalize(a)], services.Evictions);
        Assert.DoesNotContain(GitIdentity.Canonicalize(a), services.LoadedWorkspaces);
        Assert.Contains(GitIdentity.Canonicalize(b), services.LoadedWorkspaces);
        Assert.Contains(GitIdentity.Canonicalize(c), services.LoadedWorkspaces);
    }

    [Fact]
    public async Task Touching_a_workspace_before_the_next_load_keeps_it()
    {
        using var temp = new TempDirectory();
        using var services = new WorkspaceServices(2, root => new FakeWorkspaceBackend(root));
        string a = CreateRoot(temp, "a");
        string b = CreateRoot(temp, "b");
        string c = CreateRoot(temp, "c");

        await services.BackendFor(a).LoadAsync(Ct);
        await services.BackendFor(b).LoadAsync(Ct);
        // A plain call touches A without loading anything: A becomes the most recent.
        await services.BackendFor(a).OutlineAsync("Calculator.cs", Ct);
        await services.BackendFor(c).LoadAsync(Ct);

        Assert.Equal([GitIdentity.Canonicalize(b)], services.Evictions);
        Assert.Contains(GitIdentity.Canonicalize(a), services.LoadedWorkspaces);
        Assert.Contains(GitIdentity.Canonicalize(c), services.LoadedWorkspaces);
    }

    [Fact]
    public async Task An_in_flight_call_pins_its_workspace_and_the_cap_waits()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        string b = CreateRoot(temp, "b");
        var backendA = new FakeWorkspaceBackend(a) { Hold = new TaskCompletionSource() };
        using var services = new WorkspaceServices(
            1,
            root => root == GitIdentity.Canonicalize(a) ? backendA : new FakeWorkspaceBackend(root)
        );
        await services.BackendFor(a).LoadAsync(Ct);

        Task<DiagnosticsResult> pinned = services
            .BackendFor(a)
            .GetDiagnosticsAsync(DiagnosticsScope.Solution, Ct)
            .AsTask();
        await backendA.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        // The load blocks its calling thread while waiting, so it must start on another thread.
        Task<WorkspaceLoadResult> loadB = Task.Run(
            async () => await services.BackendFor(b).LoadAsync(Ct).ConfigureAwait(false),
            Ct
        );
        await Task.Delay(150, Ct);
        Assert.False(loadB.IsCompleted, "an all-pinned cap must wait for the in-flight call");

        backendA.Hold!.TrySetResult();
        await pinned.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await loadB.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal([GitIdentity.Canonicalize(a)], services.Evictions);
    }

    [Fact]
    public async Task Disposing_the_registry_disposes_every_loaded_backend()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        string b = CreateRoot(temp, "b");
        var backends = new List<FakeWorkspaceBackend>();
        var services = new WorkspaceServices(
            2,
            root =>
            {
                var backend = new FakeWorkspaceBackend(root);
                backends.Add(backend);
                return backend;
            }
        );
        await services.BackendFor(a).LoadAsync(Ct);
        await services.BackendFor(b).LoadAsync(Ct);

        services.Dispose();

        Assert.Equal(2, backends.Count);
        Assert.All(backends, backend => Assert.True(backend.Disposed));
    }

    private static string CreateRoot(TempDirectory temp, string name)
    {
        string root = temp.File(name);
        Directory.CreateDirectory(root);
        return root;
    }
}
