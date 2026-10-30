namespace Lunate.Extensibility.Abstractions;

public interface IExtensionContext
{
    string Id { get; }

    IExtensionSettings Settings { get; }

    IExtensionSecrets Secrets { get; }

    IExtensionLog Log { get; }

    /// <summary>
    /// Registers a hook handler. Registration is registration only: it must not start processes,
    /// sockets or timers.
    /// </summary>
    void Register(IHookHandler handler);

    /// <summary>
    /// Registers a named background service. Registration is registration only; the host starts the
    /// service with session lifecycle semantics. Duplicate names are refused.
    /// </summary>
    void RegisterService(string name, IBackgroundService service);

    /// <summary>Subscribes to file changes after successful mutations, optionally filtered by pattern.</summary>
    void SubscribeFileChanged(IFileChangedHandler handler, string? pathPattern = null);

    /// <summary>
    /// Registers a tool-result renderer. Registration is registration only: it must not render or
    /// start processes, sockets or timers.
    /// </summary>
    void RegisterToolResultRenderer(IToolResultRenderer renderer);

    /// <summary>
    /// Registers a status widget. Registration is registration only: it must not render or start
    /// processes, sockets or timers.
    /// </summary>
    void RegisterStatusWidget(IStatusWidget widget);

    /// <summary>
    /// Registers a tool. Registration is registration only: it must not start processes, sockets or
    /// timers. At most 32 tools per extension; malformed registrations and duplicate names are
    /// refused naming the extension and the field.
    /// </summary>
    void RegisterTool(ExtensionToolRegistration registration);

    /// <summary>Registers a read-only verifier declared by name and filename patterns in the manifest.</summary>
    void RegisterWriteVerifier(WriteVerifierRegistration registration);

    /// <summary>
    /// Registers a per-workspace service under its manifest <c>workspaceServices</c> name.
    /// Registration is registration only: the factory must not start processes, sockets or timers.
    /// Duplicate names are refused; the service must be declared in the manifest list, which is
    /// what binds its loaded-workspace cap (the host may lower the cap; it never raises it).
    /// </summary>
    void RegisterWorkspaceService<TService>(string name, IWorkspaceServiceFactory<TService> factory)
        where TService : notnull;

    /// <summary>
    /// Acquires this extension's named workspace service for one canonical worktree root. The value
    /// is created lazily per root; the lease that comes back pins the entry until disposal, and a
    /// registry the session shut down refuses with <see cref="ObjectDisposedException"/>.
    /// </summary>
    ValueTask<IWorkspaceServiceLease<TService>> AcquireWorkspaceServiceAsync<TService>(
        string name,
        string canonicalWorktreeRoot,
        CancellationToken ct = default
    )
        where TService : notnull;

    /// <summary>
    /// Registers an MCP server. Registration is registration only: it must not start processes,
    /// sockets or timers. The first registration per server name wins; a duplicate is refused and
    /// logged. The process starts on the first tool listing.
    /// </summary>
    void RegisterMcpServer(McpServerRegistration registration);

    /// <summary>
    /// Registers a model provider. Registration is registration only: it must not start processes,
    /// sockets or timers; the provider's client is created lazily at first use. At most 8 providers
    /// per extension; duplicate ids and served model ids are refused naming both extensions.
    /// </summary>
    void RegisterModelProvider(ExtensionModelProvider provider);

    /// <summary>
    /// Registers a model middleware at its pipeline slot. Registration is registration only: it
    /// must not start processes, sockets or timers. At most 16 middlewares per extension.
    /// </summary>
    void RegisterModelMiddleware(ExtensionModelMiddleware middleware);

    /// <summary>
    /// Registers a virtual model. Registration is registration only: it must not start processes,
    /// sockets or timers. At most 8 virtual models per extension; duplicate ids are refused naming
    /// both extensions, and backends are validated at first resolve.
    /// </summary>
    void RegisterVirtualModel(VirtualModelRegistration model);

    /// <summary>
    /// Registers a skill source. Registration is registration only: it must not start processes,
    /// sockets or timers. The contributed skill directories must be on disk inside the extension's
    /// own folder; directories outside it are refused with a warning. A duplicate source id is
    /// replaced, with the conflict logged.
    /// </summary>
    void RegisterSkillSource(ISkillSource source);

    /// <summary>
    /// Registers a command source. Registration is registration only: it must not start processes,
    /// sockets or timers. Every command the source describes must be declared in the manifest
    /// <c>commands</c> list; a provided-but-undeclared command or a collision with a built-in
    /// command refuses the load. A declared command no source provides reads
    /// <c>declared, not provided</c> in <c>/extensions</c>.
    /// </summary>
    void RegisterCommandSource(ICommandSource source);

    /// <summary>Looks up a service another extension registered by its name.</summary>
    bool TryGetService(string name, out IBackgroundService? service);

    /// <summary>Looks up a core service handle by its reserved name (<c>core/…</c>).</summary>
    bool TryGetCoreService<T>(string name, out T? service)
        where T : class;
}
