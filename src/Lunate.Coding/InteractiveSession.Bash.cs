using System.Globalization;
using System.Text.Json;
using Lunate.Agent;
using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private bool _bashMode;
    private bool _bashSilent;
    private bool _bashRunning;
    private long _bashCallSequence;
    private Task? _bashRun;
    private CancellationTokenSource? _bashCancellation;

    /// <summary>Whether the input line is in bash mode (the tests' view).</summary>
    internal bool IsBashMode => _bashMode;

    /// <summary>Whether bash mode is the silent form (the tests' view).</summary>
    internal bool IsBashSilent => _bashSilent;

    /// <summary>Whether a bash-mode command is running (the tests' view).</summary>
    internal bool IsBashRunning => _bashRunning;

    /// <summary>
    /// Enters bash mode when the first character of an empty input is <c>!</c>, or flips an
    /// already-entered empty normal form to silent when the next character is a second <c>!</c>.
    /// Returns true when the mode changed and the live input was updated.
    /// </summary>
    private bool TryEnterBashMode(bool wasEmpty, KeyEvent key)
    {
        if (!wasEmpty)
        {
            return false;
        }

        if (!_bashMode)
        {
            if (!_input.Text.StartsWith('!'))
            {
                return false;
            }

            bool silent = _input.Text.StartsWith("!!");
            string command = _input.Text[(silent ? 2 : 1)..];
            _bashMode = true;
            _bashSilent = silent;
            _input = new InputLineState(command, command.Length);
            CloseAutocomplete();
            _live.SetInput(_input, bashMode: true);
            return true;
        }

        if (!_bashSilent && key is { Kind: KeyKind.Character, Text: "!" } && _input.Text == "!")
        {
            _bashSilent = true;
            _input = InputLine.Empty;
            CloseAutocomplete();
            _live.SetInput(_input, bashMode: true);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Leaves bash mode. Restoring puts the prefix back in front of the typed line, so Esc never
    /// loses it; submitting clears the line.
    /// </summary>
    private void LeaveBashMode(bool restore)
    {
        string prefix = _bashSilent ? "!!" : "!";
        string text = restore ? prefix + _input.Text : string.Empty;
        _bashMode = false;
        _bashSilent = false;
        _input = new InputLineState(text, text.Length);
        _live.SetInput(_input);
    }

    /// <summary>
    /// The bash-mode submission path: Enter runs the remaining line through the existing bash
    /// tool, and a bare prefix leaves without running.
    /// </summary>
    private Task SubmitBashAsync(CancellationToken ct)
    {
        string command = _input.Text;
        LeaveBashMode(restore: false);
        if (string.IsNullOrWhiteSpace(command))
        {
            return Task.CompletedTask;
        }

        _bashRun = RunBashAsync(command, ct);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs one typed line through the registered bash tool with the same tool context the
    /// harness builds, and commits its result through the session's one tool-block path.
    /// </summary>
    private async Task RunBashAsync(string command, CancellationToken ct)
    {
        if (_tools.Find("bash") is not { } tool)
        {
            Notify("the bash tool is not available; the command was not run", NoticeLevel.Error);
            return;
        }

        long sequence = Interlocked.Increment(ref _bashCallSequence);
        string runId = string.Create(CultureInfo.InvariantCulture, $"bash-{sequence}");
        string callId = string.Create(CultureInfo.InvariantCulture, $"bash-{sequence}-call");
        JsonElement args = JsonSerializer.SerializeToElement(new { command });
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetime.Token,
            ct
        );
        _bashCancellation = cancellation;
        _bashRunning = true;
        HandleBashEvent(new ToolCallStart(runId, callId, tool.Name));
        HandleBashEvent(new ToolCallArgs(runId, callId, args.GetRawText()));

        try
        {
            ToolResult result = await ExecuteBashAsync(
                    tool,
                    args,
                    runId,
                    callId,
                    cancellation.Token
                )
                .ConfigureAwait(false);
            HandleBashEvent(
                new ToolCallResult(runId, callId, result.Output, result.IsError, result.Details)
            );
        }
        finally
        {
            _bashRunning = false;
            _bashCancellation = null;
            _live.SetTool(null);
        }
    }

    private async Task<ToolResult> ExecuteBashAsync(
        ITool tool,
        JsonElement args,
        string runId,
        string callId,
        CancellationToken ct
    )
    {
        try
        {
            if (
                _approver is { } approver
                && !await approver
                    .ApproveAsync(
                        new ApprovalRequest(
                            tool,
                            args,
                            runId,
                            ParentRunId: null,
                            Depth: 0,
                            WorkspaceRoot: _workspace.WorktreeRoot
                        ),
                        ct
                    )
                    .ConfigureAwait(false)
            )
            {
                return new ToolResult($"Denied: '{tool.Name}' was not run.", IsError: true);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (Exception exception)
        {
            return new ToolResult(
                $"Approval for tool '{tool.Name}' failed: {exception.Message}. The call was not run.",
                IsError: true
            );
        }

        try
        {
            var context = new ToolContext(_workspace.WorktreeRoot, NullAgentEvents.Instance)
            {
                RunId = runId,
                CallId = callId,
                Model = _model,
                FileChanges = new SessionFileChangeSink(
                    _extensions?.FileChanges,
                    _workspace.WorktreeRoot,
                    runId
                ),
            };
            return await tool.ExecuteAsync(args, context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Cancelled();
        }
        catch (Exception exception)
        {
            return new ToolResult(
                $"Tool '{tool.Name}' failed: {exception.Message}. Fix the call or try a different approach.",
                IsError: true
            );
        }
    }

    private ToolResult Cancelled()
    {
        Notify("bash command cancelled", NoticeLevel.Warning);
        return new ToolResult(
            "the bash command was cancelled; its output was discarded",
            IsError: true
        );
    }

    /// <summary>The synthetic call events reach the observer seam exactly like a run's events.</summary>
    private void HandleBashEvent(AgentEvent agentEvent)
    {
        _options.EventObserver?.Invoke(agentEvent);
        HandleEvent(agentEvent);
    }

    private sealed class NullAgentEvents : IAgentEvents
    {
        public static readonly NullAgentEvents Instance = new();

        public void Emit(AgentEvent agentEvent) { }
    }

    /// <summary>Stamps the session's workspace id and the synthetic run id onto bash changes.</summary>
    private sealed class SessionFileChangeSink(
        IFileChangeSink? inner,
        string workspaceId,
        string runId
    ) : IFileChangeSink
    {
        public void Notify(FileChangeEvent change) =>
            (inner ?? NullFileChangeSink.Instance).Notify(
                change with
                {
                    WorkspaceId = workspaceId,
                    RunId = runId,
                }
            );
    }
}
