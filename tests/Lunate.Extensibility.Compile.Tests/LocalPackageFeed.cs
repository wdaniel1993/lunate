using System.Text;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace Lunate.Extensibility.Compile.Tests;

/// <summary>
/// A flat local folder feed, so a published binary can be held to the whole restore path without a
/// network: real nupkgs, a real nuspec, and — for the reference pack — the machine's own reference
/// assemblies. Every package is a plain file in one directory, which is also what makes "the second
/// run did not touch the feed" observable as a file-system fact.
/// </summary>
internal sealed class LocalPackageFeed
{
    private readonly List<string> _temporary = [];

    internal LocalPackageFeed(string root)
    {
        FeedDirectory = Path.Combine(root, "feed");
        Directory.CreateDirectory(FeedDirectory);
    }

    internal string FeedDirectory { get; }

    /// <summary>One package with the given package-relative files.</summary>
    internal LocalPackageFeed Add(
        string id,
        string version,
        IReadOnlyDictionary<string, byte[]> files
    )
    {
        var builder = new PackageBuilder
        {
            Id = id,
            Version = NuGetVersion.Parse(version),
            Description = id,
        };
        builder.Authors.Add("lunate-tests");

        foreach ((string target, byte[] content) in files)
        {
            string source = Path.Combine(
                Path.GetTempPath(),
                string.Concat("lunate-pkg-", Guid.NewGuid().ToString("N"), ".bin")
            );
            File.WriteAllBytes(source, content);
            _temporary.Add(source);
            builder.Files.Add(new PhysicalPackageFile { SourcePath = source, TargetPath = target });
        }

        using FileStream stream = File.Create(
            System.IO.Path.Combine(
                FeedDirectory,
                FormattableString.Invariant($"{id}.{version}.nupkg")
            )
        );
        builder.Save(stream);
        return this;
    }

    /// <summary>
    /// The reference pack as a nupkg: the given <c>ref/&lt;tfm&gt;</c> directory's assemblies, so a
    /// restore out of this feed yields a pack a real compile can use.
    /// </summary>
    internal LocalPackageFeed AddReferencePack(string version, string refDirectory, string tfm)
    {
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(refDirectory, "*.dll"))
        {
            files[FormattableString.Invariant($"ref/{tfm}/{Path.GetFileName(file)}")] =
                File.ReadAllBytes(file);
        }

        Assert.NotEmpty(files);
        return Add("Microsoft.NETCore.App.Ref", version, files);
    }

    /// <summary>A <c>nuget.config</c> beside the extension, pointing at this feed only.</summary>
    internal string WriteConfig(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "nuget.config");
        File.WriteAllText(
            path,
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="local" value="{FeedDirectory}" />
              </packageSources>
            </configuration>
            """,
            new UTF8Encoding(false)
        );
        return path;
    }

    /// <summary>
    /// Every file and directory under the feed with what a read would disturb: a size and a write
    /// time for a file, a write time for a directory. Comparing two snapshots is how the second run
    /// is proved to have opened nothing here.
    /// </summary>
    internal string Snapshot()
    {
        StringBuilder text = new();
        foreach (
            string directory in Directory
                .GetDirectories(FeedDirectory, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
        )
        {
            Stamp(text, "d", directory, Directory.GetLastWriteTimeUtc(directory));
        }

        foreach (
            string file in Directory
                .GetFiles(FeedDirectory, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
        )
        {
            FileInfo info = new(file);
            Stamp(text, "f", file, info.LastWriteTimeUtc, info.Length);
        }

        return text.ToString();
    }

    private static void Stamp(
        StringBuilder text,
        string kind,
        string path,
        DateTime time,
        long length = 0
    ) =>
        text.Append(kind)
            .Append(' ')
            .Append(path)
            .Append(' ')
            .Append(length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(time.ToString("O", System.Globalization.CultureInfo.InvariantCulture))
            .Append('\n');

    /// <summary>Removes the scratch payload files this feed copied into its nupkgs.</summary>
    internal void Cleanup()
    {
        foreach (string file in _temporary)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException) { }
        }
    }
}
