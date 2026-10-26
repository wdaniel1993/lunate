using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// How a NuGet-backed restore behaves. Everything is optional: the production host passes nothing
/// and gets the standard NuGet configuration chain and the global packages folder NuGet resolves.
/// Tests pass a local folder feed and a temporary packages folder, so no test touches the network.
/// </summary>
internal sealed class NuGetRestoreEngineOptions
{
    /// <summary>
    /// Feeds to read instead of the configuration chain. Empty means machine, user and project
    /// <c>nuget.config</c> in NuGet's order, rooted at the extension's directory.
    /// </summary>
    public IReadOnlyList<SingleFilePackageSource> Sources { get; init; } = [];

    /// <summary>Overrides the global packages folder; null reads <c>NUGET_PACKAGES</c>, then the config.</summary>
    public string? GlobalPackagesFolder { get; init; }

    /// <summary>
    /// Called immediately before each feed interaction ("versions …", "download …"). The restore
    /// tests count these calls to prove a stale or cached path makes none; null disables it.
    /// </summary>
    public Action<string>? OnFeedRequest { get; init; }
}
