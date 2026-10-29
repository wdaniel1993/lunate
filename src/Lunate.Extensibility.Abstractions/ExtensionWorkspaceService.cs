namespace Lunate.Extensibility.Abstractions;

/// <summary>
/// One entry of a manifest's <c>workspaceServices</c> list: a per-workspace service the extension
/// registers by <paramref name="name"/>, which may be loaded for at most <paramref
/// name="maxLoadedWorkspaces"/> canonical worktree roots at once (the host may lower the cap; the
/// manifest cannot raise it past the host's own budget). The default cap is shared with the host's
/// default and the published requirement, so the value a manifest omits is the value the host uses.
/// </summary>
public sealed record ExtensionWorkspaceService(string Name, int MaxLoadedWorkspaces = 2);
