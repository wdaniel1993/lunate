using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>One submitted text after the input hook chain: the text to use, or a consumed input.</summary>
internal sealed record InputPipelineResult(string Text, bool Consumed);

/// <summary>
/// Runs every submitted text - a new turn or a steering message - through the
/// <see cref="HookRunner.RunInputReceivedAsync"/> chain before anything starts or queues: a
/// transformed text is what proceeds, a consumed input starts and queues nothing. Without a hook
/// runner the text passes through unchanged.
/// </summary>
internal sealed class InputPipeline(HookRunner? runner = null)
{
    private readonly HookRunner? _runner = runner;

    public async ValueTask<InputPipelineResult> ProcessAsync(
        string text,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_runner is not { } runner)
        {
            return new InputPipelineResult(text, Consumed: false);
        }

        InputReceivedResult result = await runner.RunInputReceivedAsync(
            new InputReceivedPayload(text),
            ct
        );
        return result switch
        {
            InputReceivedResult.Consume => new InputPipelineResult(string.Empty, Consumed: true),
            InputReceivedResult.Transform transform => new InputPipelineResult(
                transform.Text,
                Consumed: false
            ),
            _ => new InputPipelineResult(text, Consumed: false),
        };
    }
}
