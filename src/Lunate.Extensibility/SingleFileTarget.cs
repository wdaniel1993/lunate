using System.Globalization;
using System.Runtime.InteropServices;

namespace Lunate.Extensibility;

/// <summary>
/// The toolset a single-file extension is restored and compiled for: the target framework the host
/// runs, its RID and its exact runtime version. The cache key and the lock both name it, because a
/// different toolset can select different assets and a different reference pack.
/// </summary>
public sealed record SingleFileTarget
{
    public required string Tfm { get; init; }

    public required string Rid { get; init; }

    public required string Runtime { get; init; }

    /// <summary>The toolset of this process: its framework, its RID and its runtime version.</summary>
    public static SingleFileTarget Current() =>
        Of(Environment.Version, RuntimeInformation.RuntimeIdentifier);

    /// <summary>
    /// The toolset for a given runtime version and RID. The runtime is
    /// <c>major.minor.build</c> — <see cref="Version.Revision"/> is not part of a .NET release
    /// version, and a ref pack is published per release version.
    /// </summary>
    public static SingleFileTarget Of(Version runtime, string rid)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(rid);

        return new SingleFileTarget
        {
            Tfm = string.Create(
                CultureInfo.InvariantCulture,
                $"net{runtime.Major}.{runtime.Minor}"
            ),
            Rid = rid,
            Runtime = string.Create(
                CultureInfo.InvariantCulture,
                $"{runtime.Major}.{runtime.Minor}.{runtime.Build}"
            ),
        };
    }
}
