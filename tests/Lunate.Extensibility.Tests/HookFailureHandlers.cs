using System.Text.Json;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

internal static class HookFailureHandlers
{
    public static Func<TPayload, CancellationToken, ValueTask> FailVoid<TPayload>(bool hanging) =>
        hanging
            ? (_, _) => new ValueTask(new TaskCompletionSource<bool>().Task)
            : (_, _) => throw new InvalidOperationException("boom");

    public static Func<TPayload, CancellationToken, ValueTask<TResult>> Fail<TPayload, TResult>(
        bool hanging
    ) =>
        hanging
            ? (_, _) => new ValueTask<TResult>(new TaskCompletionSource<TResult>().Task)
            : (_, _) => throw new InvalidOperationException("boom");

    public static ValueTask HealthyObserve(PolicyObservation observation)
    {
        observation.HealthyRan = true;
        return ValueTask.CompletedTask;
    }

    internal sealed class PolicyObservation
    {
        public bool HealthyRan { get; set; }

        public object? Result { get; set; }

        public IReadOnlyList<string> Log { get; set; } = [];
    }
}
