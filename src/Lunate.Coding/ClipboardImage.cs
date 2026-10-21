using System.ComponentModel;
using Lunate.Agent;
using Lunate.Extensibility.Abstractions;

namespace Lunate.Coding;

/// <summary>What a clipboard helper produced: one image, an empty clipboard, or no helper.</summary>
internal enum ClipboardImageStatus
{
    Image,
    Empty,
    Unavailable,
    TooLarge,
    TimedOut,
}

/// <summary>
/// The clipboard probe result; <see cref="Helper"/> names the platform helper and
/// <see cref="Length"/> is the helper's output size when it was refused as too large.
/// </summary>
internal sealed record ClipboardImageResult(
    ClipboardImageStatus Status,
    ReadOnlyMemory<byte> Data,
    string Helper,
    long Length = 0
)
{
    public static ClipboardImageResult Image(ReadOnlyMemory<byte> data, string helper) =>
        new(ClipboardImageStatus.Image, data, helper, data.Length);

    public static ClipboardImageResult Empty(string helper) =>
        new(ClipboardImageStatus.Empty, ReadOnlyMemory<byte>.Empty, helper);

    public static ClipboardImageResult Unavailable(string helper) =>
        new(ClipboardImageStatus.Unavailable, ReadOnlyMemory<byte>.Empty, helper);

    public static ClipboardImageResult TooLarge(long length, string helper) =>
        new(ClipboardImageStatus.TooLarge, ReadOnlyMemory<byte>.Empty, helper, length);

    public static ClipboardImageResult TimedOut(string helper) =>
        new(ClipboardImageStatus.TimedOut, ReadOnlyMemory<byte>.Empty, helper);
}

/// <summary>The seam behind <c>@clipboard</c>; tests substitute a fake instead of a real clipboard.</summary>
internal interface IClipboardImageSource
{
    Task<ClipboardImageResult> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The platform helper chain: <c>pngpaste</c> on macOS, <c>wl-paste</c> on Linux and PowerShell's
/// <c>Get-Clipboard -Format Image</c> on Windows. Helpers write to a private temporary directory
/// because the process seam decodes text; the output is size-capped before it is read, and the
/// directory is removed after the bytes are read.
/// </summary>
internal sealed class PlatformClipboardImageSource(IProcessRunner? runner = null)
    : IClipboardImageSource
{
    private readonly IProcessRunner _runner = runner ?? new ProcessRunner();

    public async Task<ClipboardImageResult> ReadAsync(CancellationToken cancellationToken)
    {
        string helper = Helper();
        string? directory = null;
        try
        {
            // CreateTempSubdirectory is 0700 on Unix, so the image is not readable by other
            // local users while the helper writes it.
            directory = Directory.CreateTempSubdirectory("lunate-clipboard-").FullName;
            string path = Path.Combine(directory, "clipboard.png");
            ProcessRunResult result;
            try
            {
                result = await _runner
                    .RunAsync(
                        new ProcessRunRequest(Executable(), Arguments(path), directory)
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

            if (result.TimedOut)
            {
                return ClipboardImageResult.TimedOut(helper);
            }

            if (result.ExitCode != 0 || !File.Exists(path))
            {
                return ClipboardImageResult.Empty(helper);
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            long length = stream.Length;
            if (length > ImageLimits.MaxBytes)
            {
                return ClipboardImageResult.TooLarge(length, helper);
            }

            if (length == 0)
            {
                return ClipboardImageResult.Empty(helper);
            }

            byte[] data = new byte[length];
            stream.ReadExactly(data);
            return ClipboardImageResult.Image(data, helper);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ClipboardImageResult.Empty(helper);
        }
        finally
        {
            if (directory is not null)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception exception)
                    when (exception is IOException or UnauthorizedAccessException) { }
            }
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
