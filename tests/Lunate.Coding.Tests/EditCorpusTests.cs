using System.Text.Json;
using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class EditCorpusTests
{
    private static readonly ToolContext Context = new("unused", new NullAgentEvents());

    public static TheoryData<string> Cases
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (var directory in Directory.GetDirectories(CorpusRoot).Order())
            {
                cases.Add(Path.GetFileName(directory));
            }

            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Every_corpus_case_passes(string name)
    {
        var caseRoot = Path.Combine(CorpusRoot, name);
        var request = JsonDocument
            .Parse(File.ReadAllText(Path.Combine(caseRoot, "request.json")))
            .RootElement;
        using var temp = new TempDirectory();
        File.Copy(Path.Combine(caseRoot, "input"), temp.File("input.txt"));
        var args = JsonSerializer.SerializeToElement(
            new
            {
                path = "input.txt",
                old_text = request.GetProperty("old_text").GetString()!,
                new_text = request.GetProperty("new_text").GetString()!,
            }
        );

        var result = await new EditTool(new Workspace(temp.Root)).ExecuteAsync(
            args,
            Context,
            CancellationToken.None
        );

        var expectedError = Path.Combine(caseRoot, "expected-error.txt");
        if (File.Exists(expectedError))
        {
            Assert.True(result.IsError, $"{name}: expected an error, got: {result.Output}");
            Assert.Equal(File.ReadAllText(expectedError).TrimEnd('\r', '\n'), result.Output);
        }
        else
        {
            Assert.False(result.IsError, $"{name}: {result.Output}");
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(caseRoot, "expected")),
                File.ReadAllBytes(temp.File("input.txt"))
            );
        }
    }

    private static string CorpusRoot { get; } = FindCorpusRoot();

    private static string FindCorpusRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "lunate.sln")))
            {
                return Path.Combine(directory.FullName, "tests", "fixtures", "edit-corpus");
            }
        }

        throw new InvalidOperationException(
            $"Could not find lunate.sln above {AppContext.BaseDirectory}."
        );
    }
}
