using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>The command contract's value semantics and result factories (the public seam).</summary>
public sealed class CommandContractTests
{
    [Fact]
    public void Descriptors_and_invocations_compare_by_value()
    {
        var descriptor = new CommandDescriptor(
            "ping",
            "Ping",
            "<text>",
            "cmd",
            CommandKind.Extension
        );
        var same = new CommandDescriptor("ping", "Ping", "<text>", "cmd", CommandKind.Extension);
        var invocation = new CommandInvocation("ping", "hello");
        var sameInvocation = new CommandInvocation("ping", "hello");

        Assert.Equal(descriptor, same);
        Assert.Equal(invocation, sameInvocation);
        Assert.NotEqual(descriptor, descriptor with { Kind = CommandKind.Builtin });
        Assert.NotEqual(invocation, invocation with { Argument = "bye" });
    }

    [Fact]
    public void Results_carry_their_message_and_error_flag()
    {
        Assert.Null(CommandResult.None.Message);
        Assert.False(CommandResult.None.IsError);

        CommandResult notice = CommandResult.Notice("done");
        Assert.Equal("done", notice.Message);
        Assert.False(notice.IsError);

        CommandResult error = CommandResult.Error("failed");
        Assert.Equal("failed", error.Message);
        Assert.True(error.IsError);
    }
}
