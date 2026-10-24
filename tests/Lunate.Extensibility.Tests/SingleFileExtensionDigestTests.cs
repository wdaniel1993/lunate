using System.Text;

namespace Lunate.Extensibility.Tests;

/// <summary>
/// The single-file trust digest (spec: "Trust before restore"): it covers the id, the declared
/// packages exactly as written, the source bytes including the header, and the lock bytes, so
/// editing any one of them asks the user again.
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

    private static readonly byte[] Lock = Encoding.UTF8.GetBytes(
        """
        {
          "lockVersion": 1
        }

        """
    );

    private static ExtensionPackageReference[] Packages =>
        [new ExtensionPackageReference("Octokit", "14.0.0", "Octokit@14.0.0")];

    private static string Baseline() => Compute(Id, Packages, Source, Lock);

    [Fact]
    public void The_digest_covers_the_id()
    {
        string baseline = Baseline();

        Assert.Equal(64, baseline.Length);
        Assert.NotEqual(baseline, Compute("acme-other", Packages, Source, Lock));
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

        Assert.NotEqual(baseline, Compute(Id, [], Source, Lock));
        Assert.NotEqual(Compute(Id, spaced, Source, Lock), Compute(Id, tight, Source, Lock));
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

        Assert.NotEqual(baseline, Compute(Id, Packages, Encoding.UTF8.GetBytes(bodyEdit), Lock));
        Assert.NotEqual(baseline, Compute(Id, Packages, Encoding.UTF8.GetBytes(headerEdit), Lock));
    }

    [Fact]
    public void The_digest_covers_the_lock_bytes()
    {
        string baseline = Baseline();
        byte[] edited = Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(Lock).Replace("\"lockVersion\": 1", "\"lockVersion\": 2")
        );
        Assert.NotEqual(Lock, edited);

        Assert.NotEqual(baseline, Compute(Id, Packages, Source, []));
        Assert.NotEqual(baseline, Compute(Id, Packages, Source, edited));
    }

    [Fact]
    public void The_digest_is_stable_for_the_same_bytes()
    {
        Assert.Equal(Baseline(), Baseline());
        Assert.Equal(Baseline(), Compute(Id, [.. Packages], [.. Source], [.. Lock]));
    }

    private static string Compute(
        string id,
        IReadOnlyList<ExtensionPackageReference> packages,
        byte[] source,
        byte[] lockBytes
    ) => SingleFileExtensionDigest.Compute(id, packages, source, lockBytes);
}
