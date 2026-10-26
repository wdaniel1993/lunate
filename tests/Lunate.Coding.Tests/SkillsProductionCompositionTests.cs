using Lunate.Agent;
using Lunate.Extensibility;
using Lunate.TestSupport;
using Lunate.Tui;

namespace Lunate.Coding.Tests;

/// <summary>
/// The production-composition guard ("spec'd but not wired"): through the real host, the skills
/// index appears in the actual composed system prompt and /skill:&lt;name&gt; content reaches the
/// model — proven against this repository's own <c>.agents/skills</c>.
/// </summary>
public sealed class SkillsProductionCompositionTests
{
    [Fact]
    public async Task The_composed_prompt_carries_the_repositorys_nine_skills_and_an_invocation_reaches_the_model()
    {
        string repository = Workspace.Canonicalize(RepositoryRoot.Find());
        string[] names =
        [
            "acceptance-test-authoring",
            "architectural-decision-records",
            "c4-diagrams",
            "gherkin-authoring",
            "glossary",
            "grill-me",
            "openspec-git-discipline",
            "spec-as-source",
            "test-driven-development",
        ];
        using var host = new InteractiveSessionHost(
            extensions: new ExtensionSeams
            {
                StorePath = Path.Combine(
                    Path.GetTempPath(),
                    "lunate-skills-" + Guid.NewGuid().ToString("N")
                ),
                Log = new RecordingExtensionLog(),
            },
            workingDirectory: repository
        );
        host.Console.Size = new ConsoleSize(600, 24);
        Task run = host.RunAsync();
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "Load project skills from this repository"
        );
        Assert.Contains(string.Join(", ", names), host.Frames, StringComparison.Ordinal);
        host.Console.SendText("y");
        await InteractiveSessionCommandTests.WaitForFrameAsync(
            host,
            "remembered for this repository"
        );

        // The real composed system prompt carries the index, all nine skills included.
        host.Client.Enqueue(Scripts.Text("hello"), Scripts.Stop());
        host.Console.SendText("hi");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(1);
        string system = host
            .Client.Requests[0]
            .Single(message => message.Role == Microsoft.Extensions.AI.ChatRole.System)
            .Text;
        Assert.Contains("read tool", system, StringComparison.Ordinal);
        foreach (string name in names)
        {
            Assert.Contains($"- {name}: ", system, StringComparison.Ordinal);
        }

        // And /skill:<name> content reaches the model as the wrapped user turn.
        host.Client.Enqueue(Scripts.Text("done"), Scripts.Stop());
        host.Console.SendText("/skill:gherkin-authoring");
        host.Console.SendEnter();
        await host.Client.WaitForCallAsync(2);
        await InteractiveSessionCommandTests.WaitForScrollbackAsync(host, "done");
        string skillTurn = host
            .Client.Requests[1]
            .Last(message => message.Role == Microsoft.Extensions.AI.ChatRole.User)
            .Text;
        Assert.StartsWith(
            "<skill name=\"gherkin-authoring\" dir=\"",
            skillTurn,
            StringComparison.Ordinal
        );
        Assert.Contains("# Gherkin Authoring", skillTurn, StringComparison.Ordinal);

        host.Console.SendText("/quit");
        host.Console.SendEnter();
        await run;
    }

    [Fact]
    public void The_base_prompt_stays_under_the_core_cap_with_the_index_counted_separately()
    {
        using var temp = new TempDirectory();
        var workspace = new Workspace(temp.Root);
        string basePrompt = SystemPrompt.Compose(
            workspace,
            ["read", "write", "edit", "bash"],
            new ShellResolver("/bin/bash")
        );

        // The guide's rule keeps applying to the base composed prompt (index excluded)...
        Assert.True(SkillIndex.EstimateTokens(basePrompt) < 1000);
        // ...while the index has its own cap and is asserted on its own.
        List<SkillDescriptor> skills = [];
        for (var index = 0; index < 60; index++)
        {
            string directory = Path.Combine(temp.Root, "skills", $"skill-{index:D2}");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "SKILL.md");
            File.WriteAllText(
                file,
                $"---\nname: skill-{index:D2}\ndescription: {new string('d', 200)}\n---\nBody\n"
            );
            skills.Add(
                new SkillDescriptor(
                    $"skill-{index:D2}",
                    new string('d', 200),
                    "Body",
                    directory,
                    file,
                    SkillScope.Project,
                    null,
                    0,
                    0,
                    CommandOnly: false,
                    Valid: true,
                    InvalidReason: null
                )
            );
        }

        SkillIndex.Composed composed = SkillIndex.Compose(skills, temp.Root, 2000);
        Assert.True(composed.EstimatedTokens <= SkillIndex.MaxEstimatedTokens);
        Assert.True(composed.EstimatedTokens > 0);
    }
}
