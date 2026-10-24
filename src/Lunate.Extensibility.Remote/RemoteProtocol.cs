using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Remote;

/// <summary>The wire protocol constants: version, capabilities and reserved method names.</summary>
public static class RemoteProtocol
{
    /// <summary>
    /// The protocol version this host speaks. Version 2 added <c>command/invoke</c>; a version-1
    /// child stays readable for hooks and services and reports its declared commands as
    /// declared, not provided.
    /// </summary>
    public const int Version = 2;

    /// <summary>The oldest child version this host still reads (hooks and services only).</summary>
    public const int MinimumVersion = 1;

    /// <summary>The capabilities the host offers, echoed in <see cref="InitializeRequest"/>.</summary>
    public static IReadOnlyList<string> Capabilities { get; } =
    ["hook-requests", "hook-notifications", "service-call", "command-invoke", "cancel"];

    public const string InitializeMethod = "initialize";
    public const string ShutdownMethod = "shutdown";
    public const string ServiceCallMethod = "service-call";
    public const string CommandInvokeMethod = "command/invoke";
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
/// The child's <c>initialize</c> answer: the protocol version it speaks, the hooks and services it
/// implements, and whether it serves command invocations. A version the host does not read (or a
/// version above this host's) refuses the extension; an undeclared hook or service refuses it; a
/// declared one that is missing only warns. <see cref="Commands"/> absent reads as false.
/// </summary>
public sealed record InitializeResult(
    int ProtocolVersion,
    IReadOnlyList<string> Hooks,
    IReadOnlyList<string> Services
)
{
    /// <summary>
    /// True when the child can answer <c>command/invoke</c>. The host does not infer it from the
    /// protocol version: a child may speak version 2 and still serve hooks and services only, and
    /// its declared commands then read as declared, not provided.
    /// </summary>
    public bool Commands { get; init; }
}
