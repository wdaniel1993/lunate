using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Lunate.TestSupport;

/// <summary>
/// One child process and what it said. The summary is built here rather than at the call site so a
/// failure can never be reported as a bare "exit code 1:" — when the child printed nothing, the
/// message says that, which is itself the diagnosis: the tool died before it wrote any output.
/// </summary>
public sealed record ChildProcessResult(
    string Command,
    string WorkingDirectory,
    int ExitCode,
    TimeSpan Duration,
    string StandardOutput,
    string StandardError
)
{
    /// <summary>How much of a stream a failure message keeps. The tail is where tools put the error.</summary>
    private const int TailChars = 4000;

    public bool Succeeded => ExitCode == 0;

    /// <summary>The child's own account of the failure, ready to become an exception message.</summary>
    public string Describe(string headline)
    {
        var text = new StringBuilder(headline);
        text.AppendLine();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  command:   {Command}"));
        text.AppendLine(
            string.Create(CultureInfo.InvariantCulture, $"  directory: {WorkingDirectory}")
        );
        text.AppendLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"  exit code: {ExitCode}{NoteOnExitCode()} after {Duration.TotalSeconds:0.0}s"
            )
        );

        // A disturbed solution restore reports "Build FAILED" with zero errors and no project line,
        // so "0 Error(s)" can be the whole account of the failure. MSBuild still named every target
        // it entered; the last one is where the child was when it gave up.
        if (LastTarget() is { } target)
        {
            text.AppendLine(
                string.Create(CultureInfo.InvariantCulture, $"  last target: {target}")
            );
        }

        string output = Tail(StandardOutput);
        string error = Tail(StandardError);
        if (output.Length == 0 && error.Length == 0)
        {
            text.Append("  output:    (the child printed nothing on stdout or stderr)");
            return text.ToString();
        }

        Append(text, "stdout", StandardOutput, output);
        Append(text, "stderr", StandardError, error);
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// A killed child is not the same as a child that chose to exit non-zero; on Unix the exit code
    /// above 128 is the signal, and saying so saves a reader from chasing an exit code that no
    /// program produced.
    /// </summary>
    private string NoteOnExitCode() =>
        ExitCode > 128 && !OperatingSystem.IsWindows()
            ? string.Create(CultureInfo.InvariantCulture, $" (killed by signal {ExitCode - 128})")
            : string.Empty;

    /// <summary>
    /// The last MSBuild target a failed child entered, read from the child's own console lines:
    /// MSBuild prints <c>TargetName:</c>, prefixed with the node number when it fanned out. Null when
    /// the child succeeded or printed no target line.
    /// </summary>
    private string? LastTarget()
    {
        if (Succeeded)
        {
            return null;
        }

        string[] lines = StandardOutput.Split('\n');
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            if (TryTargetName(lines[index], out string name))
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Whether <paramref name="line"/> is an MSBuild target line, and which target it names.</summary>
    private static bool TryTargetName(string line, out string name)
    {
        name = string.Empty;
        string trimmed = line.Trim();
        if (!trimmed.EndsWith(':'))
        {
            return false;
        }

        string candidate = trimmed[..^1];
        int node = candidate.IndexOf('>');
        if (node >= 0)
        {
            if (node == 0 || !candidate[..node].All(char.IsAsciiDigit))
            {
                return false;
            }

            candidate = candidate[(node + 1)..];
        }

        if (candidate.Length == 0 || (!char.IsAsciiLetter(candidate[0]) && candidate[0] != '_'))
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.'))
            {
                return false;
            }
        }

        name = candidate;
        return true;
    }

    private static void Append(StringBuilder text, string name, string raw, string tail)
    {
        if (tail.Length == 0)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  {name}: (nothing)"));
            return;
        }

        text.AppendLine(
            tail.Length == raw.TrimEnd().Length
                ? string.Create(CultureInfo.InvariantCulture, $"  {name}:")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {name} (last {TailChars} of {raw.Length} chars):"
                )
        );
        foreach (string line in tail.Split('\n'))
        {
            text.Append("    ").AppendLine(line.TrimEnd('\r'));
        }
    }

    private static string Tail(string text)
    {
        string trimmed = text.TrimEnd();
        return trimmed.Length <= TailChars ? trimmed : trimmed[^TailChars..];
    }
}

/// <summary>Runs the dotnet CLI as a child process and keeps everything it said.</summary>
public static class DotnetCli
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The host the test run uses: <c>DOTNET_HOST_PATH</c> when the runner set it.</summary>
    public static string HostPath()
    {
        string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        return string.IsNullOrEmpty(host) || !File.Exists(host) ? "dotnet" : host;
    }

    /// <summary>
    /// Runs <paramref name="arguments"/> in <paramref name="workingDirectory"/>. Both streams are
    /// drained while the child runs — reading one to the end before the other deadlocks a child
    /// that writes more than a pipe buffer to the stream nobody is reading.
    /// </summary>
    /// <exception cref="TimeoutException">
    /// The child outlived <paramref name="timeout"/>; the message carries what it printed until then.
    /// </exception>
    public static ChildProcessResult Run(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout
    )
    {
        var startInfo = new ProcessStartInfo(HostPath())
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach ((string name, string? value) in environment)
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        string command = string.Create(
            CultureInfo.InvariantCulture,
            $"{HostPath()} {string.Join(' ', arguments)}"
        );
        var started = Stopwatch.StartNew();
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();

        bool exited = process.WaitForExit((int)timeout.TotalMilliseconds);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }

        var result = new ChildProcessResult(
            command,
            workingDirectory,
            exited ? process.ExitCode : -1,
            started.Elapsed,
            Drain(output),
            Drain(error)
        );
        if (!exited)
        {
            throw new TimeoutException(
                result.Describe(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"dotnet did not finish within {timeout.TotalSeconds:0} seconds"
                    )
                )
            );
        }

        return result;
    }

    /// <summary>
    /// The streams close when the child exits; a build server the child left behind can hold them
    /// open, and a diagnostic helper must not hang on that. A read that fails is reported as such:
    /// swallowing it would turn a real error into "the child printed nothing", which sends a reader
    /// looking for a silent crash that never happened. A wait that expires is reported the same way
    /// and for the same reason — an empty string would say the child was silent when it was the pipe
    /// that never closed.
    /// </summary>
    private static string Drain(Task<string> stream) => Drain(stream, DrainTimeout);

    /// <summary>The drain above, with its budget exposed so a test can prove what an expiry says.</summary>
    internal static string Drain(Task<string> stream, TimeSpan timeout)
    {
        try
        {
            return stream.Wait(timeout)
                ? stream.Result
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"<the stream had not closed {timeout.TotalSeconds:0.#} s after the child exited; its output is missing from this report>"
                );
        }
        catch (AggregateException exception)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"<the stream could not be read: {exception.GetBaseException().Message}>"
            );
        }
    }
}
