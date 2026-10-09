using System.Text.Json;
using Lunate.Ai;

namespace Lunate.Coding.Tests;

internal static class PrintModeTestSupport
{
    internal const string ModelId = "gpt-4o-mini";

    internal static PrintModeOptions BaseOptions(TempDirectory temp, IChatClientFactory factory) =>
        new()
        {
            Factory = factory,
            ModelsPath = temp.File("models.json"),
            AuthPath = temp.File("auth.json"),
            SettingsPath = temp.File("settings.json"),
            SessionDirectory = temp.File("sessions"),
            WorkingDirectory = temp.Root,
            Environment = Environment(),
        };

    internal static Func<string, string?> Environment(
        string? model = ModelId,
        string? approval = null
    ) =>
        name =>
            name switch
            {
                "LUNATE_MODEL" => model,
                "LUNATE_APPROVAL" => approval,
                _ => null,
            };

    internal static async Task<(int ExitCode, string Output, string Errors)> RunAsync(
        PrintModeOptions options
    )
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exitCode = await PrintMode.RunAsync(
            options,
            output,
            errors,
            TestContext.Current.CancellationToken
        );
        // stderr diagnostics are human-readable and use the platform newline by design;
        // tests compare them line-by-line, so normalize to Unix endings here. stdout is
        // NOT normalized: the answer and the JSONL stream are canonical contracts.
        return (
            exitCode,
            output.ToString(),
            errors.ToString().Replace("\r\n", "\n", StringComparison.Ordinal)
        );
    }

    internal static string SingleSessionId(TempDirectory temp)
    {
        string[] files = Directory.GetFiles(temp.File("sessions"), "*.jsonl");
        return Path.GetFileNameWithoutExtension(Assert.Single(files));
    }

    internal static List<JsonElement> ParseLines(string jsonl)
    {
        var lines = new List<JsonElement>();
        foreach (string line in jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            lines.Add(document.RootElement.Clone());
        }

        return lines;
    }

    internal static string TypeOf(JsonElement line) => line.GetProperty("type").GetString()!;
}
