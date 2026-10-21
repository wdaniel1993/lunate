using System.Globalization;
using System.Text;
using Lunate.Agent;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>Whether a resolved submission starts a run, is refused, or stays quiet after a notice.</summary>
internal enum ImageInputOutcome
{
    Ready,
    Refused,
    Quiet,
}

/// <summary>
/// One submission after image resolution: the text with placeholders and the attachments to carry.
/// A refusal names the reason; <see cref="ImageInputOutcome.Quiet"/> suppresses a repeated notice.
/// </summary>
internal sealed record ImageInputResult(
    ImageInputOutcome Outcome,
    string Text,
    IReadOnlyList<ImageAttachment>? Images = null,
    string? Error = null
)
{
    public static ImageInputResult Ready(string text, List<ImageAttachment>? images) =>
        new(ImageInputOutcome.Ready, text, images is { Count: > 0 } ? images : null);

    public static ImageInputResult Refused(string error) =>
        new(ImageInputOutcome.Refused, string.Empty, Error: error);

    public static ImageInputResult Quiet() => new(ImageInputOutcome.Quiet, string.Empty);
}

/// <summary>
/// Resolves <c>@path</c>, a dropped image path and <c>@clipboard</c> after the input hook chain:
/// magic bytes decide the format, the shared limits are enforced, and each accepted image becomes
/// one attachment while its token becomes the <c>[image WxH]</c> placeholder. The resolver reads
/// files only; the harness stores and sends the bytes.
/// </summary>
internal sealed class ImageInputResolver
{
    private static readonly string[] ImageExtensions =
    [
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp",
        ".svg",
        ".bmp",
        ".tif",
        ".tiff",
        ".ico",
        ".avif",
        ".heic",
    ];

    private readonly string _workingDirectory;
    private readonly ModelInfo? _model;
    private readonly IClipboardImageSource? _clipboard;
    private bool _clipboardUnavailableNotified;

    public ImageInputResolver(
        string workingDirectory,
        ModelInfo? model,
        IClipboardImageSource? clipboard = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _model = model;
        _clipboard = clipboard;
    }

    /// <summary>
    /// Whether a whole submission is one existing supported image path — the drag/drop shape. The
    /// interactive frontend checks this before the slash-command classification, because an
    /// absolute path starts with <c>/</c>.
    /// </summary>
    internal static bool IsDroppedImagePath(string workingDirectory, string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('@'))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(
                Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(workingDirectory, trimmed)
            );
            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length > ImageLimits.MaxBytes)
            {
                return false;
            }

            byte[] data = File.ReadAllBytes(fullPath);
            return ImageFile.TryReadDimensions(data, out _, out _, out _);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return false;
        }
    }

    public async ValueTask<ImageInputResult> ResolveAsync(
        string text,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(text);
        List<ImageAttachment> attachments = [];
        Dictionary<string, ImageAttachment> seen = new(StringComparer.Ordinal);
        ImageAttachment? clipboardImage = null;
        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            char current = text[index];
            bool tokenStart = current == '@' && (index == 0 || char.IsWhiteSpace(text[index - 1]));
            if (!tokenStart)
            {
                builder.Append(current);
                index++;
                continue;
            }

            int end;
            string path;
            if (index + 1 < text.Length && text[index + 1] == '"')
            {
                int close = text.IndexOf('"', index + 2);
                if (close < 0)
                {
                    builder.Append(current);
                    index++;
                    continue;
                }

                path = text[(index + 2)..close];
                end = close + 1;
            }
            else
            {
                end = index + 1;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                path = text[(index + 1)..end];
            }

            if (path.Length == 0)
            {
                builder.Append(current);
                index++;
                continue;
            }

            if (string.Equals(path, "clipboard", StringComparison.OrdinalIgnoreCase))
            {
                if (clipboardImage is null)
                {
                    ImageInputResult? refusal = await ReadClipboardAsync(
                            attachments,
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                    if (refusal is not null)
                    {
                        return refusal;
                    }

                    clipboardImage = attachments[^1];
                }

                builder.Append(Placeholder(clipboardImage));
                index = end;
                continue;
            }

            ImageResolution file = ResolveFile(path);
            if (file.Error is { } error)
            {
                return ImageInputResult.Refused(error);
            }

            if (file.Attachment is not { } attachment)
            {
                builder.Append(text, index, end - index);
                index = end;
                continue;
            }

            string key = file.FullPath!;
            if (!seen.ContainsKey(key))
            {
                ImageInputResult? limitError = Add(attachments, seen, key, attachment);
                if (limitError is not null)
                {
                    return limitError;
                }
            }

            builder.Append(Placeholder(attachment));
            index = end;
        }

        string resolved = builder.ToString();
        if (attachments.Count == 0 && TryResolveDroppedPath(text, out ImageResolution dropped))
        {
            if (dropped.Attachment is not { } attachment)
            {
                return ImageInputResult.Refused(dropped.Error!);
            }

            attachments.Add(attachment);
            resolved = Placeholder(attachment);
        }

        if (attachments.Count > 0 && _model is not { SupportsVision: true })
        {
            return ImageInputResult.Refused(
                $"model {_model?.Id ?? "unknown"} does not support images; use a vision model to attach images."
            );
        }

        return ImageInputResult.Ready(resolved, attachments);
    }

    private async ValueTask<ImageInputResult?> ReadClipboardAsync(
        List<ImageAttachment> attachments,
        CancellationToken cancellationToken
    )
    {
        if (_clipboard is null)
        {
            return ClipboardRefusal("no clipboard image source is available");
        }

        ClipboardImageResult result = await _clipboard.ReadAsync(cancellationToken);
        switch (result.Status)
        {
            case ClipboardImageStatus.Unavailable:
                return ClipboardRefusal(
                    $"the clipboard image helper {result.Helper} was not found; install it to attach a clipboard image with @clipboard."
                );
            case ClipboardImageStatus.Empty:
                return ImageInputResult.Refused(
                    "the clipboard does not contain an image; copy an image and submit @clipboard again."
                );
        }

        if (result.Data.Length > ImageLimits.MaxBytes)
        {
            return ImageInputResult.Refused(TooLarge("the clipboard image", result.Data.Length));
        }

        if (
            !ImageFile.TryReadDimensions(
                result.Data.Span,
                out string mime,
                out int width,
                out int height
            )
        )
        {
            return ImageInputResult.Refused(
                $"the clipboard image is not a supported image; supported formats are {ImageLimits.Formats}."
            );
        }

        if (width > ImageLimits.MaxSide || height > ImageLimits.MaxSide)
        {
            return ImageInputResult.Refused(TooWide("the clipboard image", width, height));
        }

        if (RefuseAboveLimit(attachments) is { } limitError)
        {
            return limitError;
        }

        attachments.Add(new ImageAttachment(mime, width, height, result.Data));
        return null;
    }

    private ImageInputResult? ClipboardRefusal(string message)
    {
        if (_clipboardUnavailableNotified)
        {
            return ImageInputResult.Quiet();
        }

        _clipboardUnavailableNotified = true;
        return ImageInputResult.Refused(message);
    }

    private static ImageInputResult? RefuseAboveLimit(List<ImageAttachment> attachments)
    {
        if (attachments.Count < ImageLimits.MaxPerMessage)
        {
            return null;
        }

        return ImageInputResult.Refused(
            string.Create(
                CultureInfo.InvariantCulture,
                $"a message can carry at most {ImageLimits.MaxPerMessage} images; remove one and try again."
            )
        );
    }

    private static ImageInputResult? Add(
        List<ImageAttachment> attachments,
        Dictionary<string, ImageAttachment> seen,
        string key,
        ImageAttachment attachment
    )
    {
        if (attachments.Count >= ImageLimits.MaxPerMessage)
        {
            return ImageInputResult.Refused(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"a message can carry at most {ImageLimits.MaxPerMessage} images; remove one and try again."
                )
            );
        }

        attachments.Add(attachment);
        seen[key] = attachment;
        return null;
    }

    private ImageResolution ResolveFile(string path)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(
                Path.IsPathRooted(path) ? path : Path.Combine(_workingDirectory, path)
            );
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ImageResolution.Failure(
                $"cannot resolve image path {path}: {exception.Message}"
            );
        }

        if (!File.Exists(fullPath))
        {
            return HasImageExtension(path)
                ? ImageResolution.Failure(
                    $"image file not found: {path}; check the path or attach a {ImageLimits.Formats} image."
                )
                : ImageResolution.Text;
        }

        long length;
        byte[] data;
        try
        {
            length = new FileInfo(fullPath).Length;
            if (length > ImageLimits.MaxBytes)
            {
                return ImageResolution.Failure(TooLarge(path, length));
            }

            data = File.ReadAllBytes(fullPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ImageResolution.Failure($"cannot read image {path}: {exception.Message}");
        }

        if (!ImageFile.TryReadDimensions(data, out string mime, out int width, out int height))
        {
            return HasImageExtension(path)
                ? ImageResolution.Failure(
                    $"{path} is not a supported image; supported formats are {ImageLimits.Formats}."
                )
                : ImageResolution.Text;
        }

        if (width > ImageLimits.MaxSide || height > ImageLimits.MaxSide)
        {
            return ImageResolution.Failure(TooWide(path, width, height));
        }

        return new ImageResolution(
            new ImageAttachment(mime, width, height, data),
            fullPath,
            Error: null
        );
    }

    private bool TryResolveDroppedPath(string text, out ImageResolution resolution)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('@'))
        {
            resolution = ImageResolution.Text;
            return false;
        }

        resolution = ResolveFile(trimmed);
        return resolution.Attachment is not null;
    }

    private static bool HasImageExtension(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static string Placeholder(ImageAttachment attachment) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"[image {attachment.Width}x{attachment.Height}]"
        );

    private static string TooLarge(string path, long length) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{path} is {length / 1048576d:F1} MB, over the {ImageLimits.MaxBytes / 1048576d:F1} MB image limit; {ImageLimits.DownscalingUnavailable}; use a smaller {ImageLimits.Formats} image."
        );

    private static string TooWide(string path, int width, int height) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{path} is {width}x{height}, over the {ImageLimits.MaxSide}-pixel image side limit; {ImageLimits.DownscalingUnavailable}; use a smaller {ImageLimits.Formats} image."
        );

    private readonly record struct ImageResolution(
        ImageAttachment? Attachment,
        string? FullPath,
        string? Error
    )
    {
        public static ImageResolution Text => new(null, null, null);

        public static ImageResolution Failure(string error) => new(null, null, error);
    }
}
