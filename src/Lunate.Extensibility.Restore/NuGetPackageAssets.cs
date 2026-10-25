using NuGet.Frameworks;
using NuGet.Packaging;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// What a package contributes to a compile on one TFM and RID. NuGet's own layout decides it:
/// <c>ref/&lt;tfm&gt;</c> when the package ships reference assemblies, otherwise
/// <c>lib/&lt;tfm&gt;</c>; the nearest framework group is chosen the way restore chooses it; native
/// and RID-specific runtime assets are only the ones for this RID; analyzers and source generators
/// are recorded so the opt-in rule has something to act on, whether or not they run.
/// </summary>
internal static class NuGetPackageAssets
{
    private static readonly FrameworkReducer Reducer = new();

    /// <summary>One package's file list and asset lists, package-relative with forward slashes.</summary>
    internal sealed record Selection(
        IReadOnlyList<string> Files,
        IReadOnlyList<string> Compile,
        IReadOnlyList<string> Runtime,
        IReadOnlyList<string> Native,
        IReadOnlyList<string> Analyzers
    )
    {
        /// <summary>True when the package ships build-time code, so the header's opt-in decides.</summary>
        internal bool Generators => Analyzers.Count > 0;
    }

    /// <summary>
    /// Reads the installed nupkg. The archive is the record of what the SHA-512 covers, so the file
    /// list and the asset lists come from the same bytes the lock hashes.
    /// </summary>
    internal static Selection Read(string nupkgPath, NuGetFramework target, string rid)
    {
        using FileStream stream = File.OpenRead(nupkgPath);
        using var reader = new PackageArchiveReader(
            stream,
            DefaultFrameworkNameProvider.Instance,
            DefaultCompatibilityProvider.Instance
        );

        string[] files =
        [
            .. reader.GetFiles().Where(file => !file.EndsWith('/')).Order(StringComparer.Ordinal),
        ];

        List<string> lib = Nearest(target, [.. reader.GetLibItems()]);
        List<string> reference = Nearest(target, [.. reader.GetReferenceItems()]);

        return new Selection(
            files,
            // A package with ref/ compiles against those; its lib/ copy is the implementation.
            Compile: reference.Count > 0 ? reference : lib,
            Runtime: [.. lib, .. RuntimeSpecificLib(files, target, rid)],
            Native:
            [
                .. files.Where(file =>
                    file.StartsWith($"runtimes/{rid}/native/", StringComparison.Ordinal)
                ),
            ],
            Analyzers: Analyzers(files)
        );
    }

    /// <summary>
    /// The assemblies of the nearest framework group, ignoring placeholders (<c>_._</c>) and
    /// non-assemblies. A package with no group for this TFM contributes nothing here.
    /// </summary>
    private static List<string> Nearest(
        NuGetFramework target,
        IReadOnlyList<FrameworkSpecificGroup> groups
    )
    {
        if (groups.Count == 0)
        {
            return [];
        }

        NuGetFramework? nearest = Reducer.GetNearest(
            target,
            groups.Select(group => group.TargetFramework)
        );
        if (nearest is null)
        {
            return [];
        }

        FrameworkSpecificGroup? group = groups.FirstOrDefault(candidate =>
            candidate.TargetFramework.Equals(nearest)
        );
        return group is null
            ? []
            : [.. group.Items.Where(IsAssembly).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// <c>runtimes/&lt;rid&gt;/lib/&lt;tfm&gt;/</c> assemblies, nearest TFM first: the RID-specific
    /// runtime assets are what makes a native-backed package work on this platform.
    /// </summary>
    private static List<string> RuntimeSpecificLib(
        IReadOnlyList<string> files,
        NuGetFramework target,
        string rid
    )
    {
        string prefix = $"runtimes/{rid}/lib/";
        Dictionary<string, List<string>> byFramework = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            if (!file.StartsWith(prefix, StringComparison.Ordinal) || !IsAssembly(file))
            {
                continue;
            }

            string[] segments = file.Split('/');
            if (segments.Length < 5)
            {
                continue;
            }

            string folder = segments[3];
            if (!byFramework.TryGetValue(folder, out List<string>? assets))
            {
                assets = [];
                byFramework[folder] = assets;
            }

            assets.Add(file);
        }

        if (byFramework.Count == 0)
        {
            return [];
        }

        Dictionary<string, NuGetFramework> frameworks = new(StringComparer.OrdinalIgnoreCase);
        foreach (string folder in byFramework.Keys)
        {
            frameworks[folder] = NuGetFramework.ParseFolder(folder);
        }

        NuGetFramework? nearest = Reducer.GetNearest(target, frameworks.Values);
        if (nearest is null)
        {
            return [];
        }

        string? chosen = frameworks
            .Where(entry => entry.Value.Equals(nearest))
            .Select(entry => entry.Key)
            .FirstOrDefault();
        return chosen is null ? [] : [.. byFramework[chosen].Order(StringComparer.Ordinal)];
    }

    private static bool IsAssembly(string item) =>
        item.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        && !item.EndsWith("/_._", StringComparison.Ordinal);

    /// <summary>
    /// Analyzer and source-generator assemblies that apply to C#. NuGet's layout is
    /// <c>analyzers/dotnet/&lt;language&gt;/*.dll</c> or
    /// <c>analyzers/dotnet/&lt;roslyn-version&gt;/&lt;language&gt;/*.dll</c>; a file with no language
    /// folder is language-agnostic and counts for C# too.
    /// </summary>
    private static List<string> Analyzers(IReadOnlyList<string> files)
    {
        List<string> analyzers = [];
        foreach (string file in files)
        {
            if (!file.StartsWith("analyzers/dotnet/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string[] segments = file.Split('/');
            // analyzers / dotnet / [roslyn* /] [language /] file
            string? language = segments.Length >= 4 ? segments[^2] : null;
            if (language is null || string.Equals(language, "cs", StringComparison.Ordinal))
            {
                analyzers.Add(file);
            }
        }

        analyzers.Sort(StringComparer.Ordinal);
        return analyzers;
    }
}
