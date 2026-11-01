using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// Tracks what a run's bash calls change in a git worktree and reports only real changes: a
/// per-run snapshot (HEAD oid, the root's prefix inside the repository, and the content hash of
/// every path the last scan saw) is diffed against each new scan — <c>git status</c> answers
/// "what differs from HEAD", so publishing it raw would re-flag already-dirty files after every
/// call and would miss clean-to-clean changes. When git cannot answer (no git, no repository,
/// broken HEAD, timeout, spawn trouble), the content-stamp walk covers (#418): it hashes every
/// readable regular file under the root and diff-answers from the snapshot, and the report says
/// how it was seen. When nothing can look at the workspace, the report is unavailable and the
/// snapshot stays — the name why it produced nothing is in the extension log (#395), so a scan
/// trouble cannot hide behind a test that polls for a publish that never comes.
/// </summary>
internal sealed partial class BashChangeTracker(
    IExtensionLog? log = null,
    TimeSpan? scanTimeout = null
)
{
    private static readonly TimeSpan DefaultScanTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The window for each git invocation: the default keeps a bash call's epilogue short on a
    /// warm machine; tests widen it, because a cold git start on a loaded CI runner can exceed it.
    /// </summary>
    private readonly TimeSpan _scanTimeout = scanTimeout ?? DefaultScanTimeout;
    private static readonly ProcessRunner Runner = new();

    /// <summary>The snapshot cap: past this, the least recently used run's snapshot is evicted.</summary>
    internal const int MaxSnapshots = 64;

    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Dictionary<
        (string? RunId, string Root),
        (Snapshot Snapshot, long Tick)
    > _snapshots = [];
    private long _tick;

    /// <summary>One publishable change: the workspace-relative path and its kind.</summary>
    internal sealed record Change(string RelativePath, FileChangeKind Kind);

    /// <summary>
    /// Scans the run's workspace root and reports the changes since this run's previous scan; the
    /// first scan of a run only baselines (nothing is published for pre-existing state). When git
    /// cannot answer, the stamp walk covers (#418) and the report's source says how it was seen;
    /// only when nothing can look at the workspace is a bare empty list again the answer, marked
    /// unavailable. Only a cancelled token propagates.
    /// </summary>
    internal async Task<ScanReport> ObserveAsync(
        string? runId,
        string root,
        CancellationToken cancellationToken = default
    )
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = (runId, root);
            bool baselined = _snapshots.TryGetValue(
                key,
                out (Snapshot Snapshot, long Tick) previous
            );

            (Scan? scan, string? gitReason) = await ScanAsync(root, cancellationToken)
                .ConfigureAwait(false);
            ScanSource source = ScanSource.Git;
            if (scan is null)
            {
                // Git did not answer (a broken HEAD, no repository, a timeout, a starved runner):
                // the stamp walk covers from content (#418) so a workspace a model keeps editing
                // stays observable behind a git that cannot look. A non-repository or unreadable
                // root is not walkable either — then nothing looked, and the scan says so instead
                // of a bare empty list that reads as "no change".
                (Scan? stamp, string? walkTrouble) = StampScan(
                    root,
                    baselined ? previous.Snapshot.Status : null
                );
                if (stamp is null)
                {
                    return new ScanReport(
                        [],
                        ScanSource.Unavailable,
                        $"{gitReason}; the stamp walk could not look either ({walkTrouble})"
                    );
                }

                scan = stamp;
                source = ScanSource.Stamp;
            }
            else
            {
                gitReason = null;
            }

            if (!baselined)
            {
                if (_snapshots.Count >= MaxSnapshots)
                {
                    _snapshots.Remove(_snapshots.MinBy(entry => entry.Value.Tick).Key);
                }

                _snapshots[key] = (Snapshot.Baseline(scan), ++_tick);
                return new ScanReport([], source, gitReason);
            }

            (IReadOnlyList<Change> changes, Snapshot next) = await DiffAsync(
                    previous.Snapshot,
                    scan,
                    root,
                    cancellationToken
                )
                .ConfigureAwait(false);
            // A fresh tick marks this snapshot as the most recently used.
            _snapshots[key] = (next, ++_tick);
            return new ScanReport(changes, source, gitReason);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task<(IReadOnlyList<Change> Changes, Snapshot Next)> DiffAsync(
        Snapshot previous,
        Scan scan,
        string root,
        CancellationToken cancellationToken
    )
    {
        var changes = new List<Change>();
        var published = new HashSet<string>(StringComparer.Ordinal);
        var next = previous.Advance(scan);

        // (a) In the current status set: new, or content differs from the last scan (a null hash
        // means the file is missing — a deletion counts). This catches same-stamp edits to
        // already-dirty files (the reason raw status must not be published).
        foreach ((string path, FileChangeKind kind) in scan.Entries)
        {
            if (
                !previous.Status.TryGetValue(path, out string? previousHash)
                || !string.Equals(previousHash, scan.Status[path], StringComparison.Ordinal)
            )
            {
                changes.Add(new Change(path, kind));
                published.Add(path);
            }

            // Remember what was observed so rule (c)'s content filter sees mid-run edits too.
            next.Disk[path] = scan.Status[path];
        }

        // (b) Left the status set: a revert (checkout --, reset, stash) has content different from
        // the last scan; a commit leaves the content unchanged and is skipped.
        foreach ((string path, string? previousHash) in previous.Status)
        {
            if (scan.Status.ContainsKey(path))
            {
                continue;
            }

            string? now = HashOf(root, path);
            if (!string.Equals(now, previousHash, StringComparison.Ordinal))
            {
                changes.Add(new Change(path, FileChangeKind.Modified));
                published.Add(path);
            }

            next.Disk[path] = now;
        }

        // (c) HEAD moved (checkout, pull, rebase): the paths the two heads differ in, content
        // filtered so a commit (unchanged content) stays quiet. A renamed path publishes both
        // sides; the letter decides Created vs Modified.
        if (
            previous.Head is not null
            && scan.Head is not null
            && !string.Equals(previous.Head, scan.Head, StringComparison.Ordinal)
        )
        {
            foreach (
                (string path, FileChangeKind kind) in await GitDiffNamesAsync(
                        root,
                        previous.Head,
                        scan.Head,
                        cancellationToken
                    )
                    .ConfigureAwait(false)
            )
            {
                if (!path.StartsWith(scan.Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = path[scan.Prefix.Length..];
                if (published.Contains(relative))
                {
                    continue;
                }

                string? now = HashOf(root, relative);
                if (
                    !previous.Disk.TryGetValue(relative, out string? cached)
                    || !string.Equals(cached, now, StringComparison.Ordinal)
                )
                {
                    changes.Add(new Change(relative, kind));
                    published.Add(relative);
                }

                next.Disk[relative] = now;
            }
        }

        return (changes, next);
    }

    private async Task<(Scan? Scan, string? Reason)> ScanAsync(
        string root,
        CancellationToken cancellationToken
    )
    {
        (ProcessRunResult? status, string? spawnReason) = await RunGitAsync(
                root,
                ["status", "--porcelain", "-z", "--ignore-submodules=dirty", "-unormal", "--", "."],
                cancellationToken
            )
            .ConfigureAwait(false);
        if (status is null)
        {
            // The reason is already logged inside the spawn wrapper.
            return (null, spawnReason);
        }

        if (status.TimedOut)
        {
            log?.Warn("bash change scan timed out; the content-stamp scan stays the backstop.");
            return (null, "git scan timed out");
        }

        if (status.ExitCode != 0)
        {
            // A git failure never publishes; it does say why (#395): a scan that produces nothing
            // must be distinguishable from "no change", in the log the extension owner reads.
            string reason =
                $"git status failed (exit {status.ExitCode!.Value.ToString(CultureInfo.InvariantCulture)}; {FirstLine(status.Stderr)})";
            log?.Warn(
                $"bash change scan: git status failed (exit "
                    + $"{status.ExitCode!.Value.ToString(CultureInfo.InvariantCulture)}; "
                    + $"{FirstLine(status.Stderr)}); the content-stamp scan stays the backstop."
            );
            return (null, reason);
        }

        IReadOnlyList<(string Path, FileChangeKind Kind)> parsed = ParsePorcelain(status.Stdout);

        string? head = null;
        string prefix = "";
        (ProcessRunResult? identity, _) = await RunGitAsync(
                root,
                ["rev-parse", "HEAD", "--show-prefix"],
                cancellationToken
            )
            .ConfigureAwait(false);
        if (identity is { TimedOut: false, ExitCode: 0 })
        {
            string[] lines = identity.Stdout.ReplaceLineEndings("\n").Split('\n');
            if (lines.Length >= 1 && lines[0].Trim().Length > 0)
            {
                head = lines[0].Trim();
            }

            if (lines.Length >= 2)
            {
                prefix = lines[1].Trim();
            }
        }

        var entries = new List<(string Path, FileChangeKind Kind)>();
        var hashes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach ((string path, FileChangeKind kind) in parsed)
        {
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string relative = path[prefix.Length..];
            entries.Add((relative, kind));
            hashes[relative] = HashOf(root, relative);
        }

        return (new Scan(head, prefix, entries, hashes), null);
    }

    /// <summary>
    /// Parses <c>git status --porcelain -z</c> output: NUL-separated entries; a rename or copy
    /// entry carries its origin path in the following field (consumed either way); untracked
    /// directories (a trailing slash) are skipped; a rename's origin is published (the file moved
    /// away) while a copy's origin is not (it still exists).
    /// </summary>
    internal static IReadOnlyList<(string Path, FileChangeKind Kind)> ParsePorcelain(string output)
    {
        var entries = new List<(string Path, FileChangeKind Kind)>();
        string[] fields = output.Split('\0');
        for (int i = 0; i < fields.Length; i++)
        {
            string field = fields[i];
            if (field.Length < 4)
            {
                continue;
            }

            char x = field[0];
            string path = field[3..];
            if (path.EndsWith('/'))
            {
                continue;
            }

            entries.Add(
                (
                    path,
                    x is '?' or 'A' or 'R' or 'C' ? FileChangeKind.Created : FileChangeKind.Modified
                )
            );

            if (x is 'R' or 'C' && i + 1 < fields.Length && fields[i + 1].Length > 0)
            {
                string origin = fields[++i];
                if (x == 'R')
                {
                    entries.Add((origin, FileChangeKind.Modified));
                }
            }
        }

        return entries;
    }

    private static string? HashOf(string root, string relativePath)
    {
        string absolute = Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)
        );
        try
        {
            using FileStream stream = new(
                absolute,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite
            );
            return Convert.ToHexStringLower(SHA256.HashData(stream));
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
                        or System.Security.SecurityException
            )
        {
            return null;
        }
    }

    /// <summary>
    /// The <c>git diff --name-status -z</c> entries between two heads, resolved to publishable
    /// changes: an add is Created, everything else Modified; a rename publishes its old path
    /// (moved away, Modified) and its new path (Created); a copy publishes only its new path
    /// (Created — the origin is unchanged).
    /// </summary>
    private async Task<IReadOnlyList<(string Path, FileChangeKind Kind)>> GitDiffNamesAsync(
        string root,
        string fromHead,
        string toHead,
        CancellationToken cancellationToken
    )
    {
        (ProcessRunResult? diff, _) = await RunGitAsync(
                root,
                ["diff", "--name-status", "-z", fromHead, toHead],
                cancellationToken
            )
            .ConfigureAwait(false);
        if (diff is null || diff.TimedOut || diff.ExitCode != 0)
        {
            log?.Warn(
                "bash change scan: the head-to-head history diff failed; "
                    + "the content-stamp scan stays the backstop."
            );
            return [];
        }

        var entries = new List<(string Path, FileChangeKind Kind)>();
        string[] fields = diff.Stdout.Split('\0');
        for (int i = 0; i < fields.Length; i++)
        {
            string letter = fields[i];
            if (letter.Length == 0)
            {
                continue;
            }

            if (i + 1 >= fields.Length)
            {
                break;
            }

            char code = letter[0];
            string path = fields[++i];
            if (code is 'R' or 'C')
            {
                if (i + 1 >= fields.Length)
                {
                    break;
                }

                string newPath = fields[++i];
                if (code is 'R')
                {
                    entries.Add((path, FileChangeKind.Modified));
                }

                entries.Add((newPath, FileChangeKind.Created));
            }
            else
            {
                entries.Add((path, code is 'A' ? FileChangeKind.Created : FileChangeKind.Modified));
            }
        }

        return entries;
    }

    /// <summary>
    /// Runs one git invocation. A spawn failure returns no result and the reason, already logged
    /// (#395), so a scan that could not look says so.
    /// </summary>
    private async Task<(ProcessRunResult? Result, string? Reason)> RunGitAsync(
        string root,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        try
        {
            ProcessRunResult result = await Runner
                .RunAsync(
                    new ProcessRunRequest(
                        "git",
                        ["--no-optional-locks", .. arguments],
                        root,
                        Timeout: _scanTimeout,
                        OutputCapBytes: 1_000_000
                    ),
                    cancellationToken
                )
                .ConfigureAwait(false);
            return (result, null);
        }
        catch (Exception exception)
            when (exception
                    is Win32Exception
                        or InvalidOperationException
                        or PlatformNotSupportedException
                        or ArgumentException
                        or IOException
            )
        {
            // Git cannot start (or is missing): still say why (#395) — a starved runner whose
            // spawn fails must not look like a run that changed nothing.
            string reason =
                $"git could not start ({exception.GetType().Name}: {exception.Message})";
            log?.Warn(
                $"bash change scan: git could not start ({exception.GetType().Name}: "
                    + $"{exception.Message}); the content-stamp scan stays the backstop."
            );
            return (null, reason);
        }
    }

    /// <summary>The first line of a git error response, capped so a log entry stays one.</summary>
    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n');
        string line = (end < 0 ? text : text[..end]).Trim();
        return line.Length <= 200 ? line : line[..200];
    }

    private sealed record Scan(
        string? Head,
        string Prefix,
        IReadOnlyList<(string Path, FileChangeKind Kind)> Entries,
        Dictionary<string, string?> Status
    );

    private sealed class Snapshot
    {
        public required string? Head { get; init; }

        public required string Prefix { get; init; }

        public required Dictionary<string, string?> Status { get; init; }

        public required Dictionary<string, string?> Disk { get; init; }

        public static Snapshot Baseline(Scan scan) =>
            new()
            {
                Head = scan.Head,
                Prefix = scan.Prefix,
                Status = scan.Status,
                Disk = new Dictionary<string, string?>(scan.Status, StringComparer.Ordinal),
            };

        public Snapshot Advance(Scan scan) =>
            new()
            {
                Head = scan.Head,
                Prefix = scan.Prefix,
                Status = scan.Status,
                Disk = new Dictionary<string, string?>(Disk, StringComparer.Ordinal),
            };
    }
}
