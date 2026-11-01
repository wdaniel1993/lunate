namespace Lunate.Coding;

internal sealed partial record InteractiveSessionOptions
{
    /// <summary>The guard platform seam; null uses the current host and bounded live probe.</summary>
    public GuardProbe? BashGuardProbe { get; init; }
}
