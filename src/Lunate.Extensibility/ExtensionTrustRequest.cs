namespace Lunate.Extensibility;

/// <summary>
/// One project-extension trust question: the descriptor to trust, whether this repository has
/// trusted the extension before (its files changed since), and — when an install provided it — the
/// one line that names the package it came from (design D7), so the prompt can say where the code
/// was chosen from without asking the host for anything else.
/// </summary>
public sealed record ExtensionTrustRequest(ExtensionDescriptor Descriptor, bool FilesChanged)
{
    /// <summary>
    /// <c>from package &lt;id&gt;@&lt;version&gt; (&lt;source&gt;)</c> for an installed extension;
    /// null for a hand-placed one.
    /// </summary>
    public string? FromPackage { get; init; }
}
