using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunate.Agent;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using LunateAnnotations = Lunate.Agent.ToolAnnotations;
using McpAnnotations = ModelContextProtocol.Protocol.ToolAnnotations;

namespace Lunate.Protocols;

/// <summary>
/// One MCP tool wrapped as an <see cref="ITool"/>. The name is <c>server__tool</c> and the risk is
/// always <see cref="ToolRisk.Execute"/> so the approval policy applies. Calls map the model's JSON
/// arguments to <c>tools/call</c>; cancellation is propagated as <c>notifications/cancelled</c>
/// (the SDK does not yet send it for an in-flight call; see modelcontextprotocol/csharp-sdk#1365),
/// a timeout becomes an error result, and transport or protocol failures become error results
/// naming the server.
/// </summary>
public sealed class McpToolAdapter : ITool
{
    private readonly McpClient client;
    private readonly string serverName;
    private readonly string toolName;
    private readonly TimeSpan callTimeout;
    private readonly LunateAnnotations? annotations;

    internal McpToolAdapter(
        McpClient client,
        string serverName,
        McpClientTool tool,
        TimeSpan callTimeout
    )
    {
        this.client = client;
        this.serverName = serverName;
        this.callTimeout = callTimeout;
        toolName = tool.ProtocolTool.Name;
        Description = tool.ProtocolTool.Description ?? string.Empty;
        ParametersSchema = tool.ProtocolTool.InputSchema.Clone();
        annotations = MapAnnotations(tool.ProtocolTool.Annotations);
    }

    public string Name => $"{serverName}__{toolName}";

    public string Description { get; }

    public JsonElement ParametersSchema { get; }

    public ToolRisk Risk => ToolRisk.Execute;

    public LunateAnnotations? Annotations => annotations;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct
    )
    {
        var request = new JsonRpcRequest
        {
            Id = new RequestId(Guid.NewGuid().ToString("N")),
            Method = RequestMethods.ToolsCall,
            Params = JsonSerializer.SerializeToNode(
                BuildRequestParams(args),
                McpJsonUtilities.DefaultOptions
            ),
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(callTimeout);

        JsonRpcResponse response;
        try
        {
            response = await client.SendRequestAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (ct.IsCancellationRequested)
            {
                await NotifyCancellationAsync(request.Id).ConfigureAwait(false);
                throw new OperationCanceledException(
                    $"The call to MCP tool '{Name}' was cancelled.",
                    exception,
                    ct
                );
            }

            if (timeout.IsCancellationRequested)
            {
                await NotifyCancellationAsync(request.Id).ConfigureAwait(false);
                return Error(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"MCP server '{serverName}' tool '{toolName}' timed out after {callTimeout.TotalSeconds:0.###}s; the call was cancelled. Check the server or raise the call timeout."
                    )
                );
            }

            return Error(
                $"MCP server '{serverName}' call to '{toolName}' failed: {exception.Message}. Check the server configuration and try again; the tool was not executed."
            );
        }

        return MapCallResult(serverName, toolName, response.Result);
    }

    internal static ToolResult MapCallResult(
        string serverName,
        string toolName,
        JsonNode? resultNode
    )
    {
        try
        {
            var result = resultNode is null
                ? null
                : JsonSerializer.Deserialize<CallToolResult>(
                    resultNode,
                    McpJsonUtilities.DefaultOptions
                );

            if (result is null)
            {
                return Error(
                    $"MCP server '{serverName}' returned an empty result for '{toolName}'. Check the server; the tool was not executed."
                );
            }

            var output = string.Join(
                "\n",
                (result.Content ?? []).OfType<TextContentBlock>().Select(block => block.Text)
            );

            if (result.IsError is true)
            {
                return new ToolResult(output, IsError: true);
            }

            var structured = result.StructuredContent;
            if (structured is { } value)
            {
                if (value.ValueKind != JsonValueKind.Object)
                {
                    return Malformed(
                        serverName,
                        toolName,
                        "structuredContent must be a JSON object"
                    );
                }

                if (output.Length == 0)
                {
                    output = value.GetRawText();
                }
            }

            return new ToolResult(output, IsError: false, StructuredContent: structured?.Clone());
        }
        catch (Exception exception)
        {
            return Malformed(serverName, toolName, exception.Message);
        }
    }

    private static ToolResult Malformed(string serverName, string toolName, string reason) =>
        Error(
            $"MCP server '{serverName}' returned a malformed result for '{toolName}': {reason}. Check the server; the call result could not be read."
        );

    private async Task NotifyCancellationAsync(RequestId requestId)
    {
        try
        {
            await client
                .SendMessageAsync(
                    new JsonRpcNotification
                    {
                        Method = NotificationMethods.CancelledNotification,
                        Params = JsonSerializer.SerializeToNode(
                            new CancelledNotificationParams { RequestId = requestId },
                            McpJsonUtilities.DefaultOptions
                        ),
                    }
                )
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The server or transport is already gone; there is nobody left to notify.
        }
    }

    private CallToolRequestParams BuildRequestParams(JsonElement args)
    {
        var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (args.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in args.EnumerateObject())
            {
                arguments[property.Name] = property.Value.Clone();
            }
        }

        return new CallToolRequestParams { Name = toolName, Arguments = arguments };
    }

    private static LunateAnnotations? MapAnnotations(McpAnnotations? annotations) =>
        annotations is null
            ? null
            : new LunateAnnotations(
                ReadOnly: annotations.ReadOnlyHint ?? false,
                Destructive: annotations.DestructiveHint ?? false,
                Idempotent: annotations.IdempotentHint ?? false,
                OpenWorld: annotations.OpenWorldHint ?? false
            );

    private static ToolResult Error(string output) => new(output, IsError: true);
}
