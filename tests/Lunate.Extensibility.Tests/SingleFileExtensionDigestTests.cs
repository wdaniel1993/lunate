using System.Text;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The single-file trust digest (spec: "Trust before restore"): it covers the id, the declared
/// packages exactly as written, the source bytes including the header and the lock's <b>graph</b>
/// half, so editing any one of them asks the user again. It deliberately does not cover the lock's
/// <b>toolset</b> half, because a .NET patch moves that without changing what the user approved.
/// </summary>
public sealed class SingleFileExtensionDigestTests
{
    private const string Id = "acme-tools";

    private static readonly byte[] Source = Encoding.UTF8.GetBytes(
        """
        #:extension acme-tools@1.0.0
        #:tool fetch_issue
        #:package Octokit@14.0.0

        public static class Acme
        {
            public static void Register() { }
        }

        """
    );

    private static ExtensionPackageReference[] Packages =>
        [new ExtensionPackageReference("Octokit", "14.0.0", "Octokit@14.0.0")];

    private static string Baseline() => Compute(Id, Packages, Source, Locked());

    [Fact]
    public void The_digest_covers_the_id()
    {
        string baseline = Baseline();

        Assert.Equal(64, baseline.Length);
        Assert.NotEqual(baseline, Compute("acme-other", Packages, Source, Locked()));
    }

    [Fact]
    public void The_digest_covers_the_declared_packages_exactly_as_written()
    {
        string baseline = Baseline();
        // A range written with a space is different bytes in the file, so it is a different
        // decision even when it resolves to the same version.
        ExtensionPackageReference[] spaced =
        [
            new ExtensionPackageReference(
                "Octokit",
                "[14.0.0, 15.0.0)",
                "Octokit@[14.0.0, 15.0.0)"
            ),
        ];
        ExtensionPackageReference[] tight =
        [
            new ExtensionPackageReference("Octokit", "[14.0.0,15.0.0)", "Octokit@[14.0.0,15.0.0)"),
        ];

        Assert.NotEqual(baseline, Compute(Id, [], Source, Locked()));
        Assert.NotEqual(
            Compute(Id, spaced, Source, Locked()),
            Compute(Id, tight, Source, Locked())
        );
    }

    [Fact]
    public void The_digest_covers_the_source_bytes_including_the_header()
    {
        string baseline = Baseline();
        string bodyEdit = Encoding
            .UTF8.GetString(Source)
            .Replace("Register() { }", "Register() {  }");
        string headerEdit = Encoding
            .UTF8.GetString(Source)
            .Replace("#:tool fetch_issue", "#:tool fetch_issue2");

        Assert.NotEqual(
            baseline,
            Compute(Id, Packages, Encoding.UTF8.GetBytes(bodyEdit), Locked())
        );
        Assert.NotEqual(
            baseline,
            Compute(Id, Packages, Encoding.UTF8.GetBytes(headerEdit), Locked())
        );
    }

    [Fact]
    public void The_digest_covers_the_lock_graph()
    {
        string baseline = Baseline();

        // No lock yet: the graph segment is absent, so the first-trust digest is a different value.
        Assert.NotEqual(baseline, Compute(id: Id, Packages, Source, graph: []));

        // A different resolution — another version, and therefore other graph bytes — is a
        // different decision, whether or not the file itself changed.
        Assert.NotEqual(baseline, ComputeWith(Locked(packageVersion: "14.1.0")));
        Assert.NotEqual(baseline, ComputeWith(Locked(sourceDigest: "another-source")));
    }

    [Fact]
    public void The_digest_does_not_cover_the_toolset()
    {
        ExtensionLock before = Locked();
        ExtensionLock patched = Locked(runtime: "10.0.14", packVersion: "10.0.14");

        // The lock file itself moved — a different runtime and pack is a different toolset …
        Assert.NotEqual(ExtensionLockJson.ToBytes(before), ExtensionLockJson.ToBytes(patched));

        // … but the graph bytes, and therefore the trust digest, are identical: a .NET patch cannot
        // make an approved file ask again.
        Assert.Equal(ExtensionLockJson.GraphBytes(before), ExtensionLockJson.GraphBytes(patched));
        Assert.Equal(ComputeWith(before), ComputeWith(patched));
        Assert.Equal(Baseline(), ComputeWith(patched));
    }

    [Fact]
    public void The_digest_is_stable_for_the_same_bytes()
    {
        Assert.Equal(Baseline(), Baseline());
        Assert.Equal(Baseline(), Compute(Id, [.. Packages], [.. Source], Locked()));
        Assert.Equal(
            ExtensionLockJson.GraphBytes(Locked()),
            ExtensionLockJson.GraphBytes(Locked())
        );
    }

    /// <summary>A whole lock, varied only where a test needs another graph or another toolset.</summary>
    private static ExtensionLock Locked(
        string packageVersion = "14.0.0",
        string runtime = "10.0.13",
        string packVersion = "10.0.13",
        string sourceDigest = "source-digest"
    ) =>
        new()
        {
            ExtensionId = Id,
            ExtensionVersion = "1.0.0",
            SourceDigest = sourceDigest,
            PackagesWritten = ["Octokit@14.0.0"],
            Toolset = new ExtensionLockToolset
            {
                Tfm = "net10.0",
                Rid = "osx-arm64",
                Runtime = runtime,
                ReferencePack = new ExtensionLockReferencePack
                {
                    Id = "Microsoft.NETCore.App.Ref",
                    Version = packVersion,
                    Rule = ReferencePackRules.Exact,
                    Sha512 = "cmVmLXBhY2s=",
                    Assets = ["ref/net10.0/System.Runtime.dll"],
                },
            },
            Sources = ["nuget.org"],
            Packages =
            [
                new ExtensionLockPackage
                {
                    Id = "Octokit",
                    Version = packageVersion,
                    Sha512 = "cGFja2FnZQ==",
                    Requested = "Octokit@14.0.0",
                    Direct = true,
                    Files = ["lib/net10.0/Octokit.dll", "Octokit.nuspec"],
                    Compile = ["lib/net10.0/Octokit.dll"],
                    Runtime = ["lib/net10.0/Octokit.dll"],
                },
            ],
        };

    private static string ComputeWith(ExtensionLock @lock) =>
        Compute(Id, Packages, Source, ExtensionLockJson.GraphBytes(@lock));

    private static string Compute(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        byte[] source,
        byte[] graph
    ) => SingleFileExtensionDigest.Compute(id, packages, source, graph);

    private static string Compute(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        byte[] source,
        ExtensionLock graph
    ) => Compute(id, packages, source, ExtensionLockJson.GraphBytes(graph));
}
