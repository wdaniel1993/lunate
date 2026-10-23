using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The host-side identity of a command (design D2): every entry carries one source-qualified id —
/// <c>builtin:&lt;name&gt;</c>, <c>skill:&lt;name&gt;</c>, <c>template:&lt;name&gt;</c> or
/// <c>ext:&lt;extension id&gt;:&lt;name&gt;</c> — computed from the descriptor's kind, source and
/// name. Two extensions offering one name stay distinguishable in the log and in the failure
/// notice while the bare name still dispatches to the first entry after ordering, and the id never
/// reaches the typed token or an autocomplete row.
/// </summary>
public sealed class CommandIdentityTests
{
    [Fact]
    public void An_id_is_computed_from_the_kind_source_and_name()
    {
        Assert.Equal(
            "builtin:help",
            CommandId.For(Descriptor("help", CommandKind.Builtin, "builtin"))
        );
        Assert.Equal(
            "skill:review",
            CommandId.For(Descriptor("skill:review", CommandKind.Skill, "skills"))
        );
        Assert.Equal(
            "template:standup",
            CommandId.For(Descriptor("standup", CommandKind.Template, "templates"))
        );
        Assert.Equal(
            "ext:alpha:ping",
            CommandId.For(Descriptor("ping", CommandKind.Extension, "alpha"))
        );
    }

    [Fact]
    public void A_skill_id_is_its_typed_name_so_the_skill_path_needs_no_change()
    {
        Assert.Equal("skill:review", CommandId.ForSource("skills", "review", CommandKind.Skill));
        Assert.Equal(
            "skill:review",
            CommandId.ForSource("skills", "skill:review", CommandKind.Skill)
        );
    }

    [Fact]
    public async Task Two_extensions_offering_one_name_compose_to_distinct_ids_and_one_entry()
    {
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource("alpha", Descriptor("ping", CommandKind.Extension, "alpha")),
                new FakeSource("beta", Descriptor("ping", CommandKind.Extension, "beta")),
            ],
            new RecordingExtensionLog(),
            TestContext.Current.CancellationToken
        );

        CommandEntry entry = Assert.Single(registry.Entries);
        Assert.Equal("ext:alpha:ping", entry.Id);
        Assert.Equal("ping", entry.Descriptor.Name);
        CommandShadow shadowed = Assert.Single(registry.Shadowed);
        Assert.Equal("ext:beta:ping", shadowed.ShadowedId);
        Assert.Equal("ext:alpha:ping", shadowed.OwnerId);
        Assert.NotEqual(shadowed.ShadowedId, shadowed.OwnerId);
    }

    [Fact]
    public async Task The_duplicate_diagnostic_names_both_sources_and_both_ids()
    {
        var log = new RecordingExtensionLog();
        await CommandRegistry.ComposeAsync(
            [
                new FakeSource("alpha", Descriptor("ping", CommandKind.Extension, "alpha")),
                new FakeSource("beta", Descriptor("ping", CommandKind.Extension, "beta")),
            ],
            log,
            TestContext.Current.CancellationToken
        );

        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("ext:alpha:ping", StringComparison.Ordinal)
                && message.Contains("ext:beta:ping", StringComparison.Ordinal)
                && message.Contains("source 'alpha'", StringComparison.Ordinal)
                && message.Contains("source 'beta'", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_failing_extension_handler_names_the_id_the_source_and_the_recovery_step()
    {
        var log = new RecordingExtensionLog();
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource("acme", Descriptor("ping", CommandKind.Extension, "acme"))
                {
                    Throw = true,
                },
            ],
            log,
            TestContext.Current.CancellationToken
        );

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("ping", string.Empty),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsError);
        Assert.Equal(
            "command ext:acme:ping failed: connection refused — see /extensions for the extension's state",
            result.Message
        );
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("ext:acme:ping", StringComparison.Ordinal)
                && message.Contains("source 'acme'", StringComparison.Ordinal)
                && message.Contains("connection refused", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_failing_skill_handler_points_at_the_skills_index()
    {
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource("skills", Descriptor("skill:review", CommandKind.Skill, "skills"))
                {
                    Throw = true,
                },
            ],
            TestContext.Current.CancellationToken
        );

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("skill:review", string.Empty),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsError);
        Assert.Equal(
            "command skill:review failed: connection refused — see /skills for the skill's state",
            result.Message
        );
    }

    [Fact]
    public async Task A_failing_built_in_keeps_the_short_form_and_no_stack_trace()
    {
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource("builtin", Descriptor("help", CommandKind.Builtin, "builtin"))
                {
                    Throw = true,
                },
            ],
            TestContext.Current.CancellationToken
        );

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("help", string.Empty),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsError);
        Assert.Equal("command /help failed: connection refused", result.Message);
        Assert.DoesNotContain("   at ", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "InvalidOperationException",
            result.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task Ids_never_reach_an_autocomplete_row()
    {
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource(
                    "alpha",
                    Descriptor("ping", CommandKind.Extension, "alpha"),
                    Descriptor("help", CommandKind.Builtin, "builtin")
                ),
            ],
            TestContext.Current.CancellationToken
        );

        Assert.All(
            registry.CompletionItems,
            item => Assert.DoesNotContain("ext:", item.Text, StringComparison.Ordinal)
        );
        Assert.All(
            registry.CompletionItems,
            item => Assert.DoesNotContain("builtin:", item.Text, StringComparison.Ordinal)
        );
    }

    private static CommandDescriptor Descriptor(string name, CommandKind kind, string sourceId) =>
        new(name, "A command", null, sourceId, kind);

    private sealed class FakeSource(string id, params CommandDescriptor[] descriptors)
        : ICommandSource
    {
        public bool Throw { get; init; }

        public string SourceId => id;

        public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
            CancellationToken cancellationToken
        ) => ValueTask.FromResult<IReadOnlyList<CommandDescriptor>>(descriptors);

        public ValueTask<CommandResult> InvokeAsync(
            CommandInvocation invocation,
            CancellationToken cancellationToken
        ) =>
            Throw
                ? throw new InvalidOperationException("connection refused")
                : ValueTask.FromResult(CommandResult.Notice("ok"));
    }
}
