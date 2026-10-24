using Lunate.Extensibility.Abstractions;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// A temp store plus worktree for the single-file discovery tests, and the header source they
/// share. The store is <c>&lt;temp&gt;/store</c> (so <c>store/extensions</c> is the global root)
/// and the worktree is the temp root (so <c>.lunate/extensions</c> is the project root).
/// </summary>
internal sealed class SingleFileFixture(TempDirectory temp)
{
    internal const string Valid = """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue

        public static class Acme
        {
            public static void Register() { }
        }
        """;

    internal string Store { get; } = Path.Combine(temp.Root, "store");

    internal string Worktree { get; } = temp.Root;

    internal string GlobalRoot => Path.Combine(Store, "extensions");

    internal string ProjectRoot => Path.Combine(Worktree, ".lunate", "extensions");

    internal string TrustFile => Path.Combine(Store, "trust.json");

    internal static string Header(string id) => $"#:extension {id}@1.0.0\n#:tool fetch_issue\n";

    internal string WriteGlobal(string name, string source) => Write(GlobalRoot, name, source);

    internal string WriteProject(string name, string source) => Write(ProjectRoot, name, source);

    internal ExtensionLoader Loader(IExtensionLog? log = null) =>
        new(TestExtensions.Options(Store, log));

    internal ExtensionTrustStore Trust() => new(TestExtensions.Options(Store));

    internal IReadOnlyList<SingleFileExtension> Discover(
        ExtensionLoader? loader = null,
        IExtensionLog? log = null
    )
    {
        ExtensionLoader resolved = loader ?? Loader(log);
        resolved.Discover(Worktree, "repo");
        return resolved.SingleFileExtensions;
    }

    private static string Write(string root, string name, string source)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, name);
        File.WriteAllText(path, source);
        return path;
    }
}
