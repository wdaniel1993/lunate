using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Ai;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>Runs one non-interactive command in the resolved shell, in the workspace root.</summary>
public sealed class BashTool : ITool
{
    /// <summary>The model-facing name; also what <see cref="HighRiskCall"/> classifies.</summary>
    internal const string ToolName = "bash";

    private const int CaptureCap = 1_000_000;

    private static readonly ProcessRunner Runner = new();

    /// <summary>
    /// What every child this tool spawns carries besides its inherited environment: the session
    /// marker (design D7's line about install being visible in the command the user approved). It
    /// is additive, so the shell still sees PATH, HOME and whatever else it inherited.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> SessionEnvironment =
        new Dictionary<string, string> { [LunateHome.SessionVariable] = LunateHome.SessionMarker };

    internal static readonly string CapMarker = string.Create(
        CultureInfo.InvariantCulture,
        $"... [output capped at {CaptureCap} bytes; the rest was discarded]"
    );

    private const string NoShellText =
        "no shell found; install bash, Git Bash, pwsh, Windows PowerShell or cmd";

    private const string Rules =
        "Runs in the workspace root; non-interactive (stdin is closed); output decodes as UTF-8; "
        + "the process tree is killed on timeout or cancel.";

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

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
    private readonly BashChangeTracker _changes;
    private readonly BashGuard _guard;
    private readonly IExtensionLog? _log;
    private bool _noticeShown;

    public BashTool(
        Workspace workspace,
        ShellResolver? resolver = null,
        TimeSpan? timeout = null,
        IExtensionLog? log = null
    )
        : this(workspace, resolver, timeout, log, new BashGuard()) { }

    /// <summary>
    /// The seam for the guard: tests substitute a guard whose targets point at a fixture's
    /// directories; the public constructor resolves the live ones.
    /// </summary>
    internal BashTool(
        Workspace workspace,
        ShellResolver? resolver,
        TimeSpan? timeout,
        IExtensionLog? log,
        BashGuard guard
    )
    {
        _workspace = workspace;
        _shell = (resolver ?? new ShellResolver()).Resolve();
        _timeout = timeout ?? DefaultTimeout;
        _changes = new BashChangeTracker(log);
        _guard = guard;
        _log = log;
        Description = _shell is null
            ? $"Run a command in the platform shell (no shell was found on this machine). {Rules}"
            : $"Run a command in {_shell.DisplayName} ({Dialect(_shell.Kind)}). {Rules}";
    }

    public string Name => ToolName;

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

        GuardCommand launch = _guard.Wrap(_shell, _workspace.WorktreeRoot);

        // A refused command never spawns: the refusal states what the guard would have done,
        // in the guard's own words, before the OS error has to say it for it.
        if (_guard.Refusal(commandElement.GetString()!, launch) is { } refusal)
        {
            return new ToolResult(refusal, IsError: true);
        }

        ProcessRunResult run;
        try
        {
            run = await Runner.RunAsync(
                new ProcessRunRequest(
                    launch.FileName,
                    [.. launch.ArgumentsPrefix, commandElement.GetString()!],
                    _workspace.WorktreeRoot,
                    Timeout: _timeout,
                    OutputCapBytes: CaptureCap
                )
                {
                    ExtraEnvironment = SessionEnvironment,
                },
                ct
            );
        }
        catch (OperationCanceledException)
        {
            // A user-cancelled command may have half-written files while the run continues; the
            // scan carries its own short timeout, not the cancelled token.
            await PublishChangesAsync(ctx).ConfigureAwait(false);
            throw;
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

        // The command ran (success, non-zero exit or timeout): report what it changed.
        await PublishChangesAsync(ctx).ConfigureAwait(false);

        NoticeOnce(launch);
        string? hint = _guard.Hint(commandElement.GetString()!, run.Stderr, launch);
        var stdout = WithCapMarker(run.Stdout, run.StdoutCapped);
        var stderr = WithCapMarker(run.Stderr, run.StderrCapped);
        if (run.TimedOut)
        {
            return Result(stdout, stderr, TimeoutFooter(), hint, isError: true);
        }

        var exitCode = run.ExitCode!.Value;
        return Result(
            stdout,
            stderr,
            ExitFooter(exitCode),
            hint,
            exitCode != 0,
            new BashDetails(exitCode)
        );
    }

    /// <summary>
    /// The guard's notice goes to the log, once, and never into the command's output: stdout belongs
    /// to the command, and a prefix on the first result changed the bytes a caller sees (the Windows
    /// pass-through broke UTF-8 and stream-order tests exactly that way). A run without a guard is
    /// still never silent — it is recorded as a warning — but the transcript stays byte-faithful.
    /// </summary>
    private void NoticeOnce(GuardCommand launch)
    {
        if (launch.Notice is not { } notice || _noticeShown)
        {
            return;
        }

        _noticeShown = true;
        _log?.Warn(notice);
    }

    /// <summary>
    /// Publishes the run's bash-made changes (the git-status diffed scan; source <c>bash</c>).
    /// Never affects the tool result: failures are silent and the content-stamp scan stays the
    /// backstop. The scan keeps its own short timeout, so a cancelled call is still scanned.
    /// </summary>
    private async Task PublishChangesAsync(ToolContext ctx)
    {
        if (_workspace.GitCommonDir is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<BashChangeTracker.Change> changes = await _changes
                .ObserveAsync(ctx.RunId, ctx.WorkingDirectory, CancellationToken.None)
                .ConfigureAwait(false);
            foreach (BashChangeTracker.Change change in changes)
            {
                string absolute = Workspace.Canonicalize(
                    Path.Combine(
                        ctx.WorkingDirectory,
                        change.RelativePath.Replace('/', Path.DirectorySeparatorChar)
                    )
                );
                ctx.FileChanges.Notify(
                    new FileChangeEvent(
                        absolute,
                        change.RelativePath,
                        ctx.WorkingDirectory,
                        ctx.RunId,
                        change.Kind,
                        Source: "bash"
                    )
                );
            }
        }
        catch (Exception)
        {
            // The change report must never affect the tool's result.
        }
    }

    private static string WithCapMarker(string text, bool capped) =>
        capped ? text + "\n" + CapMarker : text;

    private static ToolResult Result(
        string stdout,
        string stderr,
        string footer,
        string? hint,
        bool isError,
        object? details = null
    ) =>
        new(BashOutput.Answer(stdout, stderr, footer, hint), isError, details)
        {
            FullOutput = BashOutput.Transcript(stdout, stderr),
        };

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
