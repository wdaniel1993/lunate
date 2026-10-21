namespace Lunate.Agent;

/// <summary>The image input limits shared by the input resolver and the tools.</summary>
public static class ImageLimits
{
    /// <summary>The largest accepted image file: 5 MiB.</summary>
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>The largest accepted width or height in pixels.</summary>
    public const int MaxSide = 4096;

    /// <summary>The most images one message may carry.</summary>
    public const int MaxPerMessage = 4;

    /// <summary>The accepted image formats, as the refusal text names them.</summary>
    public const string Formats = "PNG, JPEG, GIF or WebP";

    /// <summary>The refusal suffix for an image that would need resizing.</summary>
    public const string DownscalingUnavailable = "downscaling is not available yet";
}
