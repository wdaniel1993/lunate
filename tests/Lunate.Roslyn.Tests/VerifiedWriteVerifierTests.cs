using Lunate.Extensibility.Abstractions;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

[Collection(RoslynIntegrationCollection.Name)]
public sealed class VerifiedWriteVerifierTests
{
    [Fact]
    public async Task EditIntroducingGeneratedRegexCallIsVerifiedNotRefused()
    {
        using var fixture = Fixtures.CopyVerifiedProject("generated-regex");
        Fixtures.Restore(fixture, "GeneratedRegex.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        Assert.Equal(
            WorkspaceStatus.Loaded,
            (await backend.LoadAsync(CancellationToken.None)).Status
        );
        var verifier = new CSharpWriteVerifier(_ => backend);

        WriteVerificationResult result = await verifier.VerifyAsync(
            Request(
                fixture.Root,
                "Program.cs",
                File.ReadAllText(Path.Combine(fixture.Root, "Program.cs")),
                "using System.Text.RegularExpressions;\n"
                    + "internal static partial class Pattern { [GeneratedRegex(\"x\")] public static partial Regex Match(); }\n"
                    + "internal static class Program { static void Main() { Pattern.Match(); } }\n"
            ),
            CancellationToken.None
        );

        Assert.Equal(WriteVerificationStatus.Verified, result.Status);
        Assert.Contains("net10.0", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChecksOnlyWorkspaceLoadedTfm()
    {
        using var fixture = Fixtures.CopyVerifiedProject("multi-target");
        Fixtures.Restore(fixture, "MultiTarget.slnx");
        using var backend = new RoslynBackend(fixture.Root);
        Assert.Equal(
            WorkspaceStatus.Loaded,
            (await backend.LoadAsync(CancellationToken.None)).Status
        );
        var verifier = new CSharpWriteVerifier(_ => backend);
        const string baseline = "internal static class Program { static void Main() {} }\n";
        const string staged =
            "internal static class Program { static void Main() {\n"
            + "#if NET9_0\nMissingType.Call();\n#endif\n"
            + "} }\n";

        WriteVerificationResult result = await verifier.VerifyAsync(
            Request(fixture.Root, "Program.cs", baseline, staged),
            CancellationToken.None
        );

        Assert.Equal(WriteVerificationStatus.Verified, result.Status);
        Assert.Contains("net10.0", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnloadedWorkspaceReturnsUnverifiedWithoutLoad()
    {
        using var fixture = Fixtures.CopyVerifiedProject("generated-regex");
        using var backend = new RoslynBackend(fixture.Root);
        var verifier = new CSharpWriteVerifier(_ => backend);

        WriteVerificationResult result = await verifier.VerifyAsync(
            Request(fixture.Root, "Program.cs", string.Empty, "class Program {}\n"),
            CancellationToken.None
        );

        Assert.Equal(WriteVerificationStatus.Unverified, result.Status);
        Assert.Contains("workspace-not-loaded", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, backend.LoadCount);
    }

    private static WriteVerificationRequest Request(
        string root,
        string relativePath,
        string baseline,
        string staged
    ) => new(relativePath, baseline, staged, new WorkspaceInfo(root, null, null));
}
