using System.Text;
using System.Text.RegularExpressions;
using Lunate.TestSupport;
using Xunit;

namespace Lunate.Coding.Tests;

/// <summary>
/// The documentation ships configuration examples readers copy verbatim, so every fenced block that
/// claims to be a settings, providers or auth file is loaded through the same code that reads the
/// real file. A block tagged <c>```json settings</c> (the info string carries the kind) that no
/// longer parses fails here, in the PR that broke it, rather than in a user's first run. The same
/// pass rejects a YAML block that describes Lunate configuration: nothing in the product reads
/// YAML, and a YAML example teaches a format the reader cannot use.
/// </summary>
public sealed class DocsConfigExamplesTests
{
    private static readonly string[] Kinds = ["settings", "providers", "auth"];

    private static readonly Regex Fence = new(
        @"^```(?<info>[^\r\n]*)\r?\n(?<body>.*?)^```\s*$",
        RegexOptions.Multiline | RegexOptions.Singleline
    );

    private static readonly Regex ConfiguredInYaml = new(
        @"^(schemaVersion|profiles|usages|decisions|providers|model|approvalsThreshold|routingProfiles)\s*:",
        RegexOptions.Multiline
    );

    [Fact]
    public void Every_tagged_config_example_loads_through_the_store_that_reads_it()
    {
        List<string> failures = [];
        foreach ((string path, string kind, string body) in Examples())
        {
            try
            {
                Load(kind, body);
            }
            catch (Exception exception)
            {
                failures.Add($"{Relative(path)} -> {kind}: {exception.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "A documented configuration example does not load:\n" + string.Join("\n\n", failures)
        );
    }

    [Fact]
    public void No_yaml_block_describes_lunate_configuration()
    {
        List<string> offenders = [];
        foreach (string path in Documents())
        {
            foreach (Match match in Fence.Matches(File.ReadAllText(path)))
            {
                string language = FirstToken(match.Groups["info"].Value);
                if (language is not ("yaml" or "yml"))
                {
                    continue;
                }

                if (ConfiguredInYaml.IsMatch(match.Groups["body"].Value))
                {
                    offenders.Add(
                        $"{Relative(path)}:{LineOf(File.ReadAllText(path), match.Index)}"
                    );
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Lunate reads JSON, never YAML; these blocks teach a format nothing parses:\n"
                + string.Join("\n", offenders)
        );
    }

    private static void Load(string kind, string body)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "lunate-docs-" + Guid.NewGuid().ToString("n")
        );
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, kind + ".json");
            File.WriteAllText(path, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            switch (kind)
            {
                case "settings":
                    SettingsStore.Resolve(path, _ => null);
                    break;
                case "providers":
                    ProviderStore.Load(
                        path,
                        AuthStore.Load(Path.Combine(directory, "auth.json")),
                        []
                    );
                    break;
                case "auth":
                    AuthStore.Load(path);
                    break;
                default:
                    throw new InvalidOperationException($"unknown kind '{kind}'");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<(string Path, string Kind, string Body)> Examples()
    {
        foreach (string path in Documents())
        {
            foreach (Match match in Fence.Matches(File.ReadAllText(path)))
            {
                string[] parts = match
                    .Groups["info"]
                    .Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && parts[0] == "json" && Kinds.Contains(parts[1]))
                {
                    yield return (path, parts[1], match.Groups["body"].Value);
                }
            }
        }
    }

    private static IEnumerable<string> Documents()
    {
        string root = RepositoryRoot.Find();
        yield return Path.Combine(root, "README.md");
        yield return Path.Combine(root, "CHANGELOG.md");
        foreach (
            string directory in new[] { Path.Combine(root, "docs"), Path.Combine(root, ".github") }
        )
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (
                string path in Directory.EnumerateFiles(
                    directory,
                    "*.md",
                    SearchOption.AllDirectories
                )
            )
            {
                yield return path;
            }
        }
    }

    private static string FirstToken(string info) =>
        info.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [var first, ..]
            ? first
            : string.Empty;

    private static string Relative(string path) =>
        Path.GetRelativePath(RepositoryRoot.Find(), path);

    private static int LineOf(string text, int index) => text[..index].Count(c => c == '\n') + 1;
}
