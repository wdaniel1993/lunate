using System.Globalization;
using System.Text;
using System.Text.Json;
using Lunate.Agent;

namespace CodeModeExtension;

/// <summary>One bridged call as the structured result reports it.</summary>
internal sealed record BridgedCall(string? CallId, string Tool, bool IsError);

/// <summary>
/// The protocol half of the bridge: parses the child's stdout frames, records logs, the final
/// result and the bridged calls, and answers every <c>call</c> frame through
/// <see cref="ToolContext.ExecuteToolAsync"/> — so approvals, hooks, cancellation and nested ids
/// stay in the core pipeline. A malformed protocol line becomes an error result, never a crash or
/// a phantom call; a call beyond the cap is refused with a teaching error frame.
/// </summary>
internal sealed class CodeModeBridge(ToolContext context, CodeModeSettings settings)
{
    private static readonly JsonElement EmptyArgs = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly List<BridgedCall> _calls = [];
    private readonly StringBuilder _logs = new();
    private int _logBytes;
    private bool _logsCapped;
    private bool _helloSeen;
    private bool _finalSeen;
    private string? _protocolError;
    private int _callCount;
    private JsonElement? _result;
    private bool _failed;

    /// <summary>The first protocol violation, when one happened.</summary>
    public string? ProtocolError => _protocolError;

    /// <summary>True once the hello handshake was accepted.</summary>
    public bool HelloSeen => _helloSeen;

    /// <summary>The captured log text, capped at the configured byte budget.</summary>
    public string Logs => _logs.ToString();

    /// <summary>True when the log budget was reached and later output was discarded.</summary>
    public bool LogsCapped => _logsCapped;

    /// <summary>The executed bridged calls, in execution order.</summary>
    public IReadOnlyList<BridgedCall> Calls => _calls;

    /// <summary>The script's final value, when it returned one.</summary>
    public JsonElement? Result => _result;

    /// <summary>True when the script threw or set a failing exit code.</summary>
    public bool ScriptFailed => _failed;

    /// <summary>What this run observed, for <see cref="CodeModeOutcome.Compose"/>.</summary>
    public CodeModeRunFacts Snapshot() =>
        new([.. _calls], _logs.ToString(), _result, _failed, _helloSeen, _protocolError);

    /// <summary>Handles one stdout line; returns the stdin answer to write, or null for none.</summary>
    public async Task<string?> OnLineAsync(string line, CancellationToken ct)
    {
        if (_protocolError is not null)
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return ProtocolFailure(
                $"the runtime sent a line that is not a protocol frame: {Excerpt(line)}"
            );
        }

        using (document)
        {
            return await HandleFrameAsync(document.RootElement, ct).ConfigureAwait(false);
        }
    }

    private async Task<string?> HandleFrameAsync(JsonElement frame, CancellationToken ct)
    {
        if (
            frame.ValueKind != JsonValueKind.Object
            || !frame.TryGetProperty("type", out JsonElement typeElement)
            || typeElement.ValueKind != JsonValueKind.String
        )
        {
            return ProtocolFailure("a protocol frame carries no string 'type'");
        }

        string type = typeElement.GetString()!;
        if (_finalSeen)
        {
            return ProtocolFailure($"the runtime sent a '{type}' frame after the final frame");
        }

        if (!_helloSeen && type != "hello")
        {
            return ProtocolFailure($"the first frame was '{type}', not the hello handshake");
        }

        return type switch
        {
            "hello" => HandleHello(frame),
            "log" => HandleLog(frame),
            "call" => await HandleCallAsync(frame, ct).ConfigureAwait(false),
            "final" => HandleFinal(frame),
            _ => ProtocolFailure($"the runtime sent an unknown frame type '{type}'"),
        };
    }

    private string? HandleHello(JsonElement frame)
    {
        if (
            !frame.TryGetProperty("protocol", out JsonElement protocol)
            || protocol.ValueKind != JsonValueKind.Number
            || !protocol.TryGetInt32(out int version)
        )
        {
            return ProtocolFailure("the hello frame carries no protocol number");
        }

        if (version != 1)
        {
            return ProtocolFailure(
                $"the runtime speaks protocol {version}; this extension speaks 1"
            );
        }

        _helloSeen = true;
        return null;
    }

    private string? HandleLog(JsonElement frame)
    {
        if (
            !frame.TryGetProperty("text", out JsonElement text)
            || text.ValueKind != JsonValueKind.String
        )
        {
            return ProtocolFailure("a log frame carries no string 'text'");
        }

        AppendLog(text.GetString()!);
        return null;
    }

    private async Task<string?> HandleCallAsync(JsonElement frame, CancellationToken ct)
    {
        if (
            !frame.TryGetProperty("id", out JsonElement idElement)
            || idElement.ValueKind != JsonValueKind.Number
            || !idElement.TryGetInt32(out int id)
        )
        {
            return ProtocolFailure("a call frame carries no numeric 'id'");
        }

        if (
            !frame.TryGetProperty("tool", out JsonElement toolElement)
            || toolElement.ValueKind != JsonValueKind.String
        )
        {
            return ProtocolFailure("a call frame carries no string 'tool'");
        }

        string tool = toolElement.GetString()!;
        if (_callCount >= settings.MaxToolCalls)
        {
            return ResultFrame(id, isError: true, output: ToolCallLimitText(tool));
        }

        _callCount++;
        JsonElement args =
            frame.TryGetProperty("args", out JsonElement argsElement)
            && argsElement.ValueKind == JsonValueKind.Object
                ? argsElement.Clone()
                : EmptyArgs;

        ToolResult result;
        try
        {
            result = await context.ExecuteToolAsync!(tool, args, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            result = new ToolResult(
                $"The bridged call to '{tool}' failed: {exception.Message}",
                IsError: true
            );
        }

        _calls.Add(new BridgedCall(result.CallId, tool, result.IsError));
        return ResultFrame(id, result.IsError, result.Output ?? string.Empty);
    }

    private string? HandleFinal(JsonElement frame)
    {
        _finalSeen = true;
        _failed =
            frame.TryGetProperty("failed", out JsonElement failed)
            && failed.ValueKind == JsonValueKind.True;
        if (
            !_failed
            && frame.TryGetProperty("result", out JsonElement result)
            && result.ValueKind != JsonValueKind.Null
        )
        {
            _result = result.Clone();
        }

        return null;
    }

    private void AppendLog(string text)
    {
        if (_logsCapped)
        {
            return;
        }

        int size = Encoding.UTF8.GetByteCount(text);
        if (_logBytes + size > settings.MaxOutputBytes)
        {
            _logsCapped = true;
            _logs
                .Append('\n')
                .Append(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"... [output capped at {settings.MaxOutputBytes} bytes; the rest was discarded]"
                    )
                )
                .Append('\n');
            return;
        }

        _logBytes += size;
        _logs.Append(text);
    }

    private string ToolCallLimitText(string tool) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Tool-call limit reached: a script may bridge at most {settings.MaxToolCalls} tool calls (the 'maxToolCalls' setting); the call to '{tool}' was not executed. Finish with the data you have or raise the limit."
        );

    private static string ResultFrame(int id, bool isError, string output) =>
        JsonSerializer.Serialize(
            new
            {
                type = "result",
                id,
                isError,
                output,
            }
        );

    private string? ProtocolFailure(string message)
    {
        _protocolError ??= message;
        return null;
    }

    private static string Excerpt(string line) => line.Length <= 120 ? line : line[..120] + "...";
}
