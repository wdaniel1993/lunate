using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding.Tests;

/// <summary>
/// The <c>/help</c> listing over the whole registry: the built-in groups print first and
/// unchanged, then one section per non-built-in kind present (<c>skills</c>, <c>extensions</c>,
/// <c>templates</c>), each sorted by name and omitted when empty; a registry with nothing but
/// built-ins prints exactly the pre-change listing, and the <c>/skill:&lt;name&gt;</c> placeholder
/// stays in its built-in group.
/// </summary>
public sealed class HelpViewTests
{
    /// <summary>The built-in listing, pinned byte for byte (design D1: an empty source adds nothing).</summary>
    private const string BuiltinListing = """
        session
          /clone    — copy the whole session into a new session file
          /compact  — compact the conversation now, optionally focusing on <text>
          /context  — show the context window and subagent budgets
          /fork     — start a new session file at a picked entry
          /help     — list the commands
          /name     — set or show the session title
          /new      — start a fresh session
          /resume   — open the session picker
          /rewind   — move the active leaf to a picked entry
          /session  — show the session file, messages and tokens
          /thinking — set the reasoning level (off|low|medium|high)
          /tree     — browse the session tree and pick a message
        model
          /favorite   — add the active or named model to favorites
          /login      — store an API key for a provider preset
          /logout     — remove the stored API key for a provider preset
          /model      — pick a model or switch to one by id
          /unfavorite — remove the active or named model from favorites
        extensions
          /extensions   — list the discovered extensions with their state
          /mcp          — list the MCP servers with their status and source
          /skill:<name> — invoke a discovered skill by name
          /skills       — list the discovered skills with their scope and state
          /trust        — show or clear this repository's trust records
        quit
          /quit — quit
        """;

    [Fact]
    public void Built_in_only_registry_prints_the_pre_change_listing_exactly()
    {
        IReadOnlyList<string> lines = HelpView.Lines(Commands.PublicDescriptors);

        Assert.Equal(
            BuiltinListing.ReplaceLineEndings("\n"),
            string.Join("\n", lines).ReplaceLineEndings("\n")
        );
    }

    [Fact]
    public void A_skill_and_an_extension_add_their_sections_after_the_built_in_groups()
    {
        IReadOnlyList<string> lines = HelpView.Lines([
            .. Commands.PublicDescriptors,
            new CommandDescriptor(
                "skill:review",
                "review the current diff",
                ArgumentHint: "[arguments]",
                SourceId: SkillCommandSource.SourceIdValue,
                Kind: CommandKind.Skill
            ),
            new CommandDescriptor(
                "ping",
                "ping the server",
                ArgumentHint: null,
                SourceId: "acme",
                Kind: CommandKind.Extension
            ),
        ]);

        string listing = string.Join("\n", lines).ReplaceLineEndings("\n");
        Assert.StartsWith(
            BuiltinListing.ReplaceLineEndings("\n"),
            listing,
            StringComparison.Ordinal
        );
        Assert.EndsWith(
            """
            skills
              /skill:review — review the current diff
            extensions
              /ping — ping the server
            """.ReplaceLineEndings("\n"),
            listing,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Sections_sort_by_name_and_pad_within_their_own_section()
    {
        IReadOnlyList<string> lines = HelpView.Lines([
            .. Commands.PublicDescriptors,
            Descriptor("zeta", CommandKind.Extension, "the last one"),
            Descriptor("alpha", CommandKind.Extension, "the first one"),
            Descriptor("standup", CommandKind.Template, "draft the standup note"),
        ]);

        Assert.Equal(
            [
                "extensions",
                "  /alpha — the first one",
                "  /zeta  — the last one",
                "templates",
                "  /standup — draft the standup note",
            ],
            lines.Skip(lines.Count - 5)
        );
    }

    [Fact]
    public void The_skill_placeholder_stays_in_the_built_in_group_and_is_not_a_section_row()
    {
        IReadOnlyList<string> lines = HelpView.Lines([
            .. Commands.PublicDescriptors,
            new CommandDescriptor(
                "skill:review",
                "review the current diff",
                ArgumentHint: "[arguments]",
                SourceId: SkillCommandSource.SourceIdValue,
                Kind: CommandKind.Skill
            ),
        ]);

        Assert.Single(lines, line => line.Contains("/skill:<name>", StringComparison.Ordinal));
        int skillsHeading = lines.ToList().IndexOf("skills");
        Assert.True(skillsHeading > 0);
        Assert.Equal(["  /skill:review — review the current diff"], lines.Skip(skillsHeading + 1));
    }

    [Fact]
    public void The_skills_section_stops_at_twenty_rows_and_points_at_the_skills_index()
    {
        List<CommandDescriptor> descriptors = [.. Commands.PublicDescriptors];
        for (var index = 0; index < 23; index++)
        {
            descriptors.Add(
                Descriptor(
                    string.Create(CultureInfo.InvariantCulture, $"skill:skill-{index:00}"),
                    CommandKind.Skill,
                    "a skill"
                )
            );
        }

        IReadOnlyList<string> lines = HelpView.Lines(descriptors);
        List<string> rows = [.. lines.Skip(lines.ToList().IndexOf("skills") + 1)];

        Assert.Equal(21, rows.Count);
        Assert.StartsWith("  /skill:skill-00 ", rows[0], StringComparison.Ordinal);
        Assert.StartsWith("  /skill:skill-19 ", rows[19], StringComparison.Ordinal);
        Assert.Equal("  +3 more — see /skills", rows[20]);
    }

    [Fact]
    public void Extensions_and_templates_are_not_capped()
    {
        List<CommandDescriptor> descriptors = [.. Commands.PublicDescriptors];
        for (var index = 0; index < 25; index++)
        {
            descriptors.Add(
                Descriptor(
                    string.Create(CultureInfo.InvariantCulture, $"command-{index:00}"),
                    CommandKind.Extension,
                    "an extension command"
                )
            );
        }

        IReadOnlyList<string> lines = HelpView.Lines(descriptors);

        Assert.DoesNotContain(lines, line => line.Contains("more — see", StringComparison.Ordinal));
        Assert.Equal(
            25,
            lines.Count(line => line.StartsWith("  /command-", StringComparison.Ordinal))
        );
    }

    private static CommandDescriptor Descriptor(
        string name,
        CommandKind kind,
        string description
    ) => new(name, description, null, kind.ToString().ToLowerInvariant(), kind);
}
