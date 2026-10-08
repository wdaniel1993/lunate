using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding;

/// <summary>Runs one non-interactive command in the resolved shell, in the workspace root.</summary>
public sealed class BashTool : ITool
{
    private const int CaptureCap = 1_000_000;

    internal static readonly string CapMarker = string.Create(
        CultureInfo.InvariantCulture,
        $"... [output capped at {CaptureCap} characters; the rest was discarded]"
    );

    private const string NoShellText =
        "no shell found; install bash, Git Bash, pwsh, Windows PowerShell or cmd";

    private const string Rules =
        "Runs in the workspace root; non-interactive (stdin is closed); output decodes as UTF-8; "
        + "the process tree is killed on timeout or cancel.";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonElement Schema = JsonDocument
        .Parse(
            """
            {
              "type": "object",
              "properties": {
                "command": {
                  "type": "string",
                  "description": "Shell command line to run through the resolved shell"
                }
              },
              "required": ["command"]
            }
            """
        )
        .RootElement.Clone();

    private readonly Workspace _workspace;
    private readonly ResolvedShell? _shell;
    private readonly TimeSpan _timeout;

    public BashTool(Workspace workspace, ShellResolver? resolver = null, TimeSpan? timeout = null)
    {
        _workspace = workspace;
        _shell = (resolver ?? new ShellResolver()).Resolve();
        _timeout = timeout ?? DefaultTimeout;
        Description = _shell is null
            ? $"Run a command in the platform shell (no shell was found on this machine). {Rules}"
            : $"Run a command in {_shell.DisplayName} ({Dialect(_shell.Kind)}). {Rules}";
    }

    public string Name => "bash";

    public string Description { get; }

    public JsonElement ParametersSchema => Schema;

    public ToolRisk Risk => ToolRisk.Execute;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        if (args.ValueKind != JsonValueKind.Object)
        {
            return Error("arguments must be a JSON object");
        }

        if (
            !args.TryGetProperty("command", out var commandElement)
            || commandElement.ValueKind != JsonValueKind.String
        )
        {
            return Error("command is required and must be a string");
        }

        if (_shell is null)
        {
            return Error(NoShellText);
        }

        ct.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = _shell.ExecutablePath,
            WorkingDirectory = _workspace.WorktreeRoot,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var prefix in _shell.ArgumentsPrefix)
        {
            startInfo.ArgumentList.Add(prefix);
        }

        startInfo.ArgumentList.Add(commandElement.GetString()!);

        Process process;
        try
        {
            process =
                Process.Start(startInfo)
                ?? throw new InvalidOperationException("the process did not start");
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
            var message = string.Create(
                CultureInfo.InvariantCulture,
                $"could not start {_shell.DisplayName} ({_shell.ExecutablePath}): {exception.Message}; check the shell installation"
            );
            return Error(message);
        }

        using var running = process;
        process.StandardInput.Close();
        var stdoutTask = ReadBoundedAsync(process.StandardOutput);
        var stderrTask = ReadBoundedAsync(process.StandardError);

        var timedOut = false;
        var stdout = string.Empty;
        var stderr = string.Empty;
        using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeoutCts.CancelAfter(_timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                timedOut = true;
                await KillTreeAsync(process);
                (stdout, stderr) = await DrainAsync(stdoutTask, stderrTask);
            }
            catch (OperationCanceledException)
            {
                await KillTreeAsync(process);
                await DrainAsync(stdoutTask, stderrTask);
                throw;
            }
        }

        if (timedOut)
        {
            return Error(ComposeText(stdout, stderr, TimeoutFooter()));
        }

        stdout = await stdoutTask;
        stderr = await stderrTask;
        var exitCode = process.ExitCode;
        return new ToolResult(ComposeText(stdout, stderr, ExitFooter(exitCode)), exitCode != 0);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var builder = new StringBuilder();
        var buffer = new char[8192];
        var capped = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                return builder.ToString();
            }

            if (capped)
            {
                continue;
            }

            var remaining = CaptureCap - builder.Length;
            if (read <= remaining)
            {
                builder.Append(buffer, 0, read);
                continue;
            }

            builder.Append(buffer, 0, remaining);
            builder.Append('\n').Append(CapMarker);
            capped = true;
        }
    }

    private static async Task KillTreeAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        { }

        try
        {
            await process
                .WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException) { }
    }

    private static async Task<(string Stdout, string Stderr)> DrainAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask
    )
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException) { }

        return (
            stdoutTask.Status == TaskStatus.RanToCompletion ? stdoutTask.Result : string.Empty,
            stderrTask.Status == TaskStatus.RanToCompletion ? stderrTask.Result : string.Empty
        );
    }

    private static string ComposeText(string stdout, string stderr, string footer)
    {
        var text = new StringBuilder();
        text.Append(stdout);
        if (stderr.Length > 0)
        {
            if (text.Length > 0 && text[^1] != '\n')
            {
                text.Append('\n');
            }

            text.Append("stderr:\n").Append(stderr);
        }

        if (text.Length > 0 && text[^1] != '\n')
        {
            text.Append('\n');
        }

        return text.Append(footer).ToString();
    }

    private static string ExitFooter(int exitCode) =>
        string.Create(CultureInfo.InvariantCulture, $"exit code: {exitCode}");

    private string TimeoutFooter() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"timed out after {_timeout.TotalSeconds:0.###}s; process tree killed"
        );

    private static string Dialect(ShellKind kind) =>
        kind switch
        {
            ShellKind.Bash or ShellKind.GitBash => "bash dialect",
            ShellKind.Sh => "POSIX sh dialect",
            ShellKind.Pwsh7 or ShellKind.WindowsPowerShell => "PowerShell dialect",
            ShellKind.Cmd => "Windows cmd dialect",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown shell kind"),
        };

    private static ToolResult Error(string output) => new(output, IsError: true);
}
