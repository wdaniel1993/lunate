using System.Text.Json;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace ModelRouterExtension;

/// <summary>The extension instance; holds nothing beyond its id.</summary>
public sealed class ModelRouterExtension(string id) : IExtension
{
    public string Id { get; } = id;
}

/// <summary>
/// The one public factory the loader discovers. It instantiates nothing: it registers the declared
/// provider's consumers — the counting backend middleware, the <c>auto</c> virtual model (selector:
/// request characters over <c>deepThresholdChars</c>, default 4000) and the read-only
/// <c>ask</c> tool — and the <c>router-counters</c> service that exposes the middleware's counts.
/// </summary>
public sealed class ModelRouterExtensionFactory : IExtensionFactory
{
    /// <summary>The default request-size threshold: longer requests route to the deep backend.</summary>
    internal const int DefaultDeepThresholdChars = 4000;

    public IExtension Create(IExtensionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        int threshold = DeepThreshold(context.Settings);
        var counters = new RouterCounters();
        context.RegisterService("router-counters", new RouterCounterService(counters));
        context.RegisterModelMiddleware(
            new ExtensionModelMiddleware(
                ModelMiddlewareSlot.AroundBackend,
                Priority: 100,
                inner => new CountingClient(inner, counters)
            )
        );
        context.RegisterVirtualModel(
            new VirtualModelRegistration(
                "auto",
                ["fast", "deep"],
                route => RequestChars(route.Messages) > threshold ? "deep" : "fast"
            )
        );

        string autoModelId = $"ext/{context.Id}/auto";
        context.RegisterTool(
            new ExtensionToolRegistration
            {
                Name = "ask",
                Description =
                    "Sends one prompt through the auto-routing model (short requests to fast, long ones to deep) and returns the reply text.",
                ParametersSchema = Schema,
                Annotations = new ToolAnnotations(ReadOnly: true),
                Handler = new RouterAskHandler(autoModelId),
            }
        );

        return new ModelRouterExtension(context.Id);
    }

    private static int DeepThreshold(IExtensionSettings settings)
    {
        if (
            settings.TryGet("deepThresholdChars", out JsonElement value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out int threshold)
            && threshold >= 0
        )
        {
            return threshold;
        }

        return DefaultDeepThresholdChars;
    }

    /// <summary>The request size the selector compares: the messages' text characters.</summary>
    private static int RequestChars(IReadOnlyList<ChatMessage> messages)
    {
        var chars = 0;
        foreach (ChatMessage message in messages)
        {
            foreach (AIContent content in message.Contents)
            {
                if (content is TextContent text)
                {
                    chars += text.Text.Length;
                }
            }
        }

        return chars;
    }

    private static JsonElement Schema
    {
        get
        {
            using JsonDocument document = JsonDocument.Parse(
                """
                {
                  "type": "object",
                  "properties": {
                    "prompt": {
                      "type": "string",
                      "description": "the prompt to send through the routed model"
                    }
                  },
                  "required": ["prompt"],
                  "additionalProperties": false
                }
                """
            );
            return document.RootElement.Clone();
        }
    }

    /// <summary>
    /// The read-only tool: one nested model call through <c>ToolContext.Models</c>, so the usage is
    /// charged to the calling run and the routing is reported to it.
    /// </summary>
    private sealed class RouterAskHandler(string autoModelId) : IExtensionToolHandler
    {
        public async Task<ToolResult> InvokeAsync(
            JsonElement arguments,
            ToolContext context,
            CancellationToken ct
        )
        {
            if (context.Models is null)
            {
                return new ToolResult(
                    "No model access in this run; ask needs a run context.",
                    IsError: true
                );
            }

            if (
                arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty("prompt", out JsonElement promptElement)
                || promptElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(promptElement.GetString())
            )
            {
                return new ToolResult(
                    "'prompt' is required and must be a non-empty string.",
                    IsError: true
                );
            }

            ChatResponse response = await context
                .Models.For(autoModelId)
                .GetResponseAsync(
                    [new ChatMessage(ChatRole.User, promptElement.GetString())],
                    null,
                    ct
                )
                .ConfigureAwait(false);
            return new ToolResult(response.Text ?? string.Empty, IsError: false);
        }
    }
}
