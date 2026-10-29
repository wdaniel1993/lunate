using System.Reflection;
using System.Runtime.Loader;
using Lunate.Extensibility.Abstractions;
using Lunate.Extensibility.Remote;

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

        // The built-in front door (design §3): the host composition supplied the factory, so no
        // assembly load, no collectible context, no scope, install-record or trust step runs —
        // the shared activation tail is the directory door's.
        if (descriptor.Scope is ExtensionScope.Builtin)
        {
            if (!_builtInById.TryGetValue(id, out BuiltInExtension? builtIn))
            {
                throw new ExtensionLoadException(
                    $"extension '{id}' composes as a built-in, but the host supplied no factory for it."
                );
            }

            return await LoadBuiltInAsync(descriptor, builtIn, startSession, cancellationToken)
                .ConfigureAwait(false);
        }

        // A directory that is a symlink out of its scope is refused by name: the scope is the trust
        // boundary, and a link that leaves it would load code from somewhere the user never chose.
        string extensionScopeRoot =
            descriptor.Scope is ExtensionScope.Project
                ? ProjectScope.Under(
                    Path.GetFullPath(workingDirectory),
                    ProjectScope.ExtensionsDirectory
                )
                : _options.ExtensionsPath;
        try
        {
            ScopePaths.RequireInside(extensionScopeRoot, descriptor.Directory!, "load");
        }
        catch (InvalidOperationException exception)
        {
            throw new ExtensionLoadException(exception.Message, exception);
        }

        // Project extensions always ask; an installed one asks in either scope, because installing
        // is not trusting (addendum, phase 6). A hand-placed user-scope extension is still trusted
        // by placement and never reaches this.
        InstalledPackageProvenance? installedProvenance = Installed.ForExtension(
            descriptor.Directory!
        );

        // Fail closed: a store holding a record that cannot be read or no longer answers for itself
        // cannot tell a hand-placed user extension from one whose package record was damaged, so a
        // user-scope extension with no provenance is not loaded while that is true.
        if (
            descriptor.Scope is ExtensionScope.Global
            && installedProvenance is null
            && Installed.Broken.FirstOrDefault() is { } broken
        )
        {
            throw new ExtensionLoadException(
                $"extension '{id}' is not loaded: the install record '{broken.RecordPath}' is broken ({broken.Reason}), so it cannot be shown to be hand-placed."
                    + " Run 'lunate packages --verify' to see the broken record, then reinstall the package it belongs to."
            );
        }

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
        string entryPath = Path.Combine(descriptor.Directory!, entryAssembly);
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

        var context = new ExtensionLoadContext(descriptor.Id, descriptor.Directory!);
        if (context.ShadowedNames.Count > 0)
        {
            _ignoredCopies[descriptor.Id] = new IgnoredHostCopy(
                context.ShadowedNames,
                [descriptor.Directory!]
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
                    () => FindFactory(assembly, descriptor),
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
            await RollbackAsync(descriptor.Id, context).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAsync(descriptor.Id, context).ConfigureAwait(false);
            throw WrapLoadFailure(descriptor, exception);
        }
    }

    /// <summary>
    /// The tail every runtime kind shares once its factory is in hand: create the extension against
    /// a fresh context, cross-check the manifest declarations, publish the registrations and — for
    /// a session load — start the background services and tell the session the tool set changed.
    /// Deliberately free of rollback: the caller owns the try/catch so each runtime kind reports its
    /// own failure in its own words. A builtin door passes no context: its factory already lives in
    /// the default load context, so there is nothing collectible to build or unload.
    /// </summary>
    private async Task<LoadedExtension> ActivateAsync(
        ExtensionDescriptor descriptor,
        Func<IExtensionFactory> factory,
        AssemblyLoadContext? context,
        IExtensionSecrets secrets,
        bool startSession,
        bool notifyToolsChanged,
        int toolCountBefore,
        CancellationToken cancellationToken
    )
    {
        IExtensionFactory implementation = factory();
        var extensionContext = CreateExtensionContext(descriptor, secrets);
        _extensionContexts[descriptor.Id] = extensionContext;
        IExtension extension = implementation.Create(extensionContext);
        CrossCheckManifestTools(descriptor);
        CrossCheckManifestWorkspaceServices(descriptor);
        await CrossCheckManifestCommandsAsync(descriptor, cancellationToken).ConfigureAwait(false);
        _modelRegistry.RegisterDeclared(descriptor.Id, descriptor.Manifest.ModelProviders, secrets);
        var loaded = new LoadedExtension(descriptor.Id, extension, descriptor);
        _loaded[descriptor.Id] = loaded;
        if (context is { } collectible)
        {
            _contexts[descriptor.Id] = collectible;
        }
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
    /// The one teardown every failed load takes: everything the extension registered is dropped,
    /// its started background services are stopped, a remote extension's host is disposed, its
    /// context is marked disposed and the load context (when there is one) is unloaded. The stop is
    /// deliberately not cancellable: it never observes the load's token, so a load that ends on
    /// cancellation still tears down completely.
    /// </summary>
    private async Task RollbackAsync(string id, AssemblyLoadContext? context)
    {
        ExtensionContext? extensionContext = DropRegistrations(id);
        await _backgroundServices.DropAsync(id).ConfigureAwait(false);
        if (_remoteExtensions.Remove(id, out RemoteExtension? remote))
        {
            await remote.DisposeAsync().ConfigureAwait(false);
        }

        // Marked after the stop and the remote teardown, like Unload: their own logging paths still
        // make host calls.
        extensionContext?.MarkDisposed();
        context?.Unload();
    }

    /// <summary>
    /// Unregisters everything one extension may have contributed, including its hooks and its
    /// background-service registrations, and forgets that it ever loaded. Symmetric with
    /// <see cref="Unload"/> minus the parts that need an interactive session (the in-flight command
    /// drain; a failed load never dispatched one).
    /// </summary>
    private ExtensionContext? DropRegistrations(string id)
    {
        _loaded.Remove(id);
        _contexts.Remove(id);
        _tools.Unregister(id);
        _workspaceServices.Unregister(id);
        _hooks.Unregister(id);
        _modelRegistry.Unregister(id);
        _toolResultRenderers.Unregister(id);
        _statusWidgets.Unregister(id);
        _mcpServers.Unregister(id);
        _skillSources.Unregister(id);
        _commandSources.Unregister(id);
        _declaredNotProvidedCommands.Remove(id);
        DropSubscriptions(id);
        return _extensionContexts.Remove(id, out ExtensionContext? extensionContext)
            ? extensionContext
            : null;
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
            (extensionId, registration) =>
                _tools.Register(
                    extensionId,
                    registration,
                    ActivationRule(descriptor, registration.Name)
                ),
            _modelRegistry.RegisterProvider,
            _modelRegistry.RegisterModelMiddleware,
            _modelRegistry.RegisterVirtualModel,
            _skillSources.Register,
            _commandSources.Register,
            (extensionId, name, serviceType, createValue) =>
                RegisterWorkspaceService(descriptor, name, serviceType, createValue),
            (extensionId, name, serviceType, worktreeRoot, ct) =>
                _workspaceServices.Acquire(extensionId, name, serviceType, worktreeRoot, ct)
        );

    private IBackgroundService? FindService(string name) =>
        _backgroundServices.Services.TryGet(name, out IBackgroundService? service) ? service : null;

    private object? FindCoreService(string name) => _backgroundServices.Services.FindCore(name);

    /// <summary>
    /// The manifest's <c>toolActivation</c> rule for one declared tool: the explicit key wins, then
    /// the <c>*</c> blanket key, and no key at all is null — the composition reads null as always
    /// active. The rule comes from the parsed manifest, so it is stamped before any registration.
    /// </summary>
    private static ExtensionToolActivationRule? ActivationRule(
        ExtensionDescriptor descriptor,
        string localName
    )
    {
        IReadOnlyDictionary<string, ExtensionToolActivationRule> rules = descriptor
            .Manifest
            .ToolActivation;
        return rules.TryGetValue(localName, out ExtensionToolActivationRule? explicitKey)
                ? explicitKey
            : rules.TryGetValue("*", out ExtensionToolActivationRule? blanket) ? blanket
            : null;
    }

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
        InstalledPackageProvenance? provenance = Installed.ForExtension(descriptor.Directory!);
        string contentHash = Installed.BindTrustDigest(
            ExtensionTrustStore.ComputeContentHash(descriptor.Directory!),
            provenance
        );
        var store = new ExtensionTrustStore(_options);

        switch (store.Evaluate(repositoryIdentity, worktreePath, descriptor.Id, contentHash))
        {
            case ExtensionTrustDecision.Trusted:
                return;
            case ExtensionTrustDecision.NewWorktree:
                store.RecordTrusted(
                    repositoryIdentity,
                    worktreePath,
                    descriptor.Id,
                    contentHash,
                    DateTimeOffset.UtcNow
                );
                return;
            default:
                ProjectTrustDispatch trust = await _hooks
                    .RunProjectTrustAsync(
                        new ProjectTrustPayload(
                            descriptor.Id,
                            descriptor.Directory!,
                            repositoryIdentity,
                            worktreePath
                        ),
                        GlobalLoadedIds(),
                        cancellationToken
                    )
                    .ConfigureAwait(false);
                // A handler is a veto, never an approver: `deny` refuses the load, anything else
                // (an explicit allow included) leaves the decision with the prompt. Trust is a user
                // decision, and print and ACP reach the same prompt — which refuses — rather than a
                // handler's allow silently recording it.
                if (trust.Result is ProjectTrustResult.Deny deny)
                {
                    throw new ExtensionTrustDeniedException(
                        $"extension '{descriptor.Id}' was not trusted for repository '{repositoryIdentity}': {deny.Reason} It was not loaded."
                    );
                }

                // A handler is a veto, never an approval: Allow changes nothing here, and only the
                // person answering the prompt below records a decision. A global extension must
                // not be able to say yes on the user's behalf.
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
                    descriptor.Id,
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
