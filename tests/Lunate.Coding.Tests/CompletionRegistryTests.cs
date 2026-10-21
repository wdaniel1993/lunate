using Lunate.Extensibility;
using Lunate.Extensibility.Abstractions;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The one autocomplete registry: composed from the command table, the discovered skills and the
/// loaded extensions' manifests, in that natural order, with no second hand-maintained list.
/// </summary>
public sealed class CompletionRegistryTests
{
    private static SkillDescriptor Skill(
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

    private static ExtensionDescriptor Extension(string id, params string[] commands) =>
        new(
            id,
            "0.1.0",
            ExtensionScope.Global,
            "/extensions/" + id,
            new ExtensionManifest
            {
                Id = id,
                Version = "0.1.0",
                ApiVersion = "^1.0.0",
                Commands = commands,
            }
        );

    [Fact]
    public void The_registry_lists_the_built_in_commands_first_in_table_order()
    {
        CompletionRegistry registry = CompletionRegistry.Compose([], []);

        Assert.Equal(
            Commands
                .Descriptors.Where(descriptor => descriptor.Name != Commands.SkillName)
                .Select(descriptor => descriptor.Name),
            registry.Items.Select(item => item.Text)
        );
        Assert.All(registry.Items, item => Assert.Equal(CompletionKind.Command, item.Kind));
    }

    [Fact]
    public void The_skill_invocation_placeholder_is_not_offered()
    {
        CompletionRegistry registry = CompletionRegistry.Compose([], []);

        Assert.DoesNotContain(registry.Items, item => item.Text == Commands.SkillName);
    }

    [Fact]
    public void Every_built_in_item_carries_the_command_tables_description()
    {
        CompletionRegistry registry = CompletionRegistry.Compose([], []);

        foreach (
            CommandDescriptor descriptor in Commands.Descriptors.Where(descriptor =>
                descriptor.Name != Commands.SkillName
            )
        )
        {
            CompletionItem item = registry.Items.Single(entry => entry.Text == descriptor.Name);
            Assert.Equal(descriptor.Description, item.Description);
        }
    }

    [Fact]
    public void The_provider_and_favourite_rows_carry_the_table_descriptions()
    {
        CompletionRegistry registry = CompletionRegistry.Compose([], []);

        foreach (string name in new[] { "/login", "/logout", "/favorite", "/unfavorite" })
        {
            CommandDescriptor descriptor = Commands.Descriptors.Single(entry => entry.Name == name);
            CompletionItem item = registry.Items.Single(entry => entry.Text == name);
            Assert.Equal(descriptor.Description, item.Description);
        }
    }

    [Fact]
    public void Skills_follow_the_commands_as_skill_prefix_entries()
    {
        CompletionRegistry registry = CompletionRegistry.Compose(
            [Skill("greek", "Use for Greek"), Skill("tail", "Read the tail")],
            []
        );

        CompletionItem[] skills =
        [
            .. registry.Items.Where(item => item.Kind == CompletionKind.Skill),
        ];
        Assert.Equal(["/skill:greek", "/skill:tail"], skills.Select(item => item.Text));
        Assert.Equal(["Use for Greek", "Read the tail"], skills.Select(item => item.Description));
    }

    [Fact]
    public void Extension_commands_follow_the_skills_described_by_the_extension_id()
    {
        CompletionRegistry registry = CompletionRegistry.Compose(
            [Skill("greek")],
            [Extension("hello", "/hello", "/hello-again")]
        );

        CompletionItem[] extensions =
        [
            .. registry.Items.Where(item => item.Kind == CompletionKind.Extension),
        ];
        Assert.Equal(["/hello", "/hello-again"], extensions.Select(item => item.Text));
        Assert.All(extensions, item => Assert.Equal("hello", item.Description));
    }

    [Fact]
    public void Invalid_skills_and_untrusted_skills_are_not_offered()
    {
        CompletionRegistry registry = CompletionRegistry.Compose(
            [Skill("ok"), Skill("broken", valid: false), Skill("sneaky", untrusted: true)],
            []
        );

        Assert.Contains(registry.Items, item => item.Text == "/skill:ok");
        Assert.DoesNotContain(registry.Items, item => item.Text == "/skill:broken");
        Assert.DoesNotContain(registry.Items, item => item.Text == "/skill:sneaky");
    }

    [Fact]
    public void A_text_appears_once_first_source_wins()
    {
        CompletionRegistry registry = CompletionRegistry.Compose(
            [Skill("help", "skill help")],
            [Extension("ext", "/help", "/model", "/hello")]
        );

        Assert.Single(registry.Items, item => item.Text == "/help");
        Assert.Single(registry.Items, item => item.Text == "/model");
        Assert.Equal(CompletionKind.Command, registry.Items.Single(i => i.Text == "/help").Kind);
        Assert.Equal("ext", registry.Items.Single(i => i.Text == "/hello").Description);
    }

    [Fact]
    public void A_skill_without_a_description_keeps_a_null_one()
    {
        CompletionRegistry registry = CompletionRegistry.Compose([Skill("bare", "")], []);

        Assert.Null(registry.Items.Single(item => item.Text == "/skill:bare").Description);
    }
}
