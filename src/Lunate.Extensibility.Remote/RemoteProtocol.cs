using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>The wire protocol constants: version, capabilities and reserved method names.</summary>
public static class RemoteProtocol
{
    /// <summary>The protocol version this host speaks; a mismatch refuses the extension.</summary>
    public const int Version = 1;

    /// <summary>The capabilities the host offers, echoed in <see cref="InitializeRequest"/>.</summary>
    public static IReadOnlyList<string> Capabilities { get; } =
    ["hook-requests", "hook-notifications", "service-call", "cancel"];

    public const string InitializeMethod = "initialize";
    public const string ShutdownMethod = "shutdown";
    public const string ServiceCallMethod = "service-call";
    public const string CancelMethod = "$/cancelRequest";

    /// <summary>The child → host service request method for one service and method.</summary>
    public static string ServiceMethod(string service, string method) =>
        $"service/{service}/{method}";
}

/// <summary>
/// The <c>initialize</c> request (host → child, the first call): the protocol version and
/// capabilities, the manifest echo, the workspace and the manifest's declared hooks and services.
/// </summary>
public sealed record InitializeRequest(
    int ProtocolVersion,
    IReadOnlyList<string> Capabilities,
    JsonElement Manifest,
    WorkspaceInfo Workspace,
    IReadOnlyList<string> Hooks,
    IReadOnlyList<string> Services
);

/// <summary>
/// The child's <c>initialize</c> answer: the protocol version it speaks and the hooks and services
/// it implements. A version mismatch refuses the extension; an undeclared hook or service refuses
/// it; a declared one that is missing only warns.
/// </summary>
public sealed record InitializeResult(
    int ProtocolVersion,
    IReadOnlyList<string> Hooks,
    IReadOnlyList<string> Services
);
