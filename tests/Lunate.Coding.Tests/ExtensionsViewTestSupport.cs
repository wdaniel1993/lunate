using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>Builders shared by the extensions-view formatter tests.</summary>
internal static class ExtensionsViewTestData
{
    internal static readonly Func<string, ExtensionCounts> NoCounts = _ => new ExtensionCounts(
        0,
        0,
        0,
        0
    );

    internal static ExtensionDescriptor Descriptor(
        string id,
        ExtensionScope scope = ExtensionScope.Global,
        string version = "0.1.0",
        IReadOnlyList<string>? tools = null,
        IReadOnlyList<string>? commands = null,
        IReadOnlyList<string>? hooks = null,
        IReadOnlyList<string>? services = null
    ) =>
        new(
            id,
            version,
            scope,
            "/store/" + id,
            new ExtensionManifest
            {
                Id = id,
                Version = version,
                ApiVersion = "^1.5.0",
                EntryAssembly = id + ".dll",
                Tools = tools ?? [],
                Commands = commands ?? [],
                Hooks = hooks ?? [],
                Services = services ?? [],
            }
        );

    internal static ExtensionLoadResult Loaded(string id, double milliseconds = 12) =>
        new(id, ExtensionLoadState.Loaded, TimeSpan.FromMilliseconds(milliseconds), null);

    internal static ExtensionLoadResult Denied(string id) =>
        new(id, ExtensionLoadState.TrustDenied, TimeSpan.FromMilliseconds(1), "was not trusted.");

    internal static ExtensionLoadResult Failed(string id, string message) =>
        new(id, ExtensionLoadState.Failed, TimeSpan.FromMilliseconds(1), message);
}
