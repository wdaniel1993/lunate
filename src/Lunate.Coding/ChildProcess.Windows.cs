using System.Diagnostics;
using System.IO;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The Windows half of <see cref="ChildProcess"/>: a redirected-stdio <c>Process.Start</c>, so the
/// child never gets a console input handle (its stdin is a pipe at end-of-file, never the terminal).
/// </summary>
internal sealed class WindowsChildProcess : ChildProcess
{
    private readonly Process _process;

    private WindowsChildProcess(Process process)
    {
        _process = process;
    }

    public override int Id => _process.Id;

    public override StreamReader StandardOutput => _process.StandardOutput;

    public override StreamReader StandardError => _process.StandardError;

    public override StreamWriter? StandardInput => _process.StandardInput;

    public static WindowsChildProcess Spawn(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is { } environment)
        {
            startInfo.Environment.Clear();
            foreach ((string name, string value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        if (request.ExtraEnvironment is { } extra)
        {
            foreach ((string name, string value) in extra)
            {
                startInfo.Environment[name] = value;
            }
        }

        Process process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {request.FileName}");
        return new WindowsChildProcess(process);
    }

    public override async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public override async Task KillTreeAsync()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or System.ComponentModel.Win32Exception
                        or NotSupportedException
            ) { }

        try
        {
            await _process
                .WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (TimeoutException) { }
    }

    public override void Dispose() => _process.Dispose();
}
