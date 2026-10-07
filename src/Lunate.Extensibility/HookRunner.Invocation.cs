namespace Lunate.Extensibility;

public sealed partial class HookRunner
{
    private enum HandlerStatus
    {
        Completed,
        Failed,
        TimedOut,
    }

    private readonly record struct HandlerOutcome<T>(HandlerStatus Status, T? Value, string? Error)
    {
        public static HandlerOutcome<T> Ok(T value) => new(HandlerStatus.Completed, value, null);

        public static HandlerOutcome<T> Failed(Exception exception) =>
            new(
                HandlerStatus.Failed,
                default,
                $"{exception.GetType().FullName}: {exception.Message}"
            );

        public static HandlerOutcome<T> TimedOut() => new(HandlerStatus.TimedOut, default, null);
    }

    private async ValueTask<HandlerOutcome<bool>> CallVoidAsync(
        Func<CancellationToken, ValueTask> call,
        TimeSpan timeout,
        CancellationToken ct
    ) =>
        await CallAsync(
                async token =>
                {
                    await call(token).ConfigureAwait(false);
                    return true;
                },
                timeout,
                ct
            )
            .ConfigureAwait(false);

    private async ValueTask<HandlerOutcome<T>> CallAsync<T>(
        Func<CancellationToken, ValueTask<T>> call,
        TimeSpan timeout,
        CancellationToken ct
    )
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<T> task;
        try
        {
            task = call(timeoutSource.Token).AsTask();
        }
        catch (Exception exception)
        {
            if (ct.IsCancellationRequested && exception is OperationCanceledException)
            {
                throw;
            }

            return HandlerOutcome<T>.Failed(exception);
        }

        Task winner = await Task.WhenAny(task, Task.Delay(timeout, timeoutSource.Token))
            .ConfigureAwait(false);
        if (winner == task)
        {
            try
            {
                return HandlerOutcome<T>.Ok(await task.ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return HandlerOutcome<T>.Failed(exception);
            }
        }

        timeoutSource.Cancel();
        Observe(task);
        if (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }

        return HandlerOutcome<T>.TimedOut();
    }

    private static void Observe(Task task) =>
        _ = task.ContinueWith(static completed => _ = completed.Exception, TaskScheduler.Default);
}
