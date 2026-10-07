using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

public sealed class HookRunnerOrderingTests
{
    [Fact]
    public async Task Handlers_run_by_priority_then_registration_order()
    {
        var runner = new HookRunner();
        List<string> order = [];
        runner.Register("ext-1", SessionStarted("ext-1.low"));
        runner.Register("ext-2", SessionStarted("ext-2.high", priority: 10));
        runner.Register("ext-1", SessionStarted("ext-1.mid", priority: 5));
        runner.Register("ext-3", SessionStarted("ext-3.zero"));

        await runner.RunSessionStartedAsync(
            TestHookPayloads.SessionStarted,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(["ext-2.high", "ext-1.mid", "ext-1.low", "ext-3.zero"], order);

        TestSessionStartedHandler SessionStarted(string name, int priority = 0) =>
            new(
                (_, _) =>
                {
                    order.Add(name);
                    return ValueTask.CompletedTask;
                }
            )
            {
                Priority = priority,
            };
    }

    [Fact]
    public async Task Equal_priorities_keep_registration_order_across_runs()
    {
        var runner = new HookRunner();
        List<string> order = [];
        for (int index = 0; index < 6; index++)
        {
            runner.Register($"ext-{index}", Record($"handler-{index}", order));
        }

        await runner.RunSessionStartedAsync(
            TestHookPayloads.SessionStarted,
            TestContext.Current.CancellationToken
        );
        await runner.RunSessionStartedAsync(
            TestHookPayloads.SessionStarted,
            TestContext.Current.CancellationToken
        );

        string[] expected = [.. Enumerable.Range(0, 6).Select(index => $"handler-{index}")];
        Assert.Equal([.. expected, .. expected], order);

        static TestSessionStartedHandler Record(string name, List<string> order) =>
            new(
                (_, _) =>
                {
                    order.Add(name);
                    return ValueTask.CompletedTask;
                }
            );
    }

    [Fact]
    public async Task Unregister_removes_every_handler_of_the_extension()
    {
        var runner = new HookRunner();
        int calls = 0;
        runner.Register("ext-1", Observe(() => calls++));
        runner.Register("ext-2", Observe(() => calls++));

        runner.Unregister("ext-1");
        await runner.RunSessionStartedAsync(
            TestHookPayloads.SessionStarted,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, calls);

        static TestSessionStartedHandler Observe(Action onCall) =>
            new(
                (_, _) =>
                {
                    onCall();
                    return ValueTask.CompletedTask;
                }
            );
    }
}
