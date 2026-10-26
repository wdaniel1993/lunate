using System.Reflection;
using System.Runtime.Loader;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    /// <summary>
    /// Loads one extension into its collectible context. <paramref name="startSession"/> false
    /// composes in register-only inspect mode: the factory runs and registrations stand, but this
    /// extension's background services do not start and no <c>ToolsChanged</c> is dispatched (the
    /// <c>lunate extensions</c> path, design D3). The session-started event is not fired by a load
    /// at all: the composition fires it once, after every extension loaded at session start has
    /// loaded (design D1).
    /// </summary>
    public async Task<LoadedExtension> Load(
        string id,
        string workingDirectory,
        string repositoryIdentity,
        IExtensionTrustPrompt prompt,
        CancellationToken cancellationToken = default,
        bool startSession = true
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(repositoryIdentity);
        ArgumentNullException.ThrowIfNull(prompt);

        if (!_descriptorsById.TryGetValue(id, out ExtensionDescriptor? descriptor))
        {
            throw new ExtensionLoadException(
                $"extension '{id}' is not installed; discovered: {DiscoveredIds(_descriptors)}."
            );
        }

        if (_loaded.ContainsKey(id))
        {
            throw new ExtensionLoadException(
                $"extension '{id}' is already loaded; unload it before loading it again."
            );
        }

        // Project extensions always ask; an installed one asks in either scope, because installing
        // is not trusting (addendum, phase 6). A hand-placed user-scope extension is still trusted
        // by placement and never reaches this.
        InstalledPackageProvenance? installedProvenance = Installed.ForExtension(
            descriptor.Directory
        );
        if (descriptor.Scope is ExtensionScope.Project || installedProvenance is not null)
        {
            await EnsureTrustedAsync(
                    descriptor,
                    workingDirectory,
                    repositoryIdentity,
                    prompt,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        if (descriptor.Manifest.Runtime is not null)
        {
            return await LoadRemoteAsync(
                    descriptor,
                    workingDirectory,
                    cancellationToken,
                    startSession
                )
                .ConfigureAwait(false);
        }

        string entryAssembly = descriptor.Manifest.EntryAssembly!;
        string entryPath = Path.Combine(descriptor.Directory, entryAssembly);
        if (!File.Exists(entryPath))
        {
            throw new ExtensionLoadException(
                $"extension '{id}': entry assembly '{entryAssembly}' was not found in '{descriptor.Directory}'."
            );
        }

        // The contract gate, before any of the assembly's types are opened: the manifest declares
        // the API the code needs, and a DLL built against a newer contract says so in its own
        // metadata even when the declaration is optimistic.
        if (ExtensionApiGate.RefuseRange(id, descriptor.Manifest.ApiVersion) is { } declared)
        {
            throw new ExtensionLoadException(declared);
        }

        if (ExtensionApiGate.ReferencedVersion(entryPath) is { } referenced)
        {
            if (ExtensionApiGate.Refuse(id, referenced) is { } refusal)
            {
                throw new ExtensionLoadException(
                    FormattableString.Invariant(
                        $"{refusal} Its '{entryAssembly}' was built against Lunate.Extensibility.Abstractions {referenced}."
                    )
                );
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var context = new ExtensionLoadContext(descriptor.Id, descriptor.Directory);
        if (context.ShadowedNames.Count > 0)
        {
            _ignoredCopies[descriptor.Id] = new IgnoredHostCopy(
                context.ShadowedNames,
                [descriptor.Directory]
            );
        }

        if (context.ShadowedNames.Count > 0)
        {
            (_options.Log ?? NullExtensionLog.Instance).Warn(
                HostOwnedAssemblies.Describe(
                    descriptor.Id,
                    context.ShadowedNames,
                    FormattableString.Invariant($"its directory '{descriptor.Directory}'")
                )
            );
        }

        int toolCountBefore = _tools.List().Count;
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(entryPath);
            return await ActivateAsync(
                    descriptor,
                    assembly,
                    context,
                    ExtensionSecretsStore.Load(_options, descriptor.Id),
                    startSession,
                    notifyToolsChanged: true,
                    toolCountBefore,
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (ExtensionLoadException)
        {
            await RollbackAsync(descriptor.Id, context).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            Rollback(descriptor.Id, context);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAsync(descriptor.Id, context).ConfigureAwait(false);
            throw WrapLoadFailure(descriptor, exception);
        }
    }

    /// <summary>
    /// The tail every runtime kind shares once its assembly is loaded: find the factory, create the
    /// extension against a fresh context, cross-check the manifest declarations, publish the
    /// registrations and — for a session load — start the background services and tell the session
    /// the tool set changed. Deliberately free of rollback: the caller owns the try/catch so each
    /// runtime kind reports its own failure in its own words.
    /// </summary>
    private async Task<LoadedExtension> ActivateAsync(
        ExtensionDescriptor descriptor,
        Assembly assembly,
        AssemblyLoadContext context,
        IExtensionSecrets secrets,
        bool startSession,
        bool notifyToolsChanged,
        int toolCountBefore,
        CancellationToken cancellationToken
    )
    {
        IExtensionFactory factory = FindFactory(assembly, descriptor);
        var extensionContext = CreateExtensionContext(descriptor, secrets);
        _extensionContexts[descriptor.Id] = extensionContext;
        IExtension extension = factory.Create(extensionContext);
        CrossCheckManifestTools(descriptor);
        await CrossCheckManifestCommandsAsync(descriptor, cancellationToken).ConfigureAwait(false);
        _modelRegistry.RegisterDeclared(descriptor.Id, descriptor.Manifest.ModelProviders, secrets);
        var loaded = new LoadedExtension(descriptor.Id, extension, descriptor);
        _loaded[descriptor.Id] = loaded;
        _contexts[descriptor.Id] = context;
        if (startSession)
        {
            if (!_sessionEnded)
            {
                _sessionBegan = true;
                await _backgroundServices.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            if (notifyToolsChanged && _tools.List().Count != toolCountBefore)
            {
                await NotifyToolsChangedAsync().ConfigureAwait(false);
            }
        }

        return loaded;
    }

    /// <summary>
    /// Removes everything one failed load may have registered and unloads its context. The
    /// synchronous half is the cancellation path, where awaiting would be a second cancellation
    /// point for a load that is already ending.
    /// </summary>
    private async Task RollbackAsync(string id, AssemblyLoadContext context)
    {
        DropRegistrations(id);
        await _backgroundServices.DropAsync(id).ConfigureAwait(false);
        context.Unload();
    }

    private void Rollback(string id, AssemblyLoadContext context)
    {
        _extensionContexts.Remove(id);
        _tools.Unregister(id);
        _commandSources.Unregister(id);
        _declaredNotProvidedCommands.Remove(id);
        context.Unload();
    }

    private void DropRegistrations(string id)
    {
        _extensionContexts.Remove(id);
        _tools.Unregister(id);
        _modelRegistry.Unregister(id);
        _toolResultRenderers.Unregister(id);
        _statusWidgets.Unregister(id);
        _mcpServers.Unregister(id);
        _skillSources.Unregister(id);
        _commandSources.Unregister(id);
        _declaredNotProvidedCommands.Remove(id);
        DropSubscriptions(id);
    }

    /// <summary>The per-extension context both runtime kinds register through.</summary>
    private ExtensionContext CreateExtensionContext(
        ExtensionDescriptor descriptor,
        IExtensionSecrets secrets
    ) =>
        new(
            descriptor.Id,
            ExtensionSettingsStore.Load(_options, descriptor),
            secrets,
            _options.Log ?? NullExtensionLog.Instance,
            _hooks.Register,
            _backgroundServices.Register,
            SubscribeFileChanged,
            FindService,
            FindCoreService,
            _toolResultRenderers.Register,
            _statusWidgets.Register,
            _mcpServers.Register,
            _tools.Register,
            _modelRegistry.RegisterProvider,
            _modelRegistry.RegisterModelMiddleware,
            _modelRegistry.RegisterVirtualModel,
            _skillSources.Register,
            _commandSources.Register
        );

    private IBackgroundService? FindService(string name) =>
        _backgroundServices.Services.TryGet(name, out IBackgroundService? service) ? service : null;

    private object? FindCoreService(string name) => _backgroundServices.Services.FindCore(name);

    /// <summary>
    /// The asymmetric manifest rule (design D6): the manifest is what the user trusted before any
    /// code loaded, so every registered tool must be declared — a registered-but-undeclared tool
    /// refuses the load. A declared-but-unregistered name is logged as a warning and dropped from
    /// the offered set (it was never registered, so nothing is offered).
    /// </summary>
    private void CrossCheckManifestTools(ExtensionDescriptor descriptor)
    {
        IReadOnlyList<ExtensionToolAdapter> registered =
        [
            .. _tools
                .List()
                .Where(tool =>
                    string.Equals(tool.ExtensionId, descriptor.Id, StringComparison.Ordinal)
                ),
        ];
        HashSet<string> declared = new(descriptor.Manifest.Tools, StringComparer.Ordinal);
        foreach (ExtensionToolAdapter tool in registered)
        {
            if (!declared.Contains(tool.LocalName))
            {
                throw new ExtensionLoadException(
                    $"extension '{descriptor.Id}': tool '{tool.LocalName}' is registered by code but not declared in the manifest 'tools' list; the manifest must cover everything the code contributes. Declare it or remove the registration."
                );
            }
        }

        foreach (string name in descriptor.Manifest.Tools)
        {
            if (
                !registered.Any(tool =>
                    string.Equals(tool.LocalName, name, StringComparison.Ordinal)
                )
            )
            {
                (_options.Log ?? NullExtensionLog.Instance).Warn(
                    $"extension '{descriptor.Id}': manifest declares tool '{name}' but no registration matched; the entry is dropped from the offered set."
                );
            }
        }
    }

    private async Task EnsureTrustedAsync(
        ExtensionDescriptor descriptor,
        string workingDirectory,
        string repositoryIdentity,
        IExtensionTrustPrompt prompt,
        CancellationToken cancellationToken
    )
    {
        string worktreePath = Path.GetFullPath(workingDirectory);
        InstalledPackageProvenance? provenance = Installed.ForExtension(descriptor.Directory);
        string contentHash = Installed.BindTrustDigest(
            ExtensionTrustStore.ComputeContentHash(descriptor.Directory),
            provenance
        );
        var store = new ExtensionTrustStore(_options);

        switch (store.Evaluate(repositoryIdentity, worktreePath, contentHash))
        {
            case ExtensionTrustDecision.Trusted:
                return;
            case ExtensionTrustDecision.NewWorktree:
                store.RecordTrusted(
                    repositoryIdentity,
                    worktreePath,
                    contentHash,
                    DateTimeOffset.UtcNow
                );
                return;
            default:
                ProjectTrustDispatch trust = await _hooks
                    .RunProjectTrustAsync(
                        new ProjectTrustPayload(
                            descriptor.Id,
                            descriptor.Directory,
                            repositoryIdentity,
                            worktreePath
                        ),
                        GlobalLoadedIds(),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (trust.Result is ProjectTrustResult.Deny deny)
                {
                    throw new ExtensionTrustDeniedException(
                        $"extension '{descriptor.Id}' was not trusted for repository '{repositoryIdentity}': {deny.Reason} It was not loaded."
                    );
                }

                if (trust.HasHandlers)
                {
                    store.RecordTrusted(
                        repositoryIdentity,
                        worktreePath,
                        contentHash,
                        DateTimeOffset.UtcNow
                    );
                    return;
                }

                bool filesChanged = store
                    .Records(repositoryIdentity)
                    .Any(record => record.Section is TrustSection.Extensions);
                bool approved = await prompt
                    .ApproveAsync(
                        new ExtensionTrustRequest(descriptor, filesChanged)
                        {
                            FromPackage = provenance?.QuestionLine,
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                if (!approved)
                {
                    throw new ExtensionTrustDeniedException(
                        $"extension '{descriptor.Id}' was not trusted for repository '{repositoryIdentity}'; it was not loaded. Approve the trust prompt to load it."
                    );
                }

                store.RecordTrusted(
                    repositoryIdentity,
                    worktreePath,
                    contentHash,
                    DateTimeOffset.UtcNow
                );
                return;
        }
    }

    private IReadOnlyCollection<string> GlobalLoadedIds() =>
        [
            .. _descriptorsById
                .Values.Where(descriptor =>
                    descriptor.Scope == ExtensionScope.Global && _loaded.ContainsKey(descriptor.Id)
                )
                .Select(descriptor => descriptor.Id),
        ];

    private static IExtensionFactory FindFactory(Assembly assembly, ExtensionDescriptor descriptor)
    {
        Type[] types = assembly.GetExportedTypes();
        List<Type> factories =
        [
            .. types.Where(type =>
                type is { IsAbstract: false, IsInterface: false, IsClass: true }
                && typeof(IExtensionFactory).IsAssignableFrom(type)
            ),
        ];

        if (factories.Count != 1)
        {
            throw new ExtensionLoadException(
                $"extension '{descriptor.Id}': entry assembly '{descriptor.Manifest.EntryAssembly}' has {factories.Count} public IExtensionFactory implementations; declare exactly one public non-abstract type implementing IExtensionFactory."
            );
        }

        Type factory = factories[0];
        if (factory.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new ExtensionLoadException(
                $"extension '{descriptor.Id}': IExtensionFactory '{factory.FullName}' needs a public parameterless constructor."
            );
        }

        return (IExtensionFactory)Activator.CreateInstance(factory)!;
    }

    private static ExtensionLoadException WrapLoadFailure(
        ExtensionDescriptor descriptor,
        Exception exception
    )
    {
        string? missing = FindMissingAssembly(exception);
        if (missing is not null)
        {
            return new ExtensionLoadException(
                $"extension '{descriptor.Id}': failed to load '{descriptor.Manifest.EntryAssembly}': missing dependency '{missing}'. Place the dependency in '{descriptor.Directory}'.",
                exception
            );
        }

        return new ExtensionLoadException(
            $"extension '{descriptor.Id}': failed to load '{descriptor.Manifest.EntryAssembly}' from '{descriptor.Directory}': {exception.Message}",
            exception
        );
    }

    private static string? FindMissingAssembly(Exception exception) =>
        exception switch
        {
            FileNotFoundException file => file.FileName ?? file.Message,
            FileLoadException file => file.FileName ?? file.Message,
            ReflectionTypeLoadException reflection => reflection
                .LoaderExceptions.Where(loader => loader is not null)
                .Select(loader => FindMissingAssembly(loader!))
                .FirstOrDefault(name => name is not null),
            _ => exception.InnerException is null
                ? null
                : FindMissingAssembly(exception.InnerException),
        };
}
