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
            {
                "bold-italic",
                """
                    **bold text**

                    *italic text*

                    ***bold and italic***
                    """
            },
            {
                "inline-code",
                """
                    Inline `code span` and `[brackets]` too.
                    """
            },
            {
                "escaping",
                """
                    [dim]not markup[/]

                    [bold]also not markup[/]

                    nested [[brackets]] stay
                    """
            },
            {
                "emphasis-edges",
                """
                    snake_case_words stays literal

                    2*3*4 follows CommonMark

                    2 * 3 * 4 keeps its asterisks
                    """
            },
            {
                "unsupported",
                """
                    | a | b |
                    | - | - |
                    | 1 | 2 |

                    A [link](https://example.com/x) here.

                    ![alt text](https://example.com/i.png)

                    <div class="note">
                    raw html
                    </div>

                    - [ ] todo
                    - [x] done
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
