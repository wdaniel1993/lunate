namespace Lunate.Coding;

internal readonly record struct HardlinkFileIdentity(ulong Device, ulong File);

internal readonly record struct HardlinkFileMetadata(
    HardlinkFileIdentity Identity,
    ulong LinkCount
);

internal interface IHardlinkScanFileSystem
{
    bool DirectoryExists(string path);

    IReadOnlyList<string> EnumerateDirectories(string path);

    IReadOnlyList<string> EnumerateFiles(string path);

    bool IsDirectoryLink(string path);

    HardlinkFileMetadata? GetFileMetadata(string path);
}

internal sealed record HardlinkScanResult(string? ExternalPath, bool Incomplete)
{
    internal IReadOnlyList<string> Warnings
    {
        get
        {
            List<string> warnings = [];
            if (ExternalPath is { } path)
            {
                warnings.Add(
                    $"warning: pre-existing hardlink can bypass the bash guard: {path} has a link outside LUNATE_HOME"
                );
            }

            if (Incomplete)
            {
                warnings.Add(
                    "warning: the state-root hardlink scan was incomplete; some files could not be inspected"
                );
            }

            return warnings;
        }
    }
}

internal static class StateRootHardlinkScan
{
    internal static void Report(
        string? stateRoot,
        IHardlinkScanFileSystem? fileSystem,
        Action<string> warn
    )
    {
        ArgumentNullException.ThrowIfNull(warn);
        if (stateRoot is null)
        {
            return;
        }

        HardlinkScanResult result = fileSystem is null
            ? Scan(stateRoot)
            : Scan(stateRoot, fileSystem);
        foreach (string warning in result.Warnings)
        {
            warn(warning);
        }
    }

    internal static HardlinkScanResult Scan(string stateRoot) =>
        Scan(stateRoot, SystemHardlinkScanFileSystem.Instance);

    internal static HardlinkScanResult Scan(string stateRoot, IHardlinkScanFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);

        bool incomplete = false;
        try
        {
            if (!fileSystem.DirectoryExists(stateRoot))
            {
                return new HardlinkScanResult(null, Incomplete: false);
            }
        }
        catch (Exception exception) when (IsScanFailure(exception))
        {
            return new HardlinkScanResult(null, Incomplete: true);
        }

        var pending = new Stack<string>();
        var visited = new HashSet<string>(PathComparer);
        var files = new Dictionary<HardlinkFileIdentity, FileGroup>();
        pending.Push(stateRoot);
        visited.Add(stateRoot);

        while (pending.TryPop(out string? directory))
        {
            IReadOnlyList<string> directories;
            try
            {
                directories = fileSystem.EnumerateDirectories(directory);
            }
            catch (Exception exception) when (IsScanFailure(exception))
            {
                incomplete = true;
                directories = [];
            }

            foreach (string child in directories.Order(StringComparer.Ordinal))
            {
                try
                {
                    if (!fileSystem.IsDirectoryLink(child) && visited.Add(child))
                    {
                        pending.Push(child);
                    }
                }
                catch (Exception exception) when (IsScanFailure(exception))
                {
                    incomplete = true;
                }
            }

            IReadOnlyList<string> entries;
            try
            {
                entries = fileSystem.EnumerateFiles(directory);
            }
            catch (Exception exception) when (IsScanFailure(exception))
            {
                incomplete = true;
                continue;
            }

            foreach (string path in entries.Order(StringComparer.Ordinal))
            {
                try
                {
                    if (fileSystem.GetFileMetadata(path) is not { } metadata)
                    {
                        continue;
                    }

                    if (!files.TryGetValue(metadata.Identity, out FileGroup? group))
                    {
                        files.Add(metadata.Identity, new FileGroup(path, metadata.LinkCount));
                    }
                    else
                    {
                        group.Add(path, metadata.LinkCount);
                    }
                }
                catch (Exception exception) when (IsScanFailure(exception))
                {
                    incomplete = true;
                }
            }
        }

        string? externalPath = files
            .Values.Where(group => group.LinkCount > group.PathsFound)
            .OrderBy(group => group.FirstPath, StringComparer.Ordinal)
            .Select(group => group.FirstPath)
            .FirstOrDefault();
        return new HardlinkScanResult(externalPath, incomplete);
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsScanFailure(Exception exception) =>
        exception
            is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException
                or System.ComponentModel.Win32Exception;

    private sealed class FileGroup(string firstPath, ulong linkCount)
    {
        internal string FirstPath { get; } = firstPath;
        internal ulong LinkCount { get; private set; } = linkCount;
        internal ulong PathsFound { get; private set; } = 1;

        internal void Add(string path, ulong linkCount)
        {
            if (!PathComparer.Equals(path, FirstPath))
            {
                PathsFound++;
            }

            LinkCount = Math.Max(LinkCount, linkCount);
        }
    }
}
