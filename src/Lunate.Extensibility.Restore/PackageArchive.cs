using System.IO.Compression;
using System.Text;

namespace Lunate.Extensibility.Restore;

/// <summary>
/// The nupkg as an archive, not as a package: every entry name is confined through
/// <see cref="PackagePaths.Entry"/> before anything is read, the entry count and the expanded size
/// are known from the zip's own directory (no decompression), and content is read as a stream so a
/// huge entry can never be materialised by accident.
/// </summary>
internal sealed class PackageArchive : IDisposable
{
    /// <summary>The most entries a package may hold before it is refused (design D11).</summary>
    internal const int MaxEntries = 20_000;

    /// <summary>The largest expanded size a package may have before it is refused.</summary>
    internal const long MaxExpandedBytes = 512L * 1024 * 1024;

    private readonly ZipArchive _zip;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.Ordinal);

    private PackageArchive(
        ZipArchive zip,
        Dictionary<string, ZipArchiveEntry> entries,
        IReadOnlyList<string> files,
        long expandedBytes
    )
    {
        _zip = zip;
        _entries = entries;
        Files = files;
        ExpandedBytes = expandedBytes;
    }

    /// <summary>Every file entry, package-relative with forward slashes, sorted; no directories.</summary>
    internal IReadOnlyList<string> Files { get; }

    /// <summary>The sum of the entries' uncompressed sizes, as the zip's central directory states it.</summary>
    internal long ExpandedBytes { get; }

    internal static PackageArchive Open(string nupkgPath, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nupkgPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        FileStream stream;
        try
        {
            stream = File.OpenRead(nupkgPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                $"{label}: package file '{nupkgPath}' could not be read: {exception.Message}",
                exception
            );
        }

        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read);
        }
        catch (InvalidDataException exception)
        {
            stream.Dispose();
            throw new InvalidDataException(
                $"{label}: package file '{nupkgPath}' is not a valid nupkg (it is not a zip "
                    + $"archive): {exception.Message}",
                exception
            );
        }

        try
        {
            Bounds(zip, label);
            Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.Ordinal);
            List<string> files = [];
            long expanded = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                string path = PackagePaths.Entry(entry.FullName);
                if (entry.FullName.EndsWith('/'))
                {
                    // A directory entry is a name, not content; its name was judged above.
                    continue;
                }

                if (!entries.TryAdd(path, entry))
                {
                    throw new InvalidDataException(
                        $"{label}: the archive carries '{path}' more than once, so which bytes "
                            + "would be installed is ambiguous; rebuild the package without the "
                            + "duplicate entry."
                    );
                }

                files.Add(path);
                expanded += entry.Length;
            }

            files.Sort(StringComparer.Ordinal);
            return new PackageArchive(zip, entries, files, expanded);
        }
        catch
        {
            zip.Dispose();
            stream.Dispose();
            throw;
        }
    }

    internal bool Contains(string path) => _entries.ContainsKey(path);

    /// <summary>The text of one entry as UTF-8 without a byte-order mark, or null when absent.</summary>
    internal string? ReadText(string path)
    {
        if (!_entries.TryGetValue(path, out ZipArchiveEntry? entry))
        {
            return null;
        }

        using Stream stream = entry.Open();
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true
        );
        return reader.ReadToEnd();
    }

    /// <summary>The file entries under <paramref name="folder"/>, prefix included.</summary>
    internal List<string> FilesUnder(string folder)
    {
        string prefix = folder + "/";
        return [.. Files.Where(file => file.StartsWith(prefix, StringComparison.Ordinal))];
    }

    public void Dispose() => _zip.Dispose();

    /// <summary>
    /// The two bounds, checked from the zip's directory before a byte is extracted: more than
    /// <see cref="MaxEntries"/> entries, or more than <see cref="MaxExpandedBytes"/> expanded.
    /// </summary>
    private static void Bounds(ZipArchive zip, string label)
    {
        var count = zip.Entries.Count;
        if (count > MaxEntries)
        {
            throw new InvalidDataException(
                FormattableString.Invariant(
                    $"{label}: the archive holds {count} entries, over the {MaxEntries}-entry limit; "
                )
                    + "nothing was extracted. Split the package or drop the entries it does not "
                    + "install."
            );
        }

        long expanded = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            expanded += entry.Length;
        }

        if (expanded > MaxExpandedBytes)
        {
            throw new InvalidDataException(
                FormattableString.Invariant(
                    $"{label}: the archive expands to {expanded} bytes, over the 512 MiB ({MaxExpandedBytes}-byte) limit; "
                ) + "nothing was extracted. Ship the payload smaller or split the package."
            );
        }
    }
}
