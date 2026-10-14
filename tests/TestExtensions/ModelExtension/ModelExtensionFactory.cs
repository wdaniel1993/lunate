using Lunate.Extensibility.Abstractions;
using Microsoft.Extensions.AI;

namespace ModelExtension;

public sealed class ModelExtension : IExtension;

/// <summary>
/// A fixture extension that exercises the load-time model registration members: a programmatic
/// provider, a BeforeRouting middleware that tags responses, and a virtual model over the backend.
/// </summary>
public sealed class ModelExtensionFactory : IExtensionFactory
{
    public IExtension Create(IExtensionContext context)
    {
        context.RegisterModelProvider(
            new ExtensionModelProvider("fixture", ["fixture-model"], () => new FixtureChatClient())
        );
        context.RegisterModelMiddleware(
            new ExtensionModelMiddleware(
                ModelMiddlewareSlot.BeforeRouting,
                0,
                inner => new TaggingClient(inner)
            )
        );
        context.RegisterVirtualModel(
            new VirtualModelRegistration("auto", ["fixture-model"], _ => "fixture-model")
        );
        return new ModelExtension();
    }

    private sealed class TaggingClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            ChatResponse response = await base.GetResponseAsync(
                    messages,
                    options,
                    cancellationToken
                )
                .ConfigureAwait(false);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "tagged:" + response.Text))
            {
                ModelId = response.ModelId,
            };
        }
    }

    private sealed class FixtureChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, "fixture reply"))
                {
                    ModelId = "fixture-model",
                }
            );

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "fixture reply")
            {
                ModelId = "fixture-model",
            };
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
