using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The one autocomplete registry: composed from the command registry's merged descriptors —
/// built-ins, skills, extension commands — in that order, with no second hand-maintained list.
/// </summary>
public sealed class CompletionRegistryTests
{
    private static CommandDescriptor Skill(string name, string description = "Does a thing") =>
        new(
            "skill:" + name,
            description,
            "[arguments]",
            SkillCommandSource.SourceIdValue,
            CommandKind.Skill
        );

    private static CommandDescriptor Extension(string id, string name, string description = "") =>
        new(name, description, null, id, CommandKind.Extension);

    [Fact]
    public void The_registry_lists_the_built_in_commands_first_in_table_order()
    {
        IReadOnlyList<CompletionItem> items = CompletionRegistry.Compose(
            Commands.PublicDescriptors
        );

        Assert.Equal(
            Commands
                .Descriptors.Where(descriptor => descriptor.Name != Commands.SkillName)
                .Select(descriptor => descriptor.Name),
            items.Select(item => item.Text)
        );
        Assert.All(items, item => Assert.Equal(CompletionKind.Command, item.Kind));
    }

    [Fact]
    public void The_skill_invocation_placeholder_is_not_offered()
    {
        IReadOnlyList<CompletionItem> items = CompletionRegistry.Compose(
            Commands.PublicDescriptors
        );

        Assert.DoesNotContain(items, item => item.Text == Commands.SkillName);
    }

    [Fact]
    public void Every_built_in_item_carries_the_command_tables_description()
    {
        IReadOnlyList<CompletionItem> items = CompletionRegistry.Compose(
            Commands.PublicDescriptors
        );

        foreach (
            BuiltinCommandDescriptor descriptor in Commands.Descriptors.Where(descriptor =>
                descriptor.Name != Commands.SkillName
            )
        )
        {
            CompletionItem item = items.Single(entry => entry.Text == descriptor.Name);
            Assert.Equal(descriptor.Description, item.Description);
        }
    }

    [Fact]
    public void The_provider_and_favourite_rows_carry_the_table_descriptions()
    {
        IReadOnlyList<CompletionItem> items = CompletionRegistry.Compose(
            Commands.PublicDescriptors
        );

        foreach (string name in new[] { "/login", "/logout", "/favorite", "/unfavorite" })
        {
            BuiltinCommandDescriptor descriptor = Commands.Descriptors.Single(entry =>
                entry.Name == name
            );
            CompletionItem item = items.Single(entry => entry.Text == name);
            Assert.Equal(descriptor.Description, item.Description);
        }
    }

    [Fact]
    public void Custom_entries_come_before_the_built_ins()
    {
        IReadOnlyList<CompletionItem> items = CompletionRegistry.Compose([
            .. Commands.PublicDescriptors,
            Skill("greek", "Use for Greek"),
            Skill("tail", "Read the tail"),
        ]);

        CompletionItem[] skills = [.. items.Where(item => item.Kind == CompletionKind.Skill)];
        Assert.Equal(["/skill:greek", "/skill:tail"], skills.Select(item => item.Text));
        Assert.Equal(["Use for Greek", "Read the tail"], skills.Select(item => item.Description));
        // A bare "/" shows the custom rows before the eight-row cap can cut them.
        Assert.Equal(CompletionKind.Skill, items[0].Kind);
        Assert.Equal(CompletionKind.Skill, items[1].Kind);
        Assert.Equal(CompletionKind.Command, items[2].Kind);
    }

    [Fact]
    public void Extension_commands_follow_the_skills_described_by_the_extension_id()
    {
        IReadOnlyList<CompletionItem> items = CompletionRegistry.Compose([
            .. Commands.PublicDescriptors,
            Skill("greek"),
            Extension("hello", "hello", "Say hello"),
            Extension("hello", "hello-again"),
        ]);

        CompletionItem[] extensions =
        [
            .. items.Where(item => item.Kind == CompletionKind.Extension),
        ];
        Assert.Equal(["/hello", "/hello-again"], extensions.Select(item => item.Text));
        Assert.Equal(["Say hello", "hello"], extensions.Select(item => item.Description));
    }

    [Fact]
    public async Task Invalid_skills_and_untrusted_skills_are_not_offered()
    {
        var source = new SkillCommandSource(
            [
                SkillEntry("ok"),
                SkillEntry("broken", valid: false),
                SkillEntry("sneaky", untrusted: true),
            ],
            (_, _, _) => ValueTask.CompletedTask
        );
        IReadOnlyList<CommandDescriptor> descriptors = await source.DescribeAsync(
            TestContext.Current.CancellationToken
        );

        Assert.Contains(descriptors, descriptor => descriptor.Name == "skill:ok");
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Name == "skill:broken");
        Assert.DoesNotContain(descriptors, descriptor => descriptor.Name == "skill:sneaky");
    }

    [Fact]
    public void A_text_appears_once_first_source_wins()
    {
        IReadOnlyList<CompletionItem> items = CompletionRegistry.Compose([
            .. Commands.PublicDescriptors,
            Skill("help", "skill help"),
            Extension("ext", "help", "extension help"),
            Extension("ext", "hello"),
        ]);

        Assert.Single(items, item => item.Text == "/help");
        Assert.Single(items, item => item.Text == "/model");
        Assert.Equal(CompletionKind.Command, items.Single(i => i.Text == "/help").Kind);
        Assert.Equal("ext", items.Single(i => i.Text == "/hello").Description);
    }

    [Fact]
    public void A_skill_without_a_description_keeps_a_null_one()
    {
        IReadOnlyList<CompletionItem> items = CompletionRegistry.Compose([
            .. Commands.PublicDescriptors,
            Skill("bare", ""),
        ]);

        Assert.Null(items.Single(item => item.Text == "/skill:bare").Description);
    }

    private static SkillDescriptor SkillEntry(
        string name,
        string description = "Does a thing",
        bool valid = true,
        bool untrusted = false
    ) =>
        new(
            name,
            description,
            "Body",
            $"/skills/{name}",
            $"/skills/{name}/SKILL.md",
            SkillScope.User,
            null,
            0,
            0,
            false,
            valid,
            null
        )
        {
            Untrusted = untrusted,
        };
}
