using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Lunate.Coding;

/// <summary>
/// Kills a POSIX process tree by enumerating descendants with <c>ps</c> and sending
/// SIGKILL to each child before its parent.
/// </summary>
/// <remarks>
/// Deliberately avoids <see cref="Process.Kill(bool)"/> with <c>entireProcessTree: true</c>:
/// its Unix implementation SIGSTOPs the whole tree before killing it, which was observed to
/// wedge macOS CI VMs — the runner agent stops responding, no logs or step timeouts fire,
/// and the job dies with "the hosted runner lost communication with the server". A minimal
/// repro (spawn <c>sh -c 'exec sleep 300' &amp; sleep 300</c>; call the framework tree kill)
/// wedges the VM, while this enumeration path completes in milliseconds. The Windows
/// implementation (job objects) is unaffected and stays on the framework path.
/// </remarks>
internal static class PosixProcessTree
{
    /// <summary>Kills the process and all its descendants, children first. Best effort.</summary>
    public static void Kill(int rootPid)
    {
        IReadOnlyList<int> targets;
        try
        {
            targets = Enumerate(ReadProcessTable(), rootPid);
        }
        catch (Exception exception)
            when (exception is Win32Exception or IOException or InvalidOperationException)
        {
            // Enumeration failed (e.g. ps unavailable); degrade to killing the root only.
            targets = [rootPid];
        }

        foreach (var pid in targets)
        {
            try
            {
                Process.GetProcessById(pid).Kill();
            }
            catch (Exception exception)
                when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Already exited or not ours to kill; best effort by design.
            }
        }
    }

    /// <summary>Collects the root's descendants (children before parents), root last.</summary>
    internal static IReadOnlyList<int> Enumerate(string processTable, int rootPid)
    {
        var parentOf = new Dictionary<int, int>();
        foreach (var line in processTable.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (
                parts.Length == 2
                && int.TryParse(
                    parts[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var pid
                )
                && int.TryParse(
                    parts[1],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parent
                )
                && pid > 0
                && pid != parent
            )
            {
                parentOf[pid] = parent;
            }
        }

        var ordered = new List<int>();
        var visited = new HashSet<int>();

        void Collect(int pid)
        {
            if (!visited.Add(pid))
            {
                return;
            }

            foreach (
                var child in parentOf
                    .Where(entry => entry.Value == pid)
                    .Select(entry => entry.Key)
                    .Order()
            )
            {
                Collect(child);
            }

            ordered.Add(pid);
        }

        Collect(rootPid);
        return ordered;
    }

    private static string ReadProcessTable()
    {
        using var ps = Process.Start(
            new ProcessStartInfo("/bin/ps")
            {
                RedirectStandardOutput = true,
                ArgumentList = { "-axo", "pid=,ppid=" },
            }
        )!;
        var output = ps.StandardOutput.ReadToEnd();
        ps.WaitForExit();
        return output;
    }
}
