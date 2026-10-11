using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>
/// The interactive trust prompt (line-based v1): it asks on the mode's stderr writer and reads one
/// answer line; only <c>y</c> or <c>yes</c> (case-insensitive) approves. The trust store records an
/// approval once per repository; this prompt is only reached when nothing has approved the content.
/// </summary>
internal sealed class ConsoleTrustPrompt : IExtensionTrustPrompt
{
    private readonly TextWriter _errors;
    private readonly TextReader _input;

    internal ConsoleTrustPrompt(TextWriter errors, TextReader input)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(input);
        _errors = errors;
        _input = input;
    }

    public ValueTask<bool> ApproveAsync(
        ExtensionDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        _errors.WriteLine(
            $"lunate: project extension '{descriptor.Id}' runs code from this repository ({descriptor.Directory}). Load it? [y/N]"
        );
        string? answer = _input.ReadLine();
        return ValueTask.FromResult(Approves(answer));
    }

    private static bool Approves(string? answer)
    {
        string trimmed = answer?.Trim() ?? string.Empty;
        return trimmed.Equals("y", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// The print- and ACP-mode trust prompt: repository code is never auto-approved off a terminal. It
/// refuses, logs why, and points at the interactive path that can approve it.
/// </summary>
internal sealed class NonInteractiveTrustPrompt : IExtensionTrustPrompt
{
    private readonly IExtensionLog _log;

    internal NonInteractiveTrustPrompt(IExtensionLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    public ValueTask<bool> ApproveAsync(
        ExtensionDescriptor descriptor,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        cancellationToken.ThrowIfCancellationRequested();
        _log.Warn(
            $"project extension '{descriptor.Id}' was not trusted; run an interactive session to approve it."
        );
        return ValueTask.FromResult(false);
    }
}
