using System.ComponentModel;
using System.Globalization;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>What a clipboard helper produced: one image, an empty clipboard, or no helper.</summary>
internal enum ClipboardImageStatus
{
    Image,
    Empty,
    Unavailable,
}

/// <summary>The clipboard probe result; <see cref="Helper"/> names the platform helper.</summary>
internal sealed record ClipboardImageResult(
    ClipboardImageStatus Status,
    ReadOnlyMemory<byte> Data,
    string Helper
)
{
    public static ClipboardImageResult Image(ReadOnlyMemory<byte> data, string helper) =>
        new(ClipboardImageStatus.Image, data, helper);

    public static ClipboardImageResult Empty(string helper) =>
        new(ClipboardImageStatus.Empty, ReadOnlyMemory<byte>.Empty, helper);

    public static ClipboardImageResult Unavailable(string helper) =>
        new(ClipboardImageStatus.Unavailable, ReadOnlyMemory<byte>.Empty, helper);
}

/// <summary>The seam behind <c>@clipboard</c>; tests substitute a fake instead of a real clipboard.</summary>
internal interface IClipboardImageSource
{
    Task<ClipboardImageResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The platform helper chain: <c>pngpaste</c> on macOS, <c>wl-paste</c> on Linux and PowerShell's
/// <c>Get-Clipboard -Format Image</c> on Windows. Helpers write to a temporary file because the
/// process seam decodes text; the file is removed after the bytes are read.
/// </summary>
internal sealed class PlatformClipboardImageSource(IProcessRunner? runner = null)
    : IClipboardImageSource
{
    private readonly IProcessRunner _runner = runner ?? new ProcessRunner();

    public async Task<ClipboardImageResult> ReadAsync(CancellationToken cancellationToken)
    {
        string helper = Helper();
        string path = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"lunate-clipboard-{Guid.NewGuid():N}.png")
        );
        try
        {
            ProcessRunResult result;
            try
            {
                result = await _runner
                    .RunAsync(
                        new ProcessRunRequest(Executable(), Arguments(path), Path.GetTempPath())
                        {
                            Timeout = TimeSpan.FromSeconds(10),
                        },
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
                when (exception is Win32Exception or IOException or InvalidOperationException)
            {
                return ClipboardImageResult.Unavailable(helper);
            }

            if (result.ExitCode != 0)
            {
                return ClipboardImageResult.Empty(helper);
            }

            byte[] data = File.Exists(path)
                ? await File.ReadAllBytesAsync(path, cancellationToken)
                : [];
            return data.Length == 0
                ? ClipboardImageResult.Empty(helper)
                : ClipboardImageResult.Image(data, helper);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ClipboardImageResult.Empty(helper);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string Helper() =>
        OperatingSystem.IsWindows() ? "PowerShell Get-Clipboard -Format Image"
        : OperatingSystem.IsMacOS() ? "pngpaste"
        : "wl-paste";

    private static string Executable() =>
        OperatingSystem.IsWindows() ? "powershell"
        : OperatingSystem.IsMacOS() ? "pngpaste"
        : "wl-paste";

    private static IReadOnlyList<string> Arguments(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            string escaped = path.Replace("'", "''", StringComparison.Ordinal);
            return
            [
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                "Add-Type -AssemblyName System.Windows.Forms; "
                    + "Add-Type -AssemblyName System.Drawing; "
                    + "$image = [System.Windows.Forms.Clipboard]::GetImage(); "
                    + $"if ($image -ne $null) {{ $image.Save('{escaped}', "
                    + "[System.Drawing.Imaging.ImageFormat]::Png) }",
            ];
        }

        if (OperatingSystem.IsMacOS())
        {
            return [path];
        }

        return ["--type", "image/png", "--output", path];
    }
}
