namespace Lunate.Agent.Tests;

public sealed class FileMutationQueueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Shared_is_the_process_wide_default_queue()
    {
        Assert.Same(FileMutationQueue.Shared, FileMutationQueue.Shared);
    }

    [Fact]
    public async Task Mutations_on_one_path_run_strictly_one_after_another()
    {
        var queue = new FileMutationQueue();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        List<string> order = [];
        Task<int> first = queue.RunAsync(
            "data/a.txt",
            async _ =>
            {
                order.Add("first-enter");
                firstEntered.SetResult();
                await releaseFirst.Task;
                order.Add("first-exit");
                return 1;
            },
            Ct
        );
        await firstEntered.Task;
        Task<int> second = queue.RunAsync(
            "data/a.txt",
            _ =>
            {
                order.Add("second");
                return Task.FromResult(2);
            },
            Ct
        );

        releaseFirst.SetResult();
        int[] results = await Task.WhenAll(first, second);

        Assert.Equal(["first-enter", "first-exit", "second"], order);
        Assert.Equal([1, 2], results);
    }

    [Fact]
    public async Task Mutations_on_different_paths_are_not_blocked_by_each_other()
    {
        var queue = new FileMutationQueue();
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var secondEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task<int> first = queue.RunAsync(
            "data/a.txt",
            async _ =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
                return 1;
            },
            Ct
        );
        await firstEntered.Task;
        Task<int> second = queue.RunAsync(
            "data/b.txt",
            _ =>
            {
                secondEntered.SetResult();
                return Task.FromResult(2);
            },
            Ct
        );

        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        releaseFirst.SetResult();
        int[] results = await Task.WhenAll(first, second);

        Assert.Equal([1, 2], results);
    }

    [Fact]
    public async Task The_path_comparison_follows_the_platform_case_rule()
    {
        var queue = new FileMutationQueue();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task<int> first = queue.RunAsync(
            "Case.txt",
            async _ =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
                return 1;
            },
            Ct
        );
        await firstEntered.Task;
        var secondEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Task<int> second = queue.RunAsync(
            "case.txt",
            _ =>
            {
                secondEntered.SetResult();
                return Task.FromResult(2);
            },
            Ct
        );

        if (OperatingSystem.IsLinux())
        {
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        }
        else
        {
            Assert.False(secondEntered.Task.IsCompleted);
        }

        releaseFirst.SetResult();
        int[] results = await Task.WhenAll(first, second);

        Assert.Equal([1, 2], results);
    }
}
