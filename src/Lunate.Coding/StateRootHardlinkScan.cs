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
                    $"pre-existing hardlink can bypass the bash guard: {path} has a link outside LUNATE_HOME"
                );
            }

            if (Incomplete)
            {
                warnings.Add(
                    "the state-root hardlink scan was incomplete; some files could not be inspected"
                );
            }

            return warnings;
        }
    }
}

internal static class StateRootHardlinkScan
{
    internal static HardlinkScanResult Scan(string stateRoot, IHardlinkScanFileSystem fileSystem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        ArgumentNullException.ThrowIfNull(fileSystem);
        return new HardlinkScanResult(null, Incomplete: false);
    }
}
