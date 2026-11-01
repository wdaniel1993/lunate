using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lunate.TestSupport;

/// <summary>
/// A content-addressed cache of fixture restore output, shared by every test process on the machine.
/// </summary>
/// <remarks>
/// A restore is a function of the tree it runs in, so the artifacts of one restore answer every test
/// whose tree is byte-identical. Entries live under the temp directory, keyed by the tree's content,
/// and are guarded by a lock file per entry: the first process to ask restores, everybody else copies.
/// The artifacts of a hit were written under the entry, so paths inside them point there; the fixtures
/// carry no packages and the entry outlives the copy, so the result is a working restore.
/// </remarks>
internal static class RestoreCache
{
    /// <summary>
    /// Bumped when the artifacts, their layout or the restore recipe change: it retires every entry.
    /// </summary>
    private const string Format = "v2";

    /// <summary>Longer than any restore takes; a lock that cannot be had is not fatal (see <see cref="Lock"/>).</summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Directories the address never reads and a restore never writes: the restore output itself,
    /// and the repository metadata. Git rewrites <c>.git</c> while a test runs — maintenance, index
    /// and ref locks — so hashing it made two identical trees disagree, and it could stat a lock
    /// file that git had just deleted.
    /// </summary>
    private static readonly string[] Excluded =
    [
        "obj",
        "bin",
        ".packages",
        ".nuget-http-cache",
        ".tmp",
        ".git",
    ];

    /// <summary>
    /// The content address of the tree at <paramref name="root"/>. Trees that share it are
    /// byte-identical, so one restore serves both; a test that edits its tree misses it.
    /// </summary>
    public static string Key(string root, string solutionFileName)
    {
        List<string> files = [];
        Collect(root, files);
        files.Sort(StringComparer.Ordinal);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(Format));
        hash.AppendData(Encoding.UTF8.GetBytes(solutionFileName));
        // Artifacts are authored by this SDK: a newer one may write a layout an older test cannot use.
        hash.AppendData(Encoding.UTF8.GetBytes(Environment.Version.ToString()));
        foreach (string file in files)
        {
            byte[] content;
            try
            {
                content = File.ReadAllBytes(file);
            }
            catch (Exception exception)
                when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                // A file that vanished mid-walk cannot be part of a stable address.
                continue;
            }

            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData(
                Encoding.UTF8.GetBytes(
                    string.Create(CultureInfo.InvariantCulture, $"\0{content.LongLength}\0")
                )
            );
            hash.AppendData(content);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>
    /// Takes the machine-wide lock for <paramref name="key"/>: one restore per tree across processes.
    /// Null when the lock could not be had, which is not a reason to fail a test — the caller restores
    /// anyway and a duplicate costs time, not correctness.
    /// </summary>
    public static IDisposable? Lock(string key) => LockFile(Path.Combine(Root(), key + ".lock"));

    /// <summary>
    /// Takes the one gate every fixture restore on the machine shares, so the number of child
    /// restores running at once stays bounded no matter how many test assemblies start together.
    /// Null when the gate could not be had — the caller restores anyway rather than fail a test on
    /// a lock.
    /// </summary>
    public static IDisposable? MachineLock() => LockFile(Path.Combine(Root(), "restore-gate.lock"));

    private static IDisposable? LockFile(string path)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < LockTimeout)
        {
            try
            {
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Copies the artifacts stored for <paramref name="key"/> into <paramref name="root"/>. An entry
    /// without restore output — a run killed mid-store — repairs itself: it is dropped and the caller
    /// restores for real.
    /// </summary>
    public static bool TryReuse(string key, string root)
    {
        string entry = Path.Combine(Root(), key);
        if (!HoldsArtifacts(entry))
        {
            Delete(entry);
            return false;
        }

        try
        {
            foreach ((string source, string relative) in WrittenDirectories(entry))
            {
                CopyDirectory(source, Path.Combine(root, relative));
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Delete(entry);
            return false;
        }
    }

    /// <summary>Keeps the artifacts now in <paramref name="root"/> for the next tree with that key.</summary>
    public static void Store(string key, string root)
    {
        string entry = Path.Combine(Root(), key);
        string staging = string.Create(
            CultureInfo.InvariantCulture,
            $"{entry}.staging-{Guid.NewGuid():N}"
        );
        try
        {
            foreach ((string source, string relative) in WrittenDirectories(root))
            {
                CopyDirectory(source, Path.Combine(staging, relative));
            }

            if (!HoldsArtifacts(staging))
            {
                // A restore that wrote nothing usable is not cached: the next test restores for real.
                Delete(staging);
                return;
            }

            Directory.Move(staging, entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cache that cannot be written costs the next test a restore, never correctness.
            Delete(staging);
        }
    }

    /// <summary>The per-machine cache root; entries are content-addressed, so a stale one is never wrong.</summary>
    private static string Root()
    {
        string root = Path.Combine(Path.GetTempPath(), "lunate-fixture-restore-cache", Format);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>Whether a tree actually carries restore output.</summary>
    private static bool HoldsArtifacts(string directory) =>
        Directory.Exists(directory)
        && Directory
            .EnumerateFiles(directory, "project.assets.json", SearchOption.AllDirectories)
            .Any();

    /// <summary>Every directory a restore writes, paired with its path relative to the tree root.</summary>
    private static IEnumerable<(string Source, string Relative)> WrittenDirectories(
        string baseDirectory
    )
    {
        var pending = new Stack<(string Path, string Relative)>();
        pending.Push((baseDirectory, string.Empty));
        while (pending.Count > 0)
        {
            (string path, string relative) = pending.Pop();
            foreach (string directory in Directory.EnumerateDirectories(path))
            {
                string name = Path.GetFileName(directory);
                string childRelative = relative.Length == 0 ? name : Path.Combine(relative, name);
                if (name == ".git")
                {
                    // A restore never writes repository metadata, so a snapshot must never carry
                    // it (#418): a fixture whose test committed into its root would otherwise
                    // store a foreign .git, and reusing it would copy that over the next test's
                    // fresh one — refs from one snapshot mixed with objects from another — and
                    // git would answer "fatal: bad object HEAD" for the rest of the leg. Copying
                    // less needs no Format bump: it is additive, older entries only stop being
                    // copied where they were never written by a restore.
                    continue;
                }

                if (Excluded.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    yield return (directory, childRelative);
                    continue;
                }

                pending.Push((directory, childRelative));
            }
        }
    }

    /// <summary>Every file the restore reads — everything except what it writes or git owns.</summary>
    private static void Collect(string directory, List<string> files)
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                files.Add(file);
            }

            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (Excluded.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                Collect(child, files);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // A directory that vanished under the walk has nothing to contribute to the address.
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }

    private static void Delete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { }
    }
}
