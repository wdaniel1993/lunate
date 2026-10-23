using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The template command slot (design D5, requirement <c>Template command source slot</c>): a
/// <see cref="CommandKind.Template"/> source composes by the same rules as every other source —
/// last, under <c>templates</c> in <c>/help</c>, with <c>template:&lt;name&gt;</c> ids — and can
/// never shadow a built-in. This change ships no template discovery, no file format and no
/// <c>/help</c> template content; the slot is exercised by the fixed test sources below.
/// </summary>
public sealed class TemplateCommandSlotTests
{
    [Fact]
    public async Task A_template_source_composes_last_and_appears_under_templates()
    {
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource(
                    "templates",
                    Descriptor("standup", CommandKind.Template, "templates")
                ),
                new FakeSource("acme", Descriptor("ping", CommandKind.Extension, "acme")),
                new FakeSource("skills", Descriptor("skill:review", CommandKind.Skill, "skills")),
                new FakeSource("builtin", Descriptor("help", CommandKind.Builtin, "builtin")),
            ],
            new RecordingExtensionLog(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            ["help", "skill:review", "ping", "standup"],
            registry.Descriptors.Select(descriptor => descriptor.Name)
        );
        Assert.Equal("template:standup", registry.Entries[^1].Id);
        Assert.Equal(CommandKind.Template, registry.Entries[^1].Descriptor.Kind);

        IReadOnlyList<string> lines = HelpView.Lines(registry.Descriptors);
        Assert.EndsWith(
            """
            templates
              /standup — A template
            """.ReplaceLineEndings("\n"),
            string.Join("\n", lines).ReplaceLineEndings("\n"),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task A_template_cannot_shadow_a_built_in_and_the_diagnostic_names_both_ids()
    {
        var log = new RecordingExtensionLog();
        var template = new FakeSource(
            "templates",
            Descriptor("help", CommandKind.Template, "templates")
        )
        {
            Result = CommandResult.Notice("template help"),
        };
        var builtin = new FakeSource("builtin", Descriptor("help", CommandKind.Builtin, "builtin"))
        {
            Result = CommandResult.Notice("builtin help"),
        };
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [template, builtin],
            log,
            TestContext.Current.CancellationToken
        );

        CommandEntry entry = Assert.Single(registry.Entries);
        Assert.Equal("builtin:help", entry.Id);
        CommandShadow shadowed = Assert.Single(registry.Shadowed);
        Assert.Equal("template:help", shadowed.ShadowedId);
        Assert.Equal("builtin:help", shadowed.OwnerId);
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("builtin:help", StringComparison.Ordinal)
                && message.Contains("template:help", StringComparison.Ordinal)
        );

        // The built-in still dispatches, and the template is not listed.
        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("help", string.Empty),
            TestContext.Current.CancellationToken
        );
        Assert.Equal("builtin help", result.Message);
        Assert.DoesNotContain(
            HelpView.Lines(registry.Descriptors),
            line => line.Contains("templates")
        );
    }

    [Fact]
    public async Task A_template_failure_is_contained_and_names_its_id()
    {
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource(
                    "templates",
                    Descriptor("standup", CommandKind.Template, "templates")
                )
                {
                    Throw = true,
                },
            ],
            new RecordingExtensionLog(),
            TestContext.Current.CancellationToken
        );

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("standup", string.Empty),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsError);
        Assert.Equal("command template:standup failed: the template broke", result.Message);
    }

    private static CommandDescriptor Descriptor(string name, CommandKind kind, string sourceId) =>
        new(name, "A template", null, sourceId, kind);

    private sealed class FakeSource(string id, params CommandDescriptor[] descriptors)
        : ICommandSource
    {
        public CommandResult Result { get; init; } = CommandResult.None;

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
                ? throw new InvalidOperationException("the template broke")
                : ValueTask.FromResult(Result);
    }
}
