namespace Lunate.Extensibility.Compile;

/// <summary>
/// The compile-time contract: the three host assemblies an extension may reference, read out of this
/// assembly's own manifest resources. They are embedded because a published self-contained
/// single-file binary has no assembly on disk to point a <c>MetadataReference</c> at — the managed
/// assemblies are bundled, so <c>Assembly.Location</c> is empty — and because an extension must
/// compile against the contract the host actually implements, not against whatever a package happens
/// to ship. The bytes are the assemblies this build resolved, so the metadata a single file compiles
/// against is the metadata a project reference would have exposed. The beside-the-host fallback
/// exists for a build that skipped the embedding target and yields the same metadata.
/// </summary>
internal static class HostContractReferences
{
    /// <summary>The manifest-resource folder the csproj's embedding target writes into.</summary>
    internal const string ResourcePrefix = "contracts/";

    /// <summary>
    /// The shared compile-time contract assemblies, in the order they are offered to the compiler:
    /// the tool and event contracts, the extension contract, then the MEAI abstractions tools are
    /// expressed in. <c>Lunate.Agent</c> and <c>Lunate.Ai</c> remain host-owned at load for runtime
    /// compatibility, but extensions do not compile directly against them. <c>System.Text.Json</c>
    /// is host-owned at load but needs no entry here: it is part of the reference pack.
    /// </summary>
    internal static readonly string[] ContractNames =
    [
        "Lunate.Contracts",
        "Lunate.Extensibility.Abstractions",
        "Microsoft.Extensions.AI.Abstractions",
    ];

    /// <summary>
    /// One image per contract assembly, by simple name. A contract that cannot be found at all is
    /// thrown rather than skipped: compiling without it would report a wall of CS0246 for the
    /// user's correct code, which is a worse lie than a failure naming the assembly.
    /// </summary>
    internal static IReadOnlyList<(string Name, byte[] Image)> All()
    {
        List<(string Name, byte[] Image)> images = [];
        foreach (string name in ContractNames)
        {
            images.Add((name, Image(name)));
        }

        return images;
    }

    private static byte[] Image(string name)
    {
        if (
            typeof(HostContractReferences).Assembly.GetManifestResourceStream(
                ResourcePrefix + name + ".dll"
            ) is
            { } resource
        )
        {
            using (resource)
            {
                using var memory = new MemoryStream();
                resource.CopyTo(memory);
                return memory.ToArray();
            }
        }

        // Not embedded: the host's own directory. In a single-file publish this is the directory
        // holding only the executable, which is why the embedded copy is the path that matters
        // there; `Assembly.Location` is deliberately not consulted — for a bundled assembly it is
        // empty by definition (IL3000), and the file-beside-the-host case already covers a build
        // that ships its assemblies loose.
        string beside = Path.Combine(AppContext.BaseDirectory, name + ".dll");
        if (File.Exists(beside))
        {
            return File.ReadAllBytes(beside);
        }

        throw new InvalidOperationException(
            FormattableString.Invariant(
                $"the compile contract '{name}' is neither embedded nor beside the host in '{AppContext.BaseDirectory}'; rebuild Lunate."
            )
        );
    }
}
