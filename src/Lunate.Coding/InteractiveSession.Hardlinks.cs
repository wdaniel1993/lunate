using Lunate.Tui;

namespace Lunate.Coding;

internal sealed partial class InteractiveSession
{
    private void WarnAboutStateRootHardlinks() =>
        StateRootHardlinkScan.Report(
            _options.HardlinkScanRoot,
            _options.HardlinkScanFileSystem,
            warning => Notify(warning, NoticeLevel.Warning)
        );
}
