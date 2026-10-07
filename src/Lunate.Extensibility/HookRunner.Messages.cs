using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

public sealed partial class HookRunner
{
    /// <summary>
    /// Runs <see cref="IInputReceivedHandler"/> handlers in order; each handler sees the previous
    /// text. The first <see cref="InputReceivedResult.Consume"/> stops the chain.
    /// </summary>
    public async ValueTask<InputReceivedResult> RunInputReceivedAsync(
        InputReceivedPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        string text = payload.Text;
        foreach (Registration registration in Ordered<IInputReceivedHandler>())
        {
            var handler = (IInputReceivedHandler)registration.Handler;
            HandlerOutcome<InputReceivedResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload with { Text = text }, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure("InputReceived", registration, _options.HandlerTimeout, outcome.Error);
                continue;
            }

            switch (outcome.Value)
            {
                case InputReceivedResult.Consume:
                    return new InputReceivedResult.Consume();
                case InputReceivedResult.Transform transform:
                    text = transform.Text;
                    break;
            }
        }

        return string.Equals(text, payload.Text, StringComparison.Ordinal)
            ? new InputReceivedResult.PassThrough()
            : new InputReceivedResult.Transform(text);
    }

    /// <summary>
    /// Runs <see cref="IMessageCompletedHandler"/> handlers in order; each handler sees the current
    /// text and the final replacement is kept.
    /// </summary>
    public async ValueTask<MessageCompletedResult> RunMessageCompletedAsync(
        MessageCompletedPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        string text = payload.Text;
        foreach (Registration registration in Ordered<IMessageCompletedHandler>())
        {
            var handler = (IMessageCompletedHandler)registration.Handler;
            HandlerOutcome<MessageCompletedResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload with { Text = text }, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure(
                    "MessageCompleted",
                    registration,
                    _options.HandlerTimeout,
                    outcome.Error
                );
                continue;
            }

            if (outcome.Value is MessageCompletedResult.Replace replace)
            {
                text = replace.Text;
            }
        }

        return string.Equals(text, payload.Text, StringComparison.Ordinal)
            ? new MessageCompletedResult.Keep()
            : new MessageCompletedResult.Replace(text);
    }

    /// <summary>
    /// Runs <see cref="IToolResultReadyHandler"/> handlers in order; each handler sees the current
    /// output, and the last attached JSON data wins.
    /// </summary>
    public async ValueTask<ToolResultReadyDispatch> RunToolResultReadyAsync(
        ToolResultReadyPayload payload,
        CancellationToken cancellationToken = default
    )
    {
        string output = payload.Output;
        JsonElement? data = null;
        foreach (Registration registration in Ordered<IToolResultReadyHandler>())
        {
            var handler = (IToolResultReadyHandler)registration.Handler;
            HandlerOutcome<ToolResultReadyResult> outcome = await CallAsync(
                    token => handler.HandleAsync(payload with { Output = output }, token),
                    _options.HandlerTimeout,
                    cancellationToken
                )
                .ConfigureAwait(false);
            if (outcome.Status is not HandlerStatus.Completed)
            {
                LogFailure("ToolResultReady", registration, _options.HandlerTimeout, outcome.Error);
                continue;
            }

            output = outcome.Value!.Output;
            if (outcome.Value.Data is { } attached)
            {
                data = attached;
            }
        }

        return new ToolResultReadyDispatch(output, data);
    }
}
