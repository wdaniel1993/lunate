using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>
/// Everything the remote extension adapter needs: the manifest and its directory, the workspace
/// the session runs in, the core process capability, and the extension context it registers its
/// proxies through.
/// </summary>
public sealed class RemoteExtensionOptions
{
    public required ExtensionManifest Manifest { get; init; }

    /// <summary>The extension's directory; relative commands, args and the default cwd resolve here.</summary>
    public required string Directory { get; init; }

    /// <summary>The session's workspace, carried on every forwarded call.</summary>
    public required WorkspaceInfo Workspace { get; init; }

    /// <summary>The core process capability (<c>core/process</c>) the child starts through.</summary>
    public required IProcessRunner ProcessRunner { get; init; }

    /// <summary>
    /// The extension context: proxies register through it and the log/settings/secrets live
    /// there. The registered services are found through the same context for child → host calls.
    /// </summary>
    public required IExtensionContext Context { get; init; }

    /// <summary>The host's one-notice callback (crash and refusal notices); null logs only.</summary>
    public Action<string>? Notify { get; init; }

    /// <summary>
    /// Looks up a JSON-callable service by name for child → host calls; null uses the context's
    /// own registry lookup. <c>core/workspace/get</c> is served by the adapter itself.
    /// </summary>
    public Func<string, IJsonService?>? FindJsonService { get; init; }

    /// <summary>
    /// Removes one proxy from the hook runner (the loader wires this to the real runner). The
    /// handshake uses it to drop a declared-but-unimplemented hook exactly like the in-process
    /// "declared but not registered" rule; null leaves the proxy installed.
    /// </summary>
    public Action<IHookHandler>? UnregisterHook { get; init; }

    /// <summary>The window a starting child has for the <c>initialize</c> handshake.</summary>
    public TimeSpan InitializeTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The grace between the shutdown request and the tree kill.</summary>
    public TimeSpan ShutdownGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The default per-call timeout; the manifest's <c>callTimeout</c> overrides it.</summary>
    public TimeSpan DefaultCallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The stderr byte cap pumped into the extension log.</summary>
    public int MaxLogBytes { get; init; } = 64 * 1024;

    /// <summary>The wire's per-line byte cap (the protocol error bound).</summary>
    public int MaxLineBytes { get; init; } = 16 * 1024 * 1024;
}
