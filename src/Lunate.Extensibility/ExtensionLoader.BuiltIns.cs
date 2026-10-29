using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// The built-in path: the host composition folds its supplied list into discovery before any store
/// scan, and <see cref="ExtensionLoader.Load"/> runs its front door — the factory is in hand, no
/// collectible <c>AssemblyLoadContext</c> is created, and no scope, install-record or trust step
/// runs — while the shared activation tail is the directory door's.
/// </summary>
public sealed partial class ExtensionLoader
{
    /// <summary>
    /// Folds the host's built-in list into the descriptor set, before any store scan: a built-in
    /// is never shadowed. A list entry whose manifest does not itself declare <c>builtin: true</c>,
    /// one whose declared apiVersion this host refuses, or one claiming out-of-process provenance
    /// (an entry assembly or a runtime) is a discovery error naming the manifest (design §4) and
    /// the entry is not composed.
    /// </summary>
    private void FoldBuiltIns()
    {
        foreach (BuiltInExtension builtIn in _options.BuiltIns)
        {
            if (
                ExtensionApiGate.RefuseRange(builtIn.Manifest.Id, builtIn.Manifest.ApiVersion) is
                { } apiRefusal
            )
            {
                _discoveryErrors.Add(new ExtensionDiscoveryError(builtIn.ManifestPath, apiRefusal));
                continue;
            }

            if (!builtIn.Manifest.BuiltIn)
            {
                _discoveryErrors.Add(
                    new ExtensionDiscoveryError(
                        builtIn.ManifestPath,
                        $"the built-in manifest '{builtIn.ManifestPath}' does not declare \"builtin\": true; the host composition refused it."
                    )
                );
                continue;
            }

            string? provenanceField =
                builtIn.Manifest.EntryAssembly is not null ? "entryAssembly"
                : builtIn.Manifest.Runtime is not null ? "runtime"
                : null;
            if (provenanceField is { } field)
            {
                _discoveryErrors.Add(
                    new ExtensionDiscoveryError(
                        builtIn.ManifestPath,
                        $"{builtIn.ManifestPath}: field '{field}' of a built-in manifest is refused; a built-in runs in the default load context."
                    )
                );
                continue;
            }

            if (_descriptorsById.ContainsKey(builtIn.Manifest.Id))
            {
                continue;
            }

            var descriptor = new ExtensionDescriptor(
                builtIn.Manifest.Id,
                builtIn.Manifest.Version,
                ExtensionScope.Builtin,
                null,
                builtIn.Manifest
            );
            _descriptors.Add(descriptor);
            _descriptorsById[builtIn.Manifest.Id] = descriptor;
        }
    }

    /// <summary>
    /// The built-in front door: the factory runs against a normal extension context, the manifest
    /// cross-checks refuse like a discovered extension's, and one built-in failing rolls back only
    /// its own registrations — the session keeps the rest. There is nothing collectible to unload
    /// behind it: <paramref name="context"/> stays <see langword="null"/>.
    /// </summary>
    private async Task<LoadedExtension> LoadBuiltInAsync(
        ExtensionDescriptor descriptor,
        BuiltInExtension builtIn,
        bool startSession,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        int toolCountBefore = _tools.List().Count;
        try
        {
            return await ActivateAsync(
                    descriptor,
                    builtIn.Factory,
                    context: null,
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
            await RollbackAsync(descriptor.Id, context: null).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(descriptor.Id, context: null).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAsync(descriptor.Id, context: null).ConfigureAwait(false);
            throw new ExtensionLoadException(
                FormattableString.Invariant(
                    $"extension '{descriptor.Id}': the built-in factory ('{builtIn.ManifestPath}') failed: {exception.Message}"
                ),
                exception
            );
        }
    }
}
