namespace Lunate.Agent;

/// <summary>
/// One image attachment as a tool or a user input hands it to the loop: the media type, the pixel
/// dimensions the producer determined and the image bytes. The loop stores the bytes in the
/// session's image store and keeps only an <see cref="ImageContentReference"/> in the history.
/// </summary>
public sealed record ImageAttachment(
    string MimeType,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Data
);
