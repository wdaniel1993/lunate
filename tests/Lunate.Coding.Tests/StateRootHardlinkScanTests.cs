namespace Lunate.Coding.Tests;

public sealed class StateRootHardlinkScanTests
{
    [Fact]
    public void A_physical_external_hardlink_is_detected_when_supported()
    {
        using var temp = new TempDirectory();
        string state = Directory.CreateDirectory(Path.Combine(temp.Root, "state")).FullName;
        string outside = Directory.CreateDirectory(Path.Combine(temp.Root, "outside")).FullName;
        string source = Path.Combine(outside, "external.json");
        string link = Path.Combine(state, "linked.json");
        File.WriteAllText(source, "{}");
        Assert.SkipUnless(
            HardlinkTestFixture.TryCreate(link, source),
            "The temporary filesystem does not support hardlinks."
        );

        HardlinkScanResult result = StateRootHardlinkScan.Scan(state);

        Assert.False(result.Incomplete);
        Assert.Contains(link, Assert.Single(result.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_hardlink_warns_once_and_names_the_in_root_path()
    {
        using var temp = new TempDirectory();
        string state = Directory.CreateDirectory(Path.Combine(temp.Root, "state")).FullName;
        string file = Path.Combine(state, "trust.json");
        File.WriteAllText(file, "{}");
        var fileSystem = new FakeHardlinkScanFileSystem(state);
        fileSystem.Files[state] = [file];
        fileSystem.Metadata[file] = new(new HardlinkFileIdentity(1, 7), LinkCount: 2);

        HardlinkScanResult result = StateRootHardlinkScan.Scan(state, fileSystem);

        string warning = Assert.Single(result.Warnings);
        Assert.Contains(file, warning, StringComparison.Ordinal);
        Assert.False(result.Incomplete);
    }

    [Fact]
    public void Links_wholly_inside_the_state_root_do_not_warn()
    {
        using var temp = new TempDirectory();
        string state = Directory.CreateDirectory(Path.Combine(temp.Root, "state")).FullName;
        string first = Path.Combine(state, "first.json");
        string second = Path.Combine(state, "second.json");
        File.WriteAllText(first, "{}");
        File.WriteAllText(second, "{}");
        var identity = new HardlinkFileIdentity(1, 8);
        var fileSystem = new FakeHardlinkScanFileSystem(state);
        fileSystem.Files[state] = [first, second];
        fileSystem.Metadata[first] = new(identity, LinkCount: 2);
        fileSystem.Metadata[second] = new(identity, LinkCount: 2);

        HardlinkScanResult result = StateRootHardlinkScan.Scan(state, fileSystem);

        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void A_missing_state_root_is_not_created()
    {
        using var temp = new TempDirectory();
        string state = Path.Combine(temp.Root, "missing");
        var fileSystem = new FakeHardlinkScanFileSystem(state);

        HardlinkScanResult result = StateRootHardlinkScan.Scan(state, fileSystem);

        Assert.Empty(result.Warnings);
        Assert.False(Directory.Exists(state));
    }

    [Fact]
    public void A_partial_scan_failure_is_reported_without_aborting_startup()
    {
        using var temp = new TempDirectory();
        string state = Directory.CreateDirectory(Path.Combine(temp.Root, "state")).FullName;
        string child = Directory.CreateDirectory(Path.Combine(state, "nested")).FullName;
        var fileSystem = new FakeHardlinkScanFileSystem(state);
        fileSystem.Directories[state] = [child];
        fileSystem.FailingDirectories.Add(child);

        HardlinkScanResult result = StateRootHardlinkScan.Scan(state, fileSystem);

        Assert.True(result.Incomplete);
        Assert.Contains(
            "incomplete",
            Assert.Single(result.Warnings),
            StringComparison.OrdinalIgnoreCase
        );
    }

    [Fact]
    public void A_directory_link_is_not_followed()
    {
        using var temp = new TempDirectory();
        string state = Directory.CreateDirectory(Path.Combine(temp.Root, "state")).FullName;
        string linked = Path.Combine(state, "linked");
        var fileSystem = new FakeHardlinkScanFileSystem(state);
        fileSystem.Directories[state] = [linked];
        fileSystem.DirectoryLinks.Add(linked);
        fileSystem.Directories[linked] = [];
        fileSystem.Files[linked] = [Path.Combine(linked, "external.json")];
        fileSystem.Metadata[Path.Combine(linked, "external.json")] = new(
            new HardlinkFileIdentity(1, 9),
            LinkCount: 2
        );

        HardlinkScanResult result = StateRootHardlinkScan.Scan(state, fileSystem);

        Assert.Empty(result.Warnings);
    }

    private sealed class FakeHardlinkScanFileSystem(string root) : IHardlinkScanFileSystem
    {
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { root };

        internal Dictionary<string, IReadOnlyList<string>> Directories { get; } =
            new(StringComparer.Ordinal);

        internal Dictionary<string, IReadOnlyList<string>> Files { get; } =
            new(StringComparer.Ordinal);

        internal Dictionary<string, HardlinkFileMetadata> Metadata { get; } =
            new(StringComparer.Ordinal);

        internal HashSet<string> DirectoryLinks { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> FailingDirectories { get; } = new(StringComparer.Ordinal);

        public bool DirectoryExists(string path) => _directories.Contains(path);

        public IReadOnlyList<string> EnumerateDirectories(string path)
        {
            if (FailingDirectories.Contains(path))
            {
                throw new IOException("fixture scan failure");
            }

            if (Directories.TryGetValue(path, out IReadOnlyList<string>? directories))
            {
                foreach (string directory in directories)
                {
                    _directories.Add(directory);
                }

                return directories;
            }

            return [];
        }

        public IReadOnlyList<string> EnumerateFiles(string path) =>
            Files.GetValueOrDefault(path, []);

        public bool IsDirectoryLink(string path) => DirectoryLinks.Contains(path);

        public HardlinkFileMetadata? GetFileMetadata(string path) =>
            Metadata.TryGetValue(path, out HardlinkFileMetadata metadata) ? metadata : null;
    }

    private static class HardlinkTestFixture
    {
        internal static bool TryCreate(string linkPath, string sourcePath) =>
            OperatingSystem.IsWindows()
                ? CreateHardLinkWindows(linkPath, sourcePath, IntPtr.Zero)
                : CreateHardLinkUnix(sourcePath, linkPath) == 0;

        [System.Runtime.InteropServices.DllImport(
            "libc",
            EntryPoint = "link",
            SetLastError = true,
            CharSet = System.Runtime.InteropServices.CharSet.Ansi
        )]
        private static extern int CreateHardLinkUnix(string existingPath, string newPath);

        [System.Runtime.InteropServices.DllImport(
            "kernel32.dll",
            EntryPoint = "CreateHardLinkW",
            SetLastError = true,
            CharSet = System.Runtime.InteropServices.CharSet.Unicode
        )]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool
        )]
        private static extern bool CreateHardLinkWindows(
            string newPath,
            string existingPath,
            IntPtr securityAttributes
        );
    }
}
