using System.Reflection;
using System.Runtime.Loader;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    private readonly List<SingleFileExtension> _singleFile = [];

    /// <summary>The build key each loaded single-file extension was loaded from (the swap's record).</summary>
    private readonly Dictionary<string, string> _singleFileKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Per loaded extension, what the load ignored because the host owns it: a locked package's id for
    /// a single-file build, or the extension's directory for a packaged one. The view reads it so the
    /// warning is a line where the user looks, not only a log line they may have missed.
    /// </summary>
    private readonly Dictionary<string, IgnoredHostCopy> _ignoredCopies = new(
        StringComparer.Ordinal
    );

    /// <summary>
    /// The single-file candidates discovered beside the packaged extensions, global before project
    /// and each group in file-name order. A refused file stays in the list — <c>/extensions</c>
    /// shows why instead of hiding it — and its id never displaces a packaged extension.
    /// </summary>
    public IReadOnlyList<SingleFileExtension> SingleFileExtensions => [.. _singleFile];

    /// <summary>
    /// Loads the assembly of one discovered single-file extension into its own collectible context:
    /// the build the cache points at for the file's current bytes, with its locked packages resolved
    /// from the packages folder the compile used. The file is re-read first, so a load never acts on
    /// a snapshot taken before an edit, and it is refused — before anything is restored, compiled or
    /// prompted — when it is not trusted, not built, or built against a newer API.
    /// <para>
    /// Trust is not asked here and never will be: this is the path a model-initiated reload reaches,
    /// and the trust question is the user's. A file that needs a decision is refused with the reason
    /// and the surface that can ask for it.
    /// </para>
    /// </summary>
    public async Task<LoadedExtension> LoadSingleFileAsync(
        string id,
        CancellationToken cancellationToken = default,
        bool startSession = true,
        bool notifyToolsChanged = true
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        SingleFileExtension snapshot =
            _singleFile.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.Ordinal)
            )
            ?? throw new ExtensionLoadException(
                $"single-file extension '{id}' is not discovered; discovered: {DiscoveredSingleFileIds()}."
            );

        if (_loaded.ContainsKey(id))
        {
            throw new ExtensionLoadException(
                $"extension '{id}' is already loaded; unload it before loading it again."
            );
        }

        // Fresh read: a reload builds and then loads, so the snapshot discovery took at session
        // start may name a build that has since been replaced.
        SingleFileExtension candidate = new SingleFileExtensionReader(_options).ReadOne(
            snapshot.Path,
            snapshot.Scope
        );

        if (!candidate.Header.IsValid || candidate.Id is null)
        {
            throw new ExtensionLoadException(
                $"single-file extension '{id}' at '{candidate.Path}' is invalid ({candidate.Reason ?? "no reason recorded"}); it was not loaded."
            );
        }

        if (!string.Equals(candidate.Id, id, StringComparison.Ordinal))
        {
            throw new ExtensionLoadException(
                $"single-file extension '{id}' now declares '{candidate.Id}' in '{candidate.Path}'; it was not loaded."
            );
        }

        switch (Trust(candidate))
        {
            // A build record in the cache is proof the file was built, never proof that these bytes
            // were approved: a warm cache must not stand in for a trust decision.
            case SingleFileExtensionTrust.Untrusted:
                throw new ExtensionTrustDeniedException(
                    $"single-file extension '{id}' at '{candidate.Path}' has no trust decision for its current bytes; approve it in an interactive session before it is loaded. It was not loaded."
                );
            case SingleFileExtensionTrust.Unreadable:
                throw new ExtensionTrustDeniedException(
                    $"single-file extension '{id}' at '{candidate.Path}': the trust record for this file is unreadable; remove its entry from trust.json and trust the file again. It was not loaded."
                );
            case SingleFileExtensionTrust.Stale:
                throw new ExtensionTrustDeniedException(
                    $"single-file extension '{id}' at '{candidate.Path}' is not trusted for its current bytes; approve it in an interactive session before it is loaded. It was not loaded."
                );
        }

        if (candidate.BuildKey is null || candidate.AssemblyPath is null)
        {
            throw new ExtensionLoadException(
                $"single-file extension '{id}' at '{candidate.Path}' is not built for its current bytes; run `lunate extensions build {id}` first."
            );
        }

        SingleFileBuild build =
            new SingleFileBuildCache(_options.StorePath).Find(id, candidate.BuildKey)
            ?? throw new ExtensionLoadException(
                $"single-file extension '{id}': the build '{candidate.BuildKey}' recorded for '{candidate.Path}' could not be read at '{candidate.AssemblyPath}'; build it again."
            );

        // The gate the build record makes possible: the apiVersion the compile ran against is
        // recorded in the build, so a build made by a newer host is refused here rather than at the
        // first call that reaches a member this host does not have.
        if (ExtensionApiGate.Refuse(id, build.Manifest.ApiVersion) is { } refusal)
        {
            throw new ExtensionLoadException(refusal);
        }

        var descriptor = Describe(candidate, build);
        var context = new SingleFileExtensionLoadContext(
            id,
            build,
            _options.GlobalPackagesFolder ?? SingleFilePackages.DefaultFolder()
        );
        int toolCountBefore = _tools.List().Count;
        try
        {
            if (context.ShadowedPackages.Count > 0)
            {
                (_options.Log ?? NullExtensionLog.Instance).Warn(
                    HostOwnedAssemblies.Describe(
                        id,
                        context.ShadowedNames,
                        PackagesText(context.ShadowedPackages)
                    )
                );
            }

            Assembly assembly = context.LoadFromAssemblyPath(build.AssemblyPath);
            LoadedExtension loaded = await ActivateAsync(
                    descriptor,
                    () => FindFactory(assembly, descriptor),
                    context,
                    ExtensionSecretsStore.Load(_options, id),
                    startSession,
                    notifyToolsChanged,
                    toolCountBefore,
                    cancellationToken
                )
                .ConfigureAwait(false);
            _singleFileKeys[id] = build.Key;
            if (context.ShadowedPackages.Count > 0)
            {
                _ignoredCopies[id] = new IgnoredHostCopy(
                    context.ShadowedNames,
                    context.ShadowedPackages
                );
            }

            return loaded;
        }
        catch (ExtensionLoadException)
        {
            await RollbackAsync(id, context).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(id, context).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollbackAsync(id, context).ConfigureAwait(false);
            throw WrapLoadFailure(descriptor, exception);
        }
    }

    /// <summary>
    /// The descriptor a single-file load registers through: the header's declarations, mapped onto
    /// the same shape a manifest has, so the tool and command cross-checks and the registries see one
    /// kind of extension. The directory is the build's, because that is where the assembly and its
    /// companions live.
    /// </summary>
    private static ExtensionDescriptor Describe(
        SingleFileExtension candidate,
        SingleFileBuild build
    )
    {
        ExtensionHeader header = candidate.Header;
        string version = candidate.Version ?? "0.0.0";
        return new ExtensionDescriptor(
            candidate.Id!,
            version,
            candidate.Scope,
            build.BuildPath,
            new ExtensionManifest
            {
                Id = candidate.Id!,
                Version = version,
                ApiVersion = ApiVersionRange(ExtensionApi.Current),
                EntryAssembly = build.AssemblyName,
                Tools = header.Tools,
                Commands = header.Commands,
                Hooks = header.Hooks,
                Services = header.Services,
                Capabilities = header.Capabilities,
                // The header declares provider ids; the descriptors are the code's to register, so
                // nothing is declared here that a load would then report as unfulfilled.
                ModelProviders = [],
                WriteVerifiers = header.WriteVerifiers,
            }
        );
    }

    /// <summary>
    /// The manifest form of an exact contract version. A descriptor is not re-gated (the build record
    /// already was), so this only has to be a range the manifest parser would accept.
    /// </summary>
    private static string ApiVersionRange(string version) => "^" + version;

    /// <summary>The owners a shadowed-copy warning names: one package, or an ordinal list of them.</summary>
    private static string PackagesText(IReadOnlyList<string> packages) =>
        packages.Count == 1
            ? FormattableString.Invariant($"package '{packages[0]}'")
            : "packages " + string.Join(", ", packages.Select(package => $"'{package}'"));

    private SingleFileExtensionTrust Trust(SingleFileExtension candidate)
    {
        try
        {
            return new ExtensionTrustStore(_options).EvaluateSingleFileTrust(
                candidate.Path,
                candidate.Digest
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SingleFileExtensionTrust.Unreadable;
        }
    }

    private string DiscoveredSingleFileIds()
    {
        string[] ids = [.. _singleFile.Select(candidate => candidate.Id).OfType<string>()];
        return ids.Length == 0 ? "none" : string.Join(", ", ids);
    }

    /// <summary>
    /// Reads both extensions roots for <c>*.cs</c> candidates, warns about what their headers
    /// ignored, then settles the identities: a packaged extension's id wins and the file is
    /// refused, and two files that declare one id are both refused (neither is more trustworthy
    /// than the other). Nothing is restored, compiled or trusted here.
    /// </summary>
    private void DiscoverSingleFileExtensions(
        string globalRoot,
        string projectRoot,
        List<ExtensionDiscoveryError> errors,
        bool warnAboutHeaders = true
    )
    {
        var reader = new SingleFileExtensionReader(_options);
        _singleFile.AddRange(reader.Read(globalRoot, ExtensionScope.Global, errors));
        _singleFile.AddRange(reader.Read(projectRoot, ExtensionScope.Project, errors));

        if (warnAboutHeaders)
        {
            // Discovery says each ignored directive once; a refresh (a listing, a reload) reads the
            // same rows without repeating the warning on every render.
            IExtensionLog log = _options.Log ?? NullExtensionLog.Instance;
            foreach (SingleFileExtension candidate in _singleFile)
            {
                foreach (ExtensionHeaderIssue warning in candidate.Header.Warnings)
                {
                    log.Warn($"{candidate.Path}: {warning}");
                }
            }
        }

        SettleSingleFileIdentities(errors);
    }

    /// <summary>
    /// Refuses the candidates whose declared id is already taken. A refusal is a discovery error
    /// naming both paths and an <see cref="SingleFileExtensionState.Invalid"/> candidate, so the
    /// file is listed with the reason rather than dropped.
    /// </summary>
    private void SettleSingleFileIdentities(List<ExtensionDiscoveryError> errors)
    {
        List<int> accepted =
        [
            .. Enumerable
                .Range(0, _singleFile.Count)
                .Where(index => _singleFile[index].Header.IsValid),
        ];

        foreach (int index in accepted)
        {
            SingleFileExtension candidate = _singleFile[index];
            if (_descriptorsById.TryGetValue(candidate.Id!, out ExtensionDescriptor? packaged))
            {
                Refuse(
                    index,
                    $"declared more than once: '{packaged.Directory}' and '{candidate.Path}'",
                    errors
                );
            }
        }

        IEnumerable<IGrouping<string, int>> duplicates = accepted
            .Where(index => _singleFile[index].State != SingleFileExtensionState.Invalid)
            .GroupBy(index => _singleFile[index].Id!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);
        foreach (IGrouping<string, int> group in duplicates)
        {
            string paths = string.Join(
                " and ",
                group.Select(index => $"'{_singleFile[index].Path}'")
            );
            foreach (int index in group)
            {
                Refuse(index, "declared more than once: " + paths, errors);
            }
        }
    }

    private void Refuse(int index, string reason, List<ExtensionDiscoveryError> errors)
    {
        SingleFileExtension candidate = _singleFile[index];
        _singleFile[index] = candidate with
        {
            State = SingleFileExtensionState.Invalid,
            Reason = reason,
        };
        errors.Add(
            new ExtensionDiscoveryError(candidate.Path, $"extension '{candidate.Id}' is {reason}.")
        );
    }
}
