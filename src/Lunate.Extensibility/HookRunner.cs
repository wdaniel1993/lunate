using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility;

/// <summary>
/// Dispatches hooks to registered handlers. Handlers run by priority (descending) and keep their
/// registration order on ties. Each hook's semantics class and failure policy is enforced: a
/// handler exception or timeout is reported through the log and never escapes to the caller;
/// caller cancellation still propagates.
/// </summary>
public sealed partial class HookRunner
{
    private readonly HookRunnerOptions _options;
    private readonly IExtensionLog _log;
    private readonly List<Registration> _handlers = [];
    private readonly Dictionary<string, int> _continuations = new(StringComparer.Ordinal);
    private long _sequence;

    public HookRunner(HookRunnerOptions? options = null, IExtensionLog? log = null)
    {
        _options = options ?? new HookRunnerOptions();
        _log = log ?? NullExtensionLog.Instance;
        Validate(_options);
    }

    /// <summary>Registers a handler owned by the extension; the handler is never null.</summary>
    public void Register(string extensionId, IHookHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers.Add(new Registration(extensionId, handler, _sequence++));
    }

    /// <summary>Removes every handler the extension registered.</summary>
    public void Unregister(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        _handlers.RemoveAll(registration =>
            string.Equals(registration.ExtensionId, extensionId, StringComparison.Ordinal)
        );
    }

    private List<Registration> Ordered<THandler>(IReadOnlyCollection<string>? extensionIds = null)
        where THandler : IHookHandler =>
        [
            .. _handlers
                .Where(registration =>
                    registration.Handler is THandler
                    && (
                        extensionIds is null
                        || extensionIds.Contains(registration.ExtensionId, StringComparer.Ordinal)
                    )
                )
                .OrderByDescending(registration => registration.Handler.Priority)
                .ThenBy(registration => registration.Sequence),
        ];

    private static void Validate(HookRunnerOptions options)
    {
        if (options.HandlerTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.HandlerTimeout,
                "HandlerTimeout must be positive."
            );
        }

        if (options.ProviderStreamEventTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ProviderStreamEventTimeout,
                "ProviderStreamEventTimeout must be positive."
            );
        }

        if (options.MaxContinuations < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxContinuations,
                "MaxContinuations must be zero or greater."
            );
        }

        if (options.ContextBudgetPerExtension < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.ContextBudgetPerExtension,
                "ContextBudgetPerExtension must be zero or greater."
            );
        }
    }

    private void LogFailure(string hook, Registration registration, TimeSpan timeout, string? error)
    {
        string handler = registration.Handler.GetType().Name;
        string reason =
            error
            ?? $"timed out after {timeout.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)} ms";
        _log.Error(
            $"hook {hook}: handler {handler} from extension '{registration.ExtensionId}' failed: {reason}; the hook's failure policy applies."
        );
    }

    private void LogBudgetDrop(string hook, Registration registration, int estimatedTokens)
    {
        _log.Warn(
            $"hook {hook}: context added by extension '{registration.ExtensionId}' was dropped: the per-extension budget of {_options.ContextBudgetPerExtension.ToString(CultureInfo.InvariantCulture)} estimated tokens would be exceeded ({estimatedTokens.ToString(CultureInfo.InvariantCulture)} estimated tokens)."
        );
    }

    private static int EstimateTokens(string text) => (text.Length + 3) / 4;

    private sealed record Registration(string ExtensionId, IHookHandler Handler, long Sequence);
}
