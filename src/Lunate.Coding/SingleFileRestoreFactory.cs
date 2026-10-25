using Lunate.Extensibility;
using Lunate.Extensibility.Restore;

namespace Lunate.Coding;

/// <summary>
/// The production restore seam. <see cref="Engine"/> is a factory, not an engine: constructing it
/// costs a delegate, and the NuGet-backed engine — and with it every <c>NuGet.*</c> assembly — is
/// created only when a restore actually runs. A session that only loads built extensions therefore
/// never has NuGet in the process, which is what the lazy-loading test pins.
/// </summary>
internal static class SingleFileRestoreFactory
{
    internal static Func<ISingleFileRestoreEngine?> Engine { get; } =
        static () => new NuGetRestoreEngine();
}
