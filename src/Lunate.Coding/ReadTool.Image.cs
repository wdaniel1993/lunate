using System.Globalization;
using Lunate.Agent;

namespace Lunate.Coding;

public sealed partial class ReadTool
{
    /// <summary>
    /// The image branch of a read: the limits, the vision gate and the header dimensions. The
    /// caller has already established that the magic bytes identify a supported image.
    /// </summary>
    private static ToolResult ReadImage(
        ResolvedPath resolved,
        IBinaryFileAccess files,
        ToolContext ctx,
        bool hasOffset,
        bool hasLimit
    )
    {
        if (hasOffset || hasLimit)
        {
            var parameters = (hasOffset, hasLimit) switch
            {
                (true, true) => "offset and limit",
                (true, false) => "offset",
                _ => "limit",
            };
            var verb = hasOffset && hasLimit ? "do not apply" : "does not apply";
            return Error(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{parameters} {verb} to images: {resolved.RelativePath} is an image and has no lines; omit {parameters} to read it."
                )
            );
        }

        if (ctx.Model is not { SupportsVision: true })
        {
            var model = ctx.Model?.Id ?? "unknown";
            return Error(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"model {model} does not support images; use a vision model to read {resolved.RelativePath}."
                )
            );
        }

        var length = files.Length(resolved.AbsolutePath);
        if (length > ImageLimits.MaxBytes)
        {
            return Error(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{resolved.RelativePath} is {length / 1048576d:F1} MB, over the {ImageLimits.MaxBytes / 1048576d:F1} MB image limit; {ImageLimits.DownscalingUnavailable}; use a smaller {ImageLimits.Formats} image."
                )
            );
        }

        // One bounded read: a file that grew after the length check cannot bypass the cap.
        var data = files.ReadPrefixBytes(resolved.AbsolutePath, ImageLimits.MaxBytes + 1);
        if (data.Length > ImageLimits.MaxBytes)
        {
            return Error(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{resolved.RelativePath} is at least {data.Length / 1048576d:F1} MB, over the {ImageLimits.MaxBytes / 1048576d:F1} MB image limit; {ImageLimits.DownscalingUnavailable}; use a smaller {ImageLimits.Formats} image."
                )
            );
        }
        if (!ImageFile.TryReadDimensions(data, out var mime, out var width, out var height))
        {
            return Error(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{resolved.RelativePath} is a binary file ({data.Length} bytes); read handles text files"
                )
            );
        }

        if (width > ImageLimits.MaxSide || height > ImageLimits.MaxSide)
        {
            return Error(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{resolved.RelativePath} is {width}x{height}, over the {ImageLimits.MaxSide}-pixel image side limit; {ImageLimits.DownscalingUnavailable}; use a smaller {ImageLimits.Formats} image."
                )
            );
        }

        return new ToolResult(
            string.Create(CultureInfo.InvariantCulture, $"[image {width}x{height}]"),
            IsError: false
        )
        {
            Images = [new ImageAttachment(mime, width, height, data)],
        };
    }
}
