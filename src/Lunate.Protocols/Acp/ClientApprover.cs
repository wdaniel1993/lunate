using System.Globalization;
using System.Text.Json;
using Acp;
using Acp.Schema;
using Lunate.Agent;

namespace Lunate.Protocols.Acp;

/// <summary>
/// The client-backed approver: one <c>session/request_permission</c> per call that reaches it,
/// offering allow-once, allow-always, reject-once and reject-always. Allow-always and reject-always
/// are remembered per tool name for the session; a cancelled request or a request error resolves as
/// a decline (the safe default).
/// </summary>
internal sealed class ClientApprover : IToolApprover
{
    internal const int DefaultPermissionTimeoutSeconds = 600;

    private static readonly IReadOnlyList<PermissionOption> OfferedOptions =
    [
        new PermissionOption
        {
            OptionId = new PermissionOptionId("allow-once"),
            Name = "Allow once",
            Kind = PermissionOptionKind.AllowOnce,
        },
        new PermissionOption
        {
            OptionId = new PermissionOptionId("allow-always"),
            Name = "Allow always",
            Kind = PermissionOptionKind.AllowAlways,
        },
        new PermissionOption
        {
            OptionId = new PermissionOptionId("reject-once"),
            Name = "Reject once",
            Kind = PermissionOptionKind.RejectOnce,
        },
        new PermissionOption
        {
            OptionId = new PermissionOptionId("reject-always"),
            Name = "Reject always",
            Kind = PermissionOptionKind.RejectAlways,
        },
    ];

    private readonly AgentSideConnection _connection;
    private readonly SessionId _sessionId;
    private readonly Action<string>? _log;
    private readonly TimeSpan _permissionTimeout;
    private readonly object _gate = new();
    private readonly HashSet<string> _always = new(StringComparer.Ordinal);
    private readonly HashSet<string> _never = new(StringComparer.Ordinal);
    private int _requests;

    public ClientApprover(
        AgentSideConnection connection,
        SessionId sessionId,
        Action<string>? log = null,
        TimeSpan? permissionTimeout = null
    )
    {
        ArgumentNullException.ThrowIfNull(connection);
        _connection = connection;
        _sessionId = sessionId;
        _log = log;
        _permissionTimeout =
            permissionTimeout ?? TimeSpan.FromSeconds(DefaultPermissionTimeoutSeconds);
    }

    /// <summary>Returns true to execute the call, false to decline it.</summary>
    public async ValueTask<bool> ApproveAsync(ITool tool, JsonElement args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tool);

        lock (_gate)
        {
            if (_always.Contains(tool.Name))
            {
                return true;
            }

            if (_never.Contains(tool.Name))
            {
                return false;
            }
        }

        var request = new RequestPermissionRequest
        {
            SessionId = _sessionId,
            ToolCall = new ToolCallUpdate
            {
                ToolCallId = new ToolCallId(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"permission-{Interlocked.Increment(ref _requests)}"
                    )
                ),
                Title = Title(tool, args),
                Kind = KindOf(tool.Risk),
                Status = ToolCallStatus.Pending,
                RawInput = args.ValueKind == JsonValueKind.Undefined ? null : args.Clone(),
            },
            Options = OfferedOptions,
        };

        RequestPermissionResponse response;
        var pending = _connection.RequestPermissionAsync(request, ct);
        try
        {
            response = await pending.WaitAsync(_permissionTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The request is abandoned: observe a late failure so it cannot surface as an
            // unobserved task exception. A late cancellation needs no observation.
            _ = pending.ContinueWith(
                static task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted
                    | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default
            );
            Log(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"request_permission {tool.Name}: no answer within {_permissionTimeout.TotalSeconds}s; the call is declined"
                )
            );
            return false;
        }
        catch (OperationCanceledException)
        {
            Log($"request_permission {tool.Name}: cancelled; the call is declined");
            return false;
        }
        catch (Exception exception)
        {
            Log($"request_permission {tool.Name}: {exception.Message}; the call is declined");
            return false;
        }

        if (response.Outcome is not SelectedPermissionOutcome selected)
        {
            return false;
        }

        PermissionOption? option = null;
        foreach (PermissionOption candidate in OfferedOptions)
        {
            if (candidate.OptionId == selected.OptionId)
            {
                option = candidate;
                break;
            }
        }

        if (option is null)
        {
            Log($"request_permission {tool.Name}: unknown outcome; the call is declined");
            return false;
        }

        if (option.Kind == PermissionOptionKind.AllowAlways)
        {
            lock (_gate)
            {
                _always.Add(tool.Name);
            }
        }
        else if (option.Kind == PermissionOptionKind.RejectAlways)
        {
            lock (_gate)
            {
                _never.Add(tool.Name);
            }
        }

        return option.Kind is PermissionOptionKind.AllowOnce or PermissionOptionKind.AllowAlways;
    }

    private static string Title(ITool tool, JsonElement args)
    {
        string summary = Summary(args);
        return summary.Length == 0
            ? tool.Name
            : string.Create(CultureInfo.InvariantCulture, $"{tool.Name}: {summary}");
    }

    private static string Summary(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Object)
        {
            if (
                args.TryGetProperty("path", out JsonElement path)
                && path.ValueKind == JsonValueKind.String
            )
            {
                return path.GetString()!;
            }

            if (
                args.TryGetProperty("command", out JsonElement command)
                && command.ValueKind == JsonValueKind.String
            )
            {
                return command.GetString()!;
            }
        }

        if (args.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return string.Empty;
        }

        string raw = args.GetRawText();
        return raw.Length <= 80 ? raw : raw[..80] + "...";
    }

    private static ToolKind KindOf(ToolRisk risk) =>
        risk switch
        {
            ToolRisk.ReadOnly => ToolKind.Read,
            ToolRisk.Write => ToolKind.Edit,
            ToolRisk.Execute => ToolKind.Execute,
            _ => ToolKind.Other,
        };

    private void Log(string message) => _log?.Invoke(message);
}
