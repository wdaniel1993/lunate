using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Lunate.Agent;

/// <summary>
/// A reference to an image in the content-addressed image store, recorded in a session message
/// instead of the image bytes: the lowercase hex SHA-256 digest, the media type and the pixel
/// dimensions. Session files stay small and deterministic because they never embed image data.
/// </summary>
public sealed class ImageContentReference : AIContent
{
    /// <summary>Creates an image reference from its digest, media type and pixel dimensions.</summary>
    public ImageContentReference(string Digest, string MimeType, int Width, int Height)
    {
        this.Digest = Digest;
        this.MimeType = MimeType;
        this.Width = Width;
        this.Height = Height;
    }

    /// <summary>The lowercase hex SHA-256 digest of the image bytes.</summary>
    [JsonPropertyName("digest")]
    public string Digest { get; }

    /// <summary>The image's media type, for example <c>image/png</c>.</summary>
    [JsonPropertyName("mime")]
    public string MimeType { get; }

    /// <summary>The image width in pixels.</summary>
    [JsonPropertyName("width")]
    public int Width { get; }

    /// <summary>The image height in pixels.</summary>
    [JsonPropertyName("height")]
    public int Height { get; }
}
