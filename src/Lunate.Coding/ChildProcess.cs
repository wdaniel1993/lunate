using System.IO;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// One child process started with captured standard streams and no terminal. A tool-call child
/// must never own the terminal: its stdin is at end-of-file and it runs in its own session with no
/// controlling terminal, so it cannot open <c>/dev/tty</c> and reconfigure Lunate's raw mode.
/// </summary>
internal abstract class ChildProcess : IDisposable
{
    /// <summary>The child's process id, for the tree kill.</summary>
    public abstract int Id { get; }

    public abstract StreamReader StandardOutput { get; }

    public abstract StreamReader StandardError { get; }

    /// <summary>The child's stdin writer; null when stdin is already at end-of-file.</summary>
    public abstract StreamWriter? StandardInput { get; }

    /// <summary>Waits for the child to exit and returns its exit code.</summary>
    public abstract Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Kills the whole process tree and reaps the child.</summary>
    public abstract Task KillTreeAsync();

    public abstract void Dispose();

    public static ChildProcess Start(ProcessRunRequest request) =>
        OperatingSystem.IsWindows()
            ? WindowsChildProcess.Spawn(request)
            : UnixChildProcess.Spawn(request);

    /// <summary>
    /// The child's environment: the caller's replacement when one is given, otherwise the inherited
    /// environment, with <see cref="ProcessRunRequest.ExtraEnvironment"/> applied on top either way.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildEnvironment(ProcessRunRequest request)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Environment is null)
        {
            foreach (
                System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()
            )
            {
                if (entry.Key is string name)
                {
                    environment[name] = entry.Value as string ?? string.Empty;
                }
            }
        }
        else
        {
            foreach ((string name, string value) in request.Environment)
            {
                environment[name] = value;
            }
        }

        if (request.ExtraEnvironment is { } extra)
        {
            foreach ((string name, string value) in extra)
            {
                environment[name] = value;
            }
        }

        return environment;
    }
}
