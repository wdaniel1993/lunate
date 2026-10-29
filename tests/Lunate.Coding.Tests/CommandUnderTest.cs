using System.Globalization;

namespace Lunate.Coding.Tests;

/// <summary>
/// The command a test started whose child should write a pid file, as much as the test can see of
/// it: the directory it was given, the task that runs it, and how a finished run reads. The probe
/// uses this to say which failure happened — the command never started, it started and the file
/// landed somewhere the test was not looking, or it started and was slow — instead of printing one
/// timeout that fits all three.
/// </summary>
internal sealed record CommandUnderTest(string WorkingDirectory)
{
    /// <summary>The task running the command, when the test has a handle on it.</summary>
    public Task? Run { get; init; }

    /// <summary>
    /// How the finished run reads in a message — "exit code 7", "timed out after 60 seconds".
    /// Called only once <see cref="Run"/> has completed successfully; without it a finished run is
    /// reported as finished and no more.
    /// </summary>
    public Func<string>? DescribeOutcome { get; init; }

    /// <summary>What the command did, in the words a failure message needs.</summary>
    public string Describe()
    {
        if (Run is null)
        {
            return "the test had no handle on the command itself";
        }

        if (Run.IsFaulted)
        {
            Exception cause =
                Run.Exception?.GetBaseException()
                ?? new InvalidOperationException("the command failed without an exception");
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the command failed before its child wrote a pid: {cause.GetType().Name}: {cause.Message}"
            );
        }

        if (Run.IsCanceled)
        {
            return "the command was cancelled before its child wrote a pid";
        }

        if (Run.IsCompleted)
        {
            return DescribeOutcome is { } outcome
                ? string.Create(CultureInfo.InvariantCulture, $"the command finished: {outcome()}")
                : "the command finished";
        }

        return "the command was still running";
    }

    /// <summary>
    /// Where a file named like <paramref name="pidFile"/> exists under the command's directory, or
    /// a sentence saying it exists nowhere: a relative write landing in another working directory
    /// is the failure this rules in or out, and it cannot be answered by assuming the path.
    /// </summary>
    public string Locate(string pidFile)
    {
        if (File.Exists(pidFile))
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the watched file exists at {pidFile} but never held a pid the probe could read"
            );
        }

        string name = Path.GetFileName(pidFile);
        try
        {
            string[] found =
            [
                .. Directory
                    .EnumerateFiles(WorkingDirectory, name, SearchOption.AllDirectories)
                    .Take(2),
            ];
            return found.Length switch
            {
                0 => string.Create(
                    CultureInfo.InvariantCulture,
                    $"no file named {name} exists anywhere under {WorkingDirectory}, the working directory the command was given, so the child did not write it at all"
                ),
                _ => string.Create(
                    CultureInfo.InvariantCulture,
                    $"a file named {name} exists at {string.Join(" and ", found)}, not at the path the probe watched ({pidFile}): the child wrote it in a different working directory"
                ),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"the search for {name} under {WorkingDirectory} failed: {exception.GetType().Name}: {exception.Message}"
            );
        }
    }
}
