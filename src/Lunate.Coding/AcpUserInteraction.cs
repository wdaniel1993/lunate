namespace Lunate.Coding;

/// <summary>
/// ACP mode's interaction: everything goes to the host log — the protocol has no notice frame, so
/// notices degrade to log lines and status is a no-op. No ACP path resolves an extension renderer
/// today (the machine modes skip blocks per the spec); <see cref="RenderExtension"/> logs a block
/// as unsent only if it is invoked. Nothing here ever reaches stdout.
/// </summary>
internal sealed class AcpUserInteraction(Action<string> log) : IUserInteraction
{
    public void Notify(UserNotice notice) => log(notice.Message);

    public void UpdateStatus(UserStatus status) { }

    public void RenderExtension(ExtensionBlock block) =>
        log(
            $"extension UI for tool '{block.ToolName}' was not sent: the Agent Client Protocol has no notice frame."
        );
}
