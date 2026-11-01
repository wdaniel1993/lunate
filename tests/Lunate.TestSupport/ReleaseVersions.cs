using System.Xml.Linq;

namespace Lunate.TestSupport;

/// <summary>
/// The versions a release derives from the tree's product version, read from
/// <c>Directory.Build.props</c> and resolved the same way MSBuild resolves them.
/// </summary>
/// <remarks>
/// The rule (ADR-0002's addendum, and <c>scripts/release-versions.sh</c> as the executable form):
/// an RC tag never publishes a stable version. The contract packages carry the API version plus the
/// product version's prerelease suffix, the preview packages carry the product version while it is a
/// prerelease and <c>&lt;version&gt;-preview.1</c> otherwise, and the template pins whatever contract
/// version that tag publishes.
///
/// Tests that read a csproj as XML see <c>$(ContractVersion)</c>, not a number. Resolving through
/// this class keeps them asserting the rule instead of a constant that is only true between
/// releases.
/// </remarks>
public static class ReleaseVersions
{
    private static readonly XDocument Props = XDocument.Load(
        Path.Combine(RepositoryRoot.Find(), "Directory.Build.props")
    );

    /// <summary>The product version: the tag without its leading <c>v</c>.</summary>
    public static string Product => Value("Version");

    /// <summary>The contract API version, which must equal <c>ExtensionApi.Current</c>.</summary>
    public static string Api => Value("ContractApiVersion");

    /// <summary>The tag's prerelease suffix, or empty for a final release.</summary>
    public static string Suffix
    {
        get
        {
            int dash = Product.IndexOf('-', StringComparison.Ordinal);
            return dash < 0 ? string.Empty : Product[dash..];
        }
    }

    /// <summary>The version of <c>Lunate.Contracts</c> and <c>Lunate.Extensibility.Abstractions</c>.</summary>
    public static string Contract => Api + Suffix;

    /// <summary>The version of <c>Lunate.Agent</c> and <c>Lunate.Extensibility.Testing</c>.</summary>
    public static string Preview => Suffix.Length > 0 ? Product : Product + "-preview.1";

    /// <summary>Resolves a csproj version expression to the version the release would publish.</summary>
    public static string Resolve(string value) =>
        value switch
        {
            "$(Version)" => Product,
            "$(ContractVersion)" => Contract,
            "$(PreviewVersion)" => Preview,
            "$(LunateContractsVersion)" => Contract,
            _ => value,
        };

    private static string Value(string name) => Props.Descendants(name).First().Value.Trim();
}
