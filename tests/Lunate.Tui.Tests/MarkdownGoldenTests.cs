using Spectre.Console;
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
            {
                "fence-csharp",
                """
                    ```csharp
                    var total = items.Sum(x => x.Count); // sum
                    /* a block
                       comment */
                    Console.WriteLine($"total: {total}");
                    ```
                    """
            },
            {
                "fence-json",
                """
                    ```json
                    {
                      "name": "lunate",
                      "count": 12.5,
                      "ok": true
                    }
                    ```
                    """
            },
            {
                "fence-shell",
                """
                    ```sh
                    if [ -f "$HOME/x" ]; then
                      echo done # comment
                    fi
                    echo $HOME
                    ```
                    """
            },
            {
                "mixed",
                """
                    # Lunate

                    Output with **bold**, *italic*, `inline code`, and a [link](https://example.com).

                    An image ![alt text](https://example.com/i.png) and 2*3*4 next to snake_case_words.

                    - first
                      - nested
                    - second

                    1. ordered one
                    2. ordered two

                    > quoted line
                    > continued

                    ```csharp
                    var x = 1; // one
                    ```

                    ```json
                    {"a": 1}
                    ```

                    ```sh
                    echo $HOME
                    ```

                    | a | b |
                    | - | - |
                    | 1 | 2 |

                    <div class="note">
                    raw html
                    </div>

                    - [ ] task

                    ---

                    [dim]not markup[/]
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

    [Fact]
    public void Ascii_mode_replaces_bullets_and_quote_prefixes()
    {
        string markdown = """
            - bullet one
            - bullet two

            > quoted line
            > continued
            """;
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Write(
            new MarkdownRenderer(
                new TerminalCapabilities(ColorSystemSupport.NoColors, Unicode: false)
            ).Render(markdown)
        );

        GoldenFiles.AssertMatchesText(
            Path.Combine(GoldensDirectory, "ascii-lists-quote.txt"),
            console.Output
        );
    }
}
