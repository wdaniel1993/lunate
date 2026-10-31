namespace Lunate.Coding;

internal sealed partial record InteractiveSessionOptions
{
    /// <summary>The startup-only state root to scan for external hardlinks; null disables the scan.</summary>
    public string? HardlinkScanRoot { get; init; }

    /// <summary>The hardlink scan's filesystem seam; null uses the host file system.</summary>
    public IHardlinkScanFileSystem? HardlinkScanFileSystem { get; init; }
}
