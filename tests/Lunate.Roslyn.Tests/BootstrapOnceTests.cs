using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class BootstrapOnceTests
{
    [Fact]
    public void Initialization_runs_once_and_is_cached()
    {
        var calls = 0;
        var once = new BootstrapOnce(() =>
        {
            calls++;
            return new BootstrapResult(Succeeded: true, FailureMessage: null);
        });

        var first = once.EnsureInitialized();
        var second = once.EnsureInitialized();

        Assert.True(first.Succeeded);
        Assert.Equal(first, second);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void A_failed_registration_is_returned_with_its_message()
    {
        var once = new BootstrapOnce(() => new BootstrapResult(false, "install an SDK"));

        var result = once.EnsureInitialized();

        Assert.False(result.Succeeded);
        Assert.Equal("install an SDK", result.FailureMessage);
    }
}
