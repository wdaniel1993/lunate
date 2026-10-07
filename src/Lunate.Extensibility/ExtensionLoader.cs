using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class ExtensionLoader
{
    private readonly ExtensionHostOptions _options;
    private readonly List<ExtensionDescriptor> _descriptors = [];
    private readonly Dictionary<string, ExtensionDescriptor> _descriptorsById = new(
        StringComparer.Ordinal
    );
    private readonly List<ExtensionDiscoveryError> _discoveryErrors = [];
    private readonly Dictionary<string, LoadedExtension> _loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExtensionLoadContext> _contexts = new(
        StringComparer.Ordinal
    );
    private readonly HookRunner _hooks;
    private bool _sessionStarted;
    private bool _sessionEnded;
    private SessionStartedPayload? _session;

    public ExtensionLoader(ExtensionHostOptions options)
        : this(options, null) { }

    public ExtensionLoader(ExtensionHostOptions options, HookRunner? hooks)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _hooks = hooks ?? new HookRunner(null, options.Log);
    }

    /// <summary>The hook runner handlers registered through extension contexts dispatch to.</summary>
    public HookRunner Hooks => _hooks;

    public IReadOnlyList<ExtensionDescriptor> Descriptors => [.. _descriptors];

    public IReadOnlyList<ExtensionDiscoveryError> DiscoveryErrors => [.. _discoveryErrors];

    public IReadOnlyList<ExtensionDescriptor> Discover(
        string workingDirectory,
        string repositoryIdentity
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(repositoryIdentity);

        _descriptors.Clear();
        _descriptorsById.Clear();
        _discoveryErrors.Clear();

        ScanDirectory(_options.ExtensionsPath, ExtensionScope.Global);
        ScanDirectory(
            Path.Combine(Path.GetFullPath(workingDirectory), ".lunate", "extensions"),
            ExtensionScope.Project
        );

        return Descriptors;
    }

    public void Unload(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!_loaded.Remove(id))
        {
            return;
        }

        _hooks.Unregister(id);
        if (_contexts.Remove(id, out ExtensionLoadContext? context))
        {
            context.Unload();
        }
    }

    /// <summary>Runs the session-ending hooks once; later calls and calls before a start do nothing.</summary>
    public async ValueTask EndSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_sessionEnded || !_sessionStarted || _session is not { } session)
        {
            return;
        }

        _sessionEnded = true;
        await _hooks
            .RunSessionEndingAsync(
                new SessionEndingPayload(session.WorkingDirectory, session.RepositoryIdentity),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async ValueTask StartSessionAsync(
        string workingDirectory,
        string repositoryIdentity,
        CancellationToken cancellationToken
    )
    {
        if (_sessionStarted)
        {
            return;
        }

        _sessionStarted = true;
        _session = new SessionStartedPayload(workingDirectory, repositoryIdentity);
        await _hooks.RunSessionStartedAsync(_session, cancellationToken).ConfigureAwait(false);
    }

    private static string DiscoveredIds(IReadOnlyList<ExtensionDescriptor> descriptors) =>
        descriptors.Count == 0
            ? "none"
            : string.Join(", ", descriptors.Select(descriptor => descriptor.Id));

    private void ScanDirectory(string root, ExtensionScope scope)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        IEnumerable<string> directories = Directory
            .EnumerateDirectories(root)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        foreach (string directory in directories)
        {
            string name = Path.GetFileName(directory);
            if (name.StartsWith('.'))
            {
                continue;
            }

            string manifestPath = Path.Combine(directory, "extension.json");
            if (!File.Exists(manifestPath))
            {
                _discoveryErrors.Add(
                    new ExtensionDiscoveryError(
                        directory,
                        $"extension directory '{directory}' has no extension.json manifest."
                    )
                );
                continue;
            }

            ExtensionManifest manifest;
            try
            {
                manifest = ExtensionManifest.Parse(File.ReadAllText(manifestPath), manifestPath);
            }
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or InvalidDataException
                )
            {
                string message =
                    exception is InvalidDataException
                        ? exception.Message
                        : $"extension manifest '{manifestPath}' could not be read: {exception.Message}";
                _discoveryErrors.Add(new ExtensionDiscoveryError(manifestPath, message));
                continue;
            }

            if (!ExtensionApi.IsCompatible(manifest.ApiVersion))
            {
                _discoveryErrors.Add(
                    new ExtensionDiscoveryError(
                        manifestPath,
                        $"extension '{manifest.Id}' requires apiVersion '{manifest.ApiVersion}' but the current API version is {ExtensionApi.Current}."
                    )
                );
                continue;
            }

            if (_descriptorsById.TryGetValue(manifest.Id, out ExtensionDescriptor? existing))
            {
                _discoveryErrors.Add(
                    new ExtensionDiscoveryError(
                        manifestPath,
                        $"extension '{manifest.Id}' is declared more than once: '{existing.Directory}' and '{directory}'."
                    )
                );
                continue;
            }

            var descriptor = new ExtensionDescriptor(
                manifest.Id,
                manifest.Version,
                scope,
                directory,
                manifest
            );
            _descriptors.Add(descriptor);
            _descriptorsById[manifest.Id] = descriptor;
        }
    }
}
