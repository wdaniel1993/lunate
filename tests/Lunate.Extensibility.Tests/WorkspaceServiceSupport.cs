using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// Support for workspace-service tests: tracked values, the factory the extension registers through
/// the contract, and loaders whose built-in extension registers the services a manifest declares.
/// </summary>
internal static class WorkspaceServiceSupport
{
    /// <summary>The file the durable-test factory leaves at a root; eviction must not delete it.</summary>
    public const string DurableFileName = "service-data.txt";

    /// <summary>A root under the temp directory the registry keys are derived from.</summary>
    public static string CreateRoot(TempDirectory temp, string name)
    {
        string root = temp.File(name);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// A built-in manifest declaring <paramref name="tools"/> and one workspace service per entry;
    /// an entry's cap is omitted when the tuple carries none.
    /// </summary>
    public static ExtensionManifest Manifest(
        string id,
        (string Name, int? MaxLoaded)[] services,
        string[]? tools = null
    )
    {
        string servicesJson = string.Join(
            ",",
            services.Select(entry =>
                entry.MaxLoaded is { } cap
                    ? $"{{\"name\":\"{entry.Name}\",\"maxLoadedWorkspaces\":{cap}}}"
                    : $"{{\"name\":\"{entry.Name}\"}}"
            )
        );
        return ExtensionManifest.Parse(
            $$"""{"id":"{{id}}","version":"0.1.0","apiVersion":"^1.0.0","builtin":true,"tools":{{System.Text.Json.JsonSerializer.Serialize(tools ?? [])}},"workspaceServices":[{{servicesJson}}]}""",
            $"host/{id}/extension.json"
        );
    }

    /// <summary>
    /// A zero-trust loader whose built-in <paramref name="factory"/> registers everything its
    /// manifest declares; <paramref name="workspaceServiceCapLimit"/> and <paramref name="log"/>
    /// set the host seams tests need. ( <c>ExtensionHostOptions</c> is init-only, so a mutate
    /// callback cannot set them.)
    /// </summary>
    public static ExtensionLoader Loader(
        TempDirectory temp,
        ExtensionManifest manifest,
        ServicefulExtensionFactory factory,
        int? workspaceServiceCapLimit = null,
        IExtensionLog? log = null
    )
    {
        var options = new ExtensionHostOptions
        {
            StorePath = temp.Subdirectory("store"),
            BuiltIns =
            [
                new BuiltInExtension($"host/{manifest.Id}/extension.json", manifest, () => factory),
            ],
            WorkspaceServiceCapLimit = workspaceServiceCapLimit,
            Log = log,
        };
        return new ExtensionLoader(options);
    }
}

/// <summary>
/// The test value of a per-workspace service: it remembers its root and calls back on disposal, so
/// a test can prove which entries the cap evicted and which shutdown disposed.
/// </summary>
internal sealed class TrackedValue : IDisposable
{
    public TrackedValue(string root, Action<string> onDispose)
    {
        Root = root;
        _onDispose = onDispose;
    }

    public string Root { get; }

    private readonly Action<string> _onDispose;

    public void Dispose() => _onDispose(Root);
}

/// <summary>
/// The test factory behind one registered workspace service: records every root it produced and
/// every disposed value, and can leave a durable file at the root (the "disposal only" stand of
/// eviction tests). The extension's registrations and acquisitions go through this — it is how a
/// test proves the extension contract was honored.
/// </summary>
internal sealed class TrackingValueFactory : IWorkspaceServiceFactory<TrackedValue>
{
    private readonly object _gate = new();
    private readonly Func<string, TrackedValue>? _create;

    public TrackingValueFactory(Func<string, TrackedValue>? create = null) => _create = create;

    public bool CreatesFile { get; set; }

    /// <summary>The canonical roots the factory created values for, in call order.</summary>
    public List<string> Creates { get; } = [];

    /// <summary>The canonical roots whose values were disposed, in order.</summary>
    public List<string> Disposals { get; } = [];

    public TrackedValue Create(string canonicalWorktreeRoot)
    {
        lock (_gate)
        {
            Creates.Add(canonicalWorktreeRoot);
        }

        if (CreatesFile)
        {
            File.WriteAllText(
                Path.Combine(canonicalWorktreeRoot, WorkspaceServiceSupport.DurableFileName),
                "durable data"
            );
        }

        return new TrackedValue(
            canonicalWorktreeRoot,
            root =>
            {
                lock (_gate)
                {
                    Disposals.Add(root);
                }
            }
        );
    }
}

/// <summary>
/// An extension whose factory registers the named workspace services and tools, and hands the host
/// context to the test: the test then drives the contract's acquisition path a handler would.
/// </summary>
internal sealed class ServicefulExtensionFactory(
    string[] services,
    string[]? tools = null,
    string[]? duplicateServices = null
) : IExtensionFactory
{
    private readonly Dictionary<string, TrackingValueFactory> _factories = [];

    public IExtensionContext? Context { get; private set; }

    /// <summary>The registered factory of one of the extension's services, by manifest name.</summary>
    public TrackingValueFactory Factory(string name) => _factories[name];

    public IExtension Create(IExtensionContext context)
    {
        Context = context;
        foreach (string name in services)
        {
            var factory = new TrackingValueFactory();
            _factories[name] = factory;
            context.RegisterWorkspaceService(name, factory);
        }

        foreach (string name in duplicateServices ?? [])
        {
            context.RegisterWorkspaceService(name, new TrackingValueFactory());
        }

        foreach (string tool in tools ?? [])
        {
            context.RegisterTool(
                new ExtensionToolRegistration
                {
                    Name = tool,
                    Description = "a workspace-service test tool",
                    ParametersSchema = System.Text.Json.JsonSerializer.SerializeToElement(
                        new { type = "object", properties = Array.Empty<object>() }
                    ),
                    Handler = new StaticHandler($"{tool} ran"),
                }
            );
        }

        return new EmptyExtension();
    }
}
