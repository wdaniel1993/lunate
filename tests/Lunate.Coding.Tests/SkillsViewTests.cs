namespace Lunate.Coding.Tests;

public sealed class SkillsViewTests
{
    private static SkillDescriptor Entry(
        string name,
        string description = "Does a thing",
        SkillScope scope = SkillScope.Project,
        string? extensionId = null,
        bool commandOnly = false,
        bool valid = true,
        string? invalidReason = null,
        string? shadows = null,
        bool untrusted = false,
        bool notIndexed = false,
        string body = "Body"
    ) =>
        new(
            name,
            description,
            body,
            $"/skills/{name}",
            $"/skills/{name}/SKILL.md",
            scope,
            extensionId,
            0,
            0,
            commandOnly,
            valid,
            invalidReason
        )
        {
            Shadows = shadows,
            Untrusted = untrusted,
            NotIndexed = notIndexed,
        };

    [Fact]
    public void Nothing_discovered_reads_none_found()
    {
        Assert.Equal("skills: none found", SkillsView.Text([]));
    }

    [Fact]
    public void The_states_matrix_renders_each_marker()
    {
        string text = SkillsView.Text([
            Entry("greek", "Use for Greek"),
            Entry("secret", scope: SkillScope.User, commandOnly: true),
            Entry("sneaky", untrusted: true),
            Entry("crowded", notIndexed: true),
            Entry("other", shadows: "user"),
            Entry("given", scope: SkillScope.Extension, extensionId: "hello"),
        ]);

        Assert.Contains("greek (project) — Use for Greek", text, StringComparison.Ordinal);
        Assert.Contains("secret (user, command-only) — ", text, StringComparison.Ordinal);
        Assert.Contains("sneaky (project, untrusted) — ", text, StringComparison.Ordinal);
        Assert.Contains("crowded (project, not indexed) — ", text, StringComparison.Ordinal);
        Assert.Contains("other (project, shadows user) — ", text, StringComparison.Ordinal);
        Assert.Contains("given (extension 'hello') — ", text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_entry_renders_its_first_sentence()
    {
        string text = SkillsView.Text([
            Entry(
                "broken",
                valid: false,
                invalidReason: "frontmatter is missing a 'description'. It cannot load."
            ),
        ]);

        Assert.Contains(
            "broken (invalid) — frontmatter is missing a 'description'",
            text,
            StringComparison.Ordinal
        );
        Assert.DoesNotContain("It cannot load", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_view_never_prints_file_contents()
    {
        string text = SkillsView.Text([Entry("greek", body: "SECRET-BODY-CONTENT")]);

        Assert.DoesNotContain("SECRET-BODY-CONTENT", text, StringComparison.Ordinal);
    }
}
