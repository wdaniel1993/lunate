using Lunate.Extensibility;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// How a package install behaves. Everything is optional: the host passes nothing and gets the NuGet
/// configuration chain from the working directory and the packages folder NuGet resolves. Tests pass
/// a local folder feed, a temporary packages folder and a fixed clock, so no test touches the network
/// and none depends on the time.
/// </summary>
public sealed class PackageInstallOptions
{
    /// <summary>
    /// Feeds to read instead of the configuration chain. Empty means machine, user and project
    /// <c>nuget.config</c>, rooted at <see cref="SettingsRoot"/>.
    /// </summary>
    public IReadOnlyList<SingleFilePackageSource> Sources { get; init; } = [];

    /// <summary>Overrides the global packages folder; null reads the config, <c>NUGET_PACKAGES</c>, then the profile.</summary>
    public string? GlobalPackagesFolder { get; init; }

    /// <summary>The directory the configuration chain is rooted at; null uses the process working directory.</summary>
    public string? SettingsRoot { get; init; }

    /// <summary>
    /// Called immediately before each feed interaction (design D3). A test counts these calls to
    /// prove an offline install makes none; null disables it.
    /// </summary>
    public Action<string>? OnFeedRequest { get; init; }

    /// <summary>
    /// True resolves from the global packages folder only and makes no feed request at all — not
    /// even a version lookup (design D11, spec: "Failure, offline and private feeds").
    /// </summary>
    public bool Offline { get; init; }

    /// <summary>The clock behind the record's install time; null uses the system clock.</summary>
    public TimeProvider? Clock { get; init; }
}
