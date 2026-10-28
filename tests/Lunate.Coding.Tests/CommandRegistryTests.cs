using System.Text.RegularExpressions;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The command registry's guards: the built-in table and its switch stay one set, sources merge in
/// the pinned order, the first source owns a duplicate name, and a handler failure is a contained
/// notice with a log entry rather than a crash.
/// </summary>
public sealed class CommandRegistryTests
{
    [Fact]
    public void The_descriptor_table_matches_the_dispatch_switch() =>
        Assert.Equal(
            [
                .. Commands
                    .Descriptors.Select(descriptor => descriptor.Name)
                    .Order(StringComparer.Ordinal),
            ],
            [.. DispatchedNames().Order(StringComparer.Ordinal)]
        );

    [Fact]
    public void Every_descriptor_carries_a_one_line_description() =>
        Assert.Empty(
            Commands
                .Descriptors.Where(descriptor =>
                    string.IsNullOrWhiteSpace(descriptor.Description)
                    || descriptor.Description.Contains('\n')
                    || descriptor.Description.EndsWith('.')
                )
                .Select(descriptor => descriptor.Name)
        );

    [Fact]
    public void Name_and_session_are_registered_session_commands()
    {
        foreach (string name in new[] { "/name", "/session", "/editor" })
        {
            BuiltinCommandDescriptor descriptor = Assert.Single(
                Commands.Descriptors,
                candidate => candidate.Name == name
            );
            Assert.Equal(CommandGroup.Session, descriptor.Group);
            Assert.Contains(name, DispatchedNames());
        }
    }

    [Fact]
    public void Provider_and_favourite_commands_are_registered_model_commands()
    {
        foreach (string name in new[] { "/auth", "/login", "/logout", "/favorite", "/unfavorite" })
        {
            BuiltinCommandDescriptor descriptor = Assert.Single(
                Commands.Descriptors,
                candidate => candidate.Name == name
            );
            Assert.Equal(CommandGroup.Model, descriptor.Group);
            Assert.Contains(name, DispatchedNames());
        }
    }

    [Fact]
    public async Task The_registry_orders_by_kind_then_source_then_descriptor()
    {
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource(
                    "extension",
                    Descriptor("extension-only", CommandKind.Extension),
                    Descriptor("same", CommandKind.Extension)
                ),
                new FakeSource("template", Descriptor("template-only", CommandKind.Template)),
                new FakeSource(
                    "skills",
                    Descriptor("skill-only", CommandKind.Skill),
                    Descriptor("same", CommandKind.Skill)
                ),
                new FakeSource(
                    "builtin",
                    Descriptor("same", CommandKind.Builtin),
                    Descriptor("builtin-only", CommandKind.Builtin)
                ),
            ],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            ["same", "builtin-only", "skill-only", "extension-only", "template-only"],
            registry.Descriptors.Select(descriptor => descriptor.Name)
        );
        Assert.Equal(
            CommandKind.Builtin,
            registry.Descriptors.Single(descriptor => descriptor.Name == "same").Kind
        );
    }

    [Fact]
    public async Task The_first_source_after_ordering_owns_a_duplicate_name()
    {
        var first = new FakeSource("first", Descriptor("ping", CommandKind.Builtin))
        {
            Result = CommandResult.Notice("first"),
        };
        var second = new FakeSource("second", Descriptor("ping", CommandKind.Extension))
        {
            Result = CommandResult.Notice("second"),
        };
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [second, first],
            TestContext.Current.CancellationToken
        );

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("ping", "hello"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("first", result.Message);
        Assert.Single(registry.Descriptors);
    }

    [Fact]
    public async Task An_unknown_command_is_a_plain_notice()
    {
        CommandRegistry registry = CommandRegistry.Empty;

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("nope", string.Empty),
            TestContext.Current.CancellationToken
        );

        Assert.False(result.IsError);
        Assert.Equal("unknown command: /nope", result.Message);
    }

    [Fact]
    public async Task A_handler_exception_becomes_an_error_result_and_a_log_entry()
    {
        var log = new RecordingExtensionLog();
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource(
                    "boom",
                    new CommandDescriptor(
                        "explode",
                        "A command",
                        null,
                        "boom",
                        CommandKind.Extension
                    )
                )
                {
                    Throw = true,
                },
            ],
            log,
            TestContext.Current.CancellationToken
        );

        CommandResult result = await registry.InvokeAsync(
            new CommandInvocation("explode", string.Empty),
            TestContext.Current.CancellationToken
        );

        Assert.True(result.IsError);
        Assert.Equal(
            "command ext:boom:explode failed: explosion — see /extensions for the extension's state",
            result.Message
        );
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("ext:boom:explode", StringComparison.Ordinal)
                && message.Contains("source 'boom'", StringComparison.Ordinal)
                && message.Contains("explosion", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task A_source_that_fails_to_describe_is_skipped_and_logged()
    {
        var log = new RecordingExtensionLog();
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [
                new FakeSource("broken", Descriptor("gone", CommandKind.Extension))
                {
                    ThrowOnDescribe = true,
                },
                new FakeSource("ok", Descriptor("kept", CommandKind.Extension)),
            ],
            log,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(["kept"], registry.Descriptors.Select(descriptor => descriptor.Name));
        Assert.Contains(
            log.Messages,
            message =>
                message.Contains("source 'broken'", StringComparison.Ordinal)
                && message.Contains("could not describe", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Cancellation_reaches_the_handler_and_propagates()
    {
        var source = new FakeSource("wait", Descriptor("wait", CommandKind.Extension))
        {
            Block = true,
        };
        CommandRegistry registry = await CommandRegistry.ComposeAsync(
            [source],
            TestContext.Current.CancellationToken
        );
        using var cancellation = new CancellationTokenSource();

        Task<CommandResult> invocation = registry
            .InvokeAsync(new CommandInvocation("wait", string.Empty), cancellation.Token)
            .AsTask();
        await source.Started.Task.WaitAsync(
            WaitBudgets.InProcess,
            TestContext.Current.CancellationToken
        );
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation);
    }

    private static CommandDescriptor Descriptor(string name, CommandKind kind) =>
        new(name, "A command", null, kind.ToString().ToLowerInvariant(), kind);

    /// <summary>
    /// The names the built-in switch acts on, read from the source: the case labels of
    /// <c>BuiltinCommandSource.InvokeAsync</c> plus the skill-invocation prefix branch (which is not
    /// a case label). The switch is the built-in source's implementation, not a second dispatcher;
    /// this guard keeps its table and its branches one set. The scan reads plain lowercase
    /// <c>case "/name":</c> labels only, so a future command with another shape needs the pattern
    /// extended — with a descriptor it fails this test, without one it would pass unnoticed.
    /// </summary>
    private static IReadOnlyList<string> DispatchedNames()
    {
        string source = File.ReadAllText(
            Path.Combine(
                TestPaths.RepositoryRoot,
                "src",
                "Lunate.Coding",
                "InteractiveSession.BuiltinCommands.cs"
            )
        );
        int start = source.IndexOf("BuiltinCommandSource", StringComparison.Ordinal);
        Assert.True(
            start >= 0,
            "the built-in source was not found in InteractiveSession.BuiltinCommands.cs"
        );
        string dispatch = source[start..];

        List<string> names =
        [
            .. Regex
                .Matches(dispatch, """case "(?<name>/[a-z]+)":""")
                .Select(match => match.Groups["name"].Value),
        ];
        if (dispatch.Contains("Commands.SkillPrefix", StringComparison.Ordinal))
        {
            names.Add(Commands.SkillName);
        }

        return names;
    }

    private sealed class FakeSource(string id, params CommandDescriptor[] descriptors)
        : ICommandSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CommandResult Result { get; init; } = CommandResult.None;

        public bool Throw { get; init; }

        public bool ThrowOnDescribe { get; init; }

        public bool Block { get; init; }

        public string SourceId => id;

        public ValueTask<IReadOnlyList<CommandDescriptor>> DescribeAsync(
            CancellationToken cancellationToken
        ) =>
            ThrowOnDescribe
                ? throw new InvalidOperationException("describe exploded")
                : ValueTask.FromResult<IReadOnlyList<CommandDescriptor>>(descriptors);

        public async ValueTask<CommandResult> InvokeAsync(
            CommandInvocation invocation,
            CancellationToken cancellationToken
        )
        {
            Started.TrySetResult();
            if (Throw)
            {
                throw new InvalidOperationException("explosion");
            }

            if (Block)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return Result;
        }
    }
}
