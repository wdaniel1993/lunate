using System.Reflection;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    public async Task<LoadedExtension> Load(
        string id,
        string workingDirectory,
        string repositoryIdentity,
        IExtensionTrustPrompt prompt,
        CancellationToken cancellationToken = default
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

        if (descriptor.Scope == ExtensionScope.Project)
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

        string entryAssembly = descriptor.Manifest.EntryAssembly;
        string entryPath = Path.Combine(descriptor.Directory, entryAssembly);
        if (!File.Exists(entryPath))
        {
            throw new ExtensionLoadException(
                $"extension '{id}': entry assembly '{entryAssembly}' was not found in '{descriptor.Directory}'."
            );
        }

        cancellationToken.ThrowIfCancellationRequested();

        var context = new ExtensionLoadContext(descriptor.Id, descriptor.Directory);
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(entryPath);
            IExtensionFactory factory = FindFactory(assembly, descriptor);
            var extensionContext = new ExtensionContext(
                descriptor.Id,
                ExtensionSettingsStore.Load(_options, descriptor),
                ExtensionSecretsStore.Load(_options, descriptor.Id),
                _options.Log ?? NullExtensionLog.Instance,
                _hooks.Register,
                _backgroundServices.Register,
                SubscribeFileChanged,
                FindService,
                FindCoreService
            );
            IExtension extension = factory.Create(extensionContext);
            _modelProviders.Register(descriptor.Id, descriptor.Manifest.ModelProviders);
            var loaded = new LoadedExtension(descriptor.Id, extension, descriptor);
            _loaded[descriptor.Id] = loaded;
            _contexts[descriptor.Id] = context;
            await StartSessionAsync(workingDirectory, repositoryIdentity, cancellationToken)
                .ConfigureAwait(false);
            if (!_sessionEnded)
            {
                await _backgroundServices.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            return loaded;
        }
        catch (ExtensionLoadException)
        {
            await _backgroundServices.DropAsync(descriptor.Id).ConfigureAwait(false);
            _modelProviders.Unregister(descriptor.Id);
            DropSubscriptions(descriptor.Id);
            context.Unload();
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await _backgroundServices.DropAsync(descriptor.Id).ConfigureAwait(false);
            _modelProviders.Unregister(descriptor.Id);
            DropSubscriptions(descriptor.Id);
            context.Unload();
            throw WrapLoadFailure(descriptor, exception);
        }
    }

    private IBackgroundService? FindService(string name) =>
        _backgroundServices.Services.TryGet(name, out IBackgroundService? service) ? service : null;

    private object? FindCoreService(string name) => _backgroundServices.Services.FindCore(name);

    private async Task EnsureTrustedAsync(
        ExtensionDescriptor descriptor,
        string workingDirectory,
        string repositoryIdentity,
        IExtensionTrustPrompt prompt,
        CancellationToken cancellationToken
    )
    {
        string worktreePath = Path.GetFullPath(workingDirectory);
        string contentHash = ExtensionTrustStore.ComputeContentHash(descriptor.Directory);
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

                bool approved = await prompt
                    .ApproveAsync(descriptor, cancellationToken)
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
