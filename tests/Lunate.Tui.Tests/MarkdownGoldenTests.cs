using Spectre.Console.Testing;

namespace Lunate.Tui.Tests;

public sealed class MarkdownGoldenTests
{
    private static string GoldensDirectory =>
        Path.Combine(TestPaths.RepositoryRoot, "tests", "Lunate.Tui.Tests", "fixtures", "markdown");

    public static TheoryData<string, string> Documents =>
        new()
        {
            {
                "heading",
                """
                    # Level one

                    ## Level two

                    ### Level three

                    #### Level four

                    ##### Level five

                    ###### Level six
                    """
            },
            {
                "lists",
                """
                    - bullet one
                    - bullet two

                    1. first
                    2. second
                    3. third
                    """
            },
            {
                "nested-lists",
                """
                    - top
                      - child
                        - grandchild
                    - second
                    """
            },
            {
                "quote",
                """
                    > quoted line
                    > still quoted
                    """
            },
            {
                "unclosed-fence",
                """
                    ```shell
                    echo one
                    echo two

                    this looks like a paragraph but is code
                    """
            },
        };

    [Theory]
    [MemberData(nameof(Documents))]
    public void Fixture_matches_the_committed_golden(string name, string markdown)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(new MarkdownRenderer().Render(markdown));

        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, name + ".txt"),
            console.Output
        );
    }
}
