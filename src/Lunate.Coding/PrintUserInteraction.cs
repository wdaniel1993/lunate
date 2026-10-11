namespace Lunate.Coding;

/// <summary>
/// Print mode's interaction: notices become <c>lunate: …</c> lines on stderr; status and extension
/// blocks are no-ops. Stdout stays byte-pure in both the text and JSON variants.
/// </summary>
internal sealed class PrintUserInteraction(TextWriter errors) : IUserInteraction
{
    public void Notify(UserNotice notice) => errors.WriteLine($"lunate: {notice.Message}");

    public void UpdateStatus(UserStatus status) { }

    public void RenderExtension(ExtensionBlock block) { }
}
