using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.Roslyn;

namespace Lunate.Coding.Tests;

/// <summary>
/// The file-change bus subscription of the registry: events route by workspace id to the backend
/// of that root only, absent roots create nothing, non-C# paths are ignored, project/build files
/// set the reload flag, and the subscription dies with the registry.
/// </summary>
public sealed class WorkspaceServicesBusTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_bus_event_marks_the_right_root()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        string b = CreateRoot(temp, "b");
        var backendA = new FakeWorkspaceBackend(GitIdentity.Canonicalize(a));
        var backendB = new FakeWorkspaceBackend(GitIdentity.Canonicalize(b));
        using var services = new WorkspaceServices(
            2,
            root => root == backendA.Root ? backendA : backendB
        );
        await services.BackendFor(a).LoadAsync(Ct);
        await services.BackendFor(b).LoadAsync(Ct);
        var bus = new FileChangeBus(backendA.Root);
        services.Connect(bus);

        string path = Path.Combine(backendA.Root, "src", "Calculator.cs");
        bus.Notify(
            new FileChangeEvent(
                path,
                "src/Calculator.cs",
                backendA.Root,
                null,
                FileChangeKind.Modified
            )
        );
        await bus.DrainAsync(Ct);

        Assert.Equal([path], backendA.FileMarks);
        Assert.Empty(backendB.FileMarks);
        Assert.Equal(0, backendA.ProjectChangeMarks);
        Assert.Equal(0, backendB.ProjectChangeMarks);
    }

    [Fact]
    public async Task An_event_for_an_unknown_root_creates_nothing()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        string unknown = GitIdentity.Canonicalize(CreateRoot(temp, "unknown"));
        List<string> created = [];
        using var services = new WorkspaceServices(
            2,
            root =>
            {
                created.Add(root);
                return new FakeWorkspaceBackend(root);
            }
        );
        await services.BackendFor(a).LoadAsync(Ct);
        var bus = new FileChangeBus(a);
        services.Connect(bus);

        bus.Notify(
            new FileChangeEvent(
                Path.Combine(unknown, "Calculator.cs"),
                "Calculator.cs",
                unknown,
                null,
                FileChangeKind.Modified
            )
        );
        bus.Notify(
            new FileChangeEvent(
                Path.Combine(unknown, "App.csproj"),
                "App.csproj",
                unknown,
                null,
                FileChangeKind.Modified
            )
        );
        await bus.DrainAsync(Ct);

        Assert.Equal([GitIdentity.Canonicalize(a)], created);
    }

    [Fact]
    public async Task Paths_that_are_neither_sources_nor_build_files_are_ignored()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        var backend = new FakeWorkspaceBackend(GitIdentity.Canonicalize(a));
        using var services = new WorkspaceServices(1, root => backend);
        await services.BackendFor(a).LoadAsync(Ct);
        var bus = new FileChangeBus(backend.Root);
        services.Connect(bus);

        bus.Notify(
            new FileChangeEvent(
                Path.Combine(backend.Root, "notes.txt"),
                "notes.txt",
                backend.Root,
                null,
                FileChangeKind.Modified
            )
        );
        bus.Notify(
            new FileChangeEvent(
                Path.Combine(backend.Root, "AssemblyInfo.cs.txt"),
                "AssemblyInfo.cs.txt",
                backend.Root,
                null,
                FileChangeKind.Modified
            )
        );
        await bus.DrainAsync(Ct);

        Assert.Empty(backend.FileMarks);
        Assert.Equal(0, backend.ProjectChangeMarks);
    }

    [Fact]
    public async Task Project_and_build_configuration_files_mark_a_reload()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        var backend = new FakeWorkspaceBackend(GitIdentity.Canonicalize(a));
        using var services = new WorkspaceServices(1, root => backend);
        await services.BackendFor(a).LoadAsync(Ct);
        var bus = new FileChangeBus(backend.Root);
        services.Connect(bus);

        string[] triggers =
        [
            "App.csproj",
            "App.sln",
            "App.slnx",
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "global.json",
        ];
        foreach (string name in triggers)
        {
            bus.Notify(
                new FileChangeEvent(
                    Path.Combine(backend.Root, name),
                    name,
                    backend.Root,
                    null,
                    FileChangeKind.Modified
                )
            );
        }

        await bus.DrainAsync(Ct);

        Assert.Equal(triggers.Length, backend.ProjectChangeMarks);
        Assert.Empty(backend.FileMarks);
    }

    [Fact]
    public async Task A_disposed_registry_ignores_later_events()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        var backend = new FakeWorkspaceBackend(GitIdentity.Canonicalize(a));
        var services = new WorkspaceServices(1, root => backend);
        await services.BackendFor(a).LoadAsync(Ct);
        var bus = new FileChangeBus(backend.Root);
        services.Connect(bus);
        services.Dispose();

        bus.Notify(
            new FileChangeEvent(
                Path.Combine(backend.Root, "Calculator.cs"),
                "Calculator.cs",
                backend.Root,
                null,
                FileChangeKind.Modified
            )
        );
        await bus.DrainAsync(Ct);

        Assert.Empty(backend.FileMarks);
    }

    [Fact]
    public async Task The_cs_suffix_check_is_case_insensitive()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        var backend = new FakeWorkspaceBackend(GitIdentity.Canonicalize(a));
        using var services = new WorkspaceServices(1, root => backend);
        await services.BackendFor(a).LoadAsync(Ct);
        var bus = new FileChangeBus(backend.Root);
        services.Connect(bus);

        string path = Path.Combine(backend.Root, "CALCULATOR.CS");
        bus.Notify(
            new FileChangeEvent(path, "CALCULATOR.CS", backend.Root, null, FileChangeKind.Modified)
        );
        await bus.DrainAsync(Ct);

        Assert.Equal([path], backend.FileMarks);
    }

    [Fact]
    public async Task Connecting_twice_still_delivers_one_mark()
    {
        using var temp = new TempDirectory();
        string a = CreateRoot(temp, "a");
        var backend = new FakeWorkspaceBackend(GitIdentity.Canonicalize(a));
        using var services = new WorkspaceServices(1, root => backend);
        await services.BackendFor(a).LoadAsync(Ct);
        var bus = new FileChangeBus(backend.Root);
        services.Connect(bus);
        services.Connect(bus);

        string path = Path.Combine(backend.Root, "Calculator.cs");
        bus.Notify(
            new FileChangeEvent(path, "Calculator.cs", backend.Root, null, FileChangeKind.Modified)
        );
        await bus.DrainAsync(Ct);

        Assert.Equal([path], backend.FileMarks);
    }

    private static string CreateRoot(TempDirectory temp, string name)
    {
        string root = temp.File(name);
        Directory.CreateDirectory(root);
        return root;
    }
}
