using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

public sealed partial class RemoteExtension
{
    /// <summary>
    /// Host → child: calls one method on a service the child registered, over
    /// <c>service-call</c> with the <see cref="ServiceCallEnvelope"/>. The call is bounded by the
    /// manifest's <c>callTimeout</c> (or the default) and the caller's cancellation.
    /// </summary>
    public async ValueTask<JsonElement?> InvokeServiceAsync(
        string service,
        string method,
        JsonElement? arguments = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (!_serviceProxies.ContainsKey(service))
        {
            throw new RemoteExtensionException(
                $"extension '{Id}' does not declare service '{service}'; the call was not sent."
            );
        }

        if (_disabledServices.Contains(service))
        {
            throw new RemoteExtensionException(
                $"extension '{Id}': service '{service}' is declared in the manifest but the child does not implement it; the call was not sent."
            );
        }

        RemoteWire wire = await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        JsonElement parameters = JsonSerializer.SerializeToElement(
            new ServiceCallEnvelope(service, method, arguments),
            WireJson.Options
        );
        return await wire.CallAsync(
                RemoteProtocol.ServiceCallMethod,
                parameters,
                CallTimeout,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>The child's <c>service/&lt;name&gt;/&lt;method&gt;</c> requests: host-side dispatch.</summary>
    private async ValueTask<JsonElement?> OnRequestAsync(
        string method,
        JsonElement? parameters,
        CancellationToken cancellationToken
    )
    {
        if (!method.StartsWith("service/", StringComparison.Ordinal))
        {
            throw new RemoteCallException(
                -32601,
                $"method '{method}' is not supported by the host; the v1 surface is initialize, hook/<name>, service/<name>/<method>, service-call and shutdown"
            );
        }

        // The route is service/<name>/<method>; a core name contains its own slash
        // (core/workspace), so the method is the last segment and the name is everything between.
        string route = method["service/".Length..];
        int separator = route.LastIndexOf('/');
        if (separator <= 0 || separator == route.Length - 1)
        {
            throw new RemoteCallException(
                -32602,
                $"malformed service method '{method}'; expected service/<name>/<method>"
            );
        }

        return await InvokeHostServiceAsync(
                route[..separator],
                route[(separator + 1)..],
                parameters,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async ValueTask<JsonElement?> InvokeHostServiceAsync(
        string name,
        string method,
        JsonElement? arguments,
        CancellationToken cancellationToken
    )
    {
        if (string.Equals(name, "core/workspace", StringComparison.Ordinal))
        {
            return string.Equals(method, "get", StringComparison.Ordinal)
                ? JsonSerializer.SerializeToElement(_options.Workspace, WireJson.Options)
                : throw new RemoteCallException(
                    -32601,
                    "service 'core/workspace' supports only the 'get' method"
                );
        }

        IJsonService? service =
            _options.FindJsonService is { } find ? find(name)
            : _options.Context.TryGetService(name, out IBackgroundService? registered)
            && registered is IJsonService json
                ? json
            : null;
        if (service is null)
        {
            throw new RemoteCallException(
                -32601,
                $"service '{name}' is not registered as JSON-callable; a service must implement IJsonService (and IBackgroundService) to answer remote calls"
            );
        }

        return await service
            .InvokeAsync(method, arguments, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The host-side handle for a service the child registered: <see cref="IBackgroundService"/>
    /// for the registry (the process lifecycle is the extension's own) and
    /// <see cref="IJsonService"/> for method calls — async-only by construction.
    /// </summary>
    private sealed class RemoteServiceProxy(RemoteExtension extension, string name)
        : IBackgroundService,
            IJsonService
    {
        public string Name { get; } = name;

        /// <summary>The process starts lazily on first use, never with the session-start sweep.</summary>
        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        /// <summary>The session-end shutdown owns the process; this stops nothing.</summary>
        public ValueTask StopAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<JsonElement?> InvokeAsync(
            string method,
            JsonElement? arguments,
            CancellationToken cancellationToken
        ) => extension.InvokeServiceAsync(Name, method, arguments, cancellationToken);
    }
}
