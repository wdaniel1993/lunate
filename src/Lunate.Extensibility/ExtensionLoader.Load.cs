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
                new EmptyExtensionSettings(),
                new EmptyExtensionSecrets(),
                _options.Log ?? NullExtensionLog.Instance
            );
            IExtension extension = factory.Create(extensionContext);
            var loaded = new LoadedExtension(descriptor.Id, extension, descriptor);
            _loaded[descriptor.Id] = loaded;
            _contexts[descriptor.Id] = context;
            return loaded;
        }
        catch (ExtensionLoadException)
        {
            context.Unload();
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            context.Unload();
            throw WrapLoadFailure(descriptor, exception);
        }
    }

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
