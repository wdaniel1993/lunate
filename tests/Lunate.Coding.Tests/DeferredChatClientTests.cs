using Lunate.Contracts;

namespace Lunate.Coding.Tests;

public sealed class DeferredChatClientTests
{
    [Fact]
    public void Native_decision_lookup_creates_the_deferred_client_on_first_decision_use()
    {
        int creations = 0;
        var inner = new ScriptedChatClient();
        using var deferred = new DeferredChatClient(() =>
        {
            creations++;
            return inner;
        });

        Assert.Null(deferred.GetService(typeof(object)));
        Assert.Equal(0, creations);
        Assert.Null(deferred.GetService(typeof(INativeDecisionClient)));
        Assert.Equal(1, creations);
    }
}
