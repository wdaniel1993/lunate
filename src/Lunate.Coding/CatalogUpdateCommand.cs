using System.Globalization;
using System.Text;
using Lunate.Ai;

namespace Lunate.Coding;

/// <summary>
/// The injectable seams of <c>--update-catalog</c>: the managed cache path and the HTTP transport.
/// </summary>
internal sealed record CatalogUpdateSeams(
    string? ManagedPath = null,
    HttpMessageHandler? Handler = null
);

/// <summary>
/// The explicit catalog refresh: read a models.dev snapshot (an http(s) URL, a <c>file://</c> URI or
/// a filesystem path), map it through the one catalog mapper, and replace exactly one target
/// atomically. It composes no session and calls no model.
/// </summary>
internal static class CatalogUpdateCommand
{
    internal const string Usage =
        "Usage: lunate --update-catalog [--output <path>] [--source <path-or-url>]";

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Parses the flags after <c>--update-catalog</c> and runs the refresh.</summary>
    internal static int Run(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter errors,
        CancellationToken ct,
        CatalogUpdateSeams? seams = null
    )
    {
        string? outputPath = null;
        string? source = null;
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "-h":
                case "--help":
                    output.WriteLine(Usage);
                    return 0;
                case "--output":
                    if (!TryTakeValue(args, ref index, out outputPath))
                    {
                        return UsageError(errors, "--output needs a path");
                    }

                    break;
                case "--source":
                    if (!TryTakeValue(args, ref index, out source))
                    {
                        return UsageError(errors, "--source needs a path or URL");
                    }

                    break;
                default:
                    if (TryInline(argument, "--output", out string? inlineOutput))
                    {
                        if (string.IsNullOrWhiteSpace(inlineOutput))
                        {
                            return UsageError(errors, "--output needs a path");
                        }

                        outputPath = inlineOutput;
                    }
                    else if (TryInline(argument, "--source", out string? inlineSource))
                    {
                        if (string.IsNullOrWhiteSpace(inlineSource))
                        {
                            return UsageError(errors, "--source needs a path or URL");
                        }

                        source = inlineSource;
                    }
                    else
                    {
                        errors.WriteLine(
                            $"lunate: unexpected argument '{argument}' for --update-catalog"
                        );
                        errors.WriteLine(Usage);
                        return 2;
                    }

                    break;
            }
        }

        return RunAsync(outputPath, source ?? ModelsDevCatalog.SourceUrl, seams, output, errors, ct)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Reads the snapshot, maps it, and writes the target atomically. A failure at any stage names
    /// the stage and leaves the existing catalog byte-identical.
    /// </summary>
    internal static async Task<int> RunAsync(
        string? outputPath,
        string source,
        CatalogUpdateSeams? seams,
        TextWriter output,
        TextWriter errors,
        CancellationToken ct
    )
    {
        string target = Path.GetFullPath(
            outputPath ?? seams?.ManagedPath ?? ModelCatalog.DefaultManagedFilePath()
        );

        string json;
        try
        {
            json = await ReadSourceAsync(source, seams?.Handler, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return LoadFailure(errors, source, "the request timed out after 30 seconds");
        }
        catch (SourceResponseException exception)
        {
            return LoadFailure(errors, source, exception.Message);
        }
        catch (Exception exception)
        {
            return LoadFailure(errors, source, Detail(exception));
        }

        CatalogSnapshot snapshot;
        try
        {
            snapshot = ModelsDevCatalog.Generate(json, ProviderPresets.All);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine(
                $"lunate: the model catalog from {source} is invalid: {Detail(exception)}. The existing catalog is unchanged."
            );
            return 1;
        }

        try
        {
            WriteAtomically(target, snapshot.Json);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            errors.WriteLine(
                $"lunate: could not write the model catalog to {target}: {Detail(exception)}. The existing catalog is unchanged."
            );
            return 1;
        }

        output.WriteLine($"wrote {target}");
        output.WriteLine($"models: {snapshot.Models.Count.ToString(CultureInfo.InvariantCulture)}");
        output.WriteLine(
            $"providers: {snapshot.Models.Select(static model => model.Provider).Distinct(StringComparer.Ordinal).Count().ToString(CultureInfo.InvariantCulture)}"
        );
        output.WriteLine($"sha256: {snapshot.SourceSha256}");
        return 0;
    }

    private static bool TryTakeValue(IReadOnlyList<string> args, ref int index, out string? value)
    {
        if (index + 1 >= args.Count || string.IsNullOrWhiteSpace(args[index + 1]))
        {
            value = null;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static bool TryInline(string argument, string name, out string? value)
    {
        string prefix = name + "=";
        if (argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = argument[prefix.Length..];
            return true;
        }

        value = null;
        return false;
    }

    private static int UsageError(TextWriter errors, string message)
    {
        errors.WriteLine($"lunate: {message}");
        errors.WriteLine(Usage);
        return 2;
    }

    private static async Task<string> ReadSourceAsync(
        string source,
        HttpMessageHandler? handler,
        CancellationToken ct
    )
    {
        if (
            Uri.TryCreate(source, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        )
        {
            using var client = new HttpClient(
                handler ?? new HttpClientHandler(),
                disposeHandler: handler is null
            )
            {
                Timeout = RequestTimeout,
            };
            using HttpResponseMessage response = await client.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new SourceResponseException(
                    $"the source answered {((int)response.StatusCode).ToString(CultureInfo.InvariantCulture)} ({response.ReasonPhrase})"
                );
            }

            return await response.Content.ReadAsStringAsync(ct);
        }

        string path =
            Uri.TryCreate(source, UriKind.Absolute, out uri) && uri.IsFile ? uri.LocalPath : source;
        return await File.ReadAllTextAsync(path, ct);
    }

    /// <summary>
    /// Writes a uniquely named temp file in the target directory, flushes it to disk and moves it
    /// over the target. The temp file never outlives the call.
    /// </summary>
    private static void WriteAtomically(string target, string json)
    {
        string directory = Path.GetDirectoryName(target) ?? ".";
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
        try
        {
            byte[] bytes = Utf8.GetBytes(json);
            using (
                var stream = new FileStream(
                    temp,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.WriteThrough
                )
            )
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static int LoadFailure(TextWriter errors, string source, string detail)
    {
        errors.WriteLine(
            $"lunate: could not load the model catalog from {source}: {detail}. The existing catalog is unchanged."
        );
        return 1;
    }

    private static string Detail(Exception exception)
    {
        string message = exception.Message.Trim().TrimEnd('.');
        return message.Length == 0 ? exception.GetType().Name : message;
    }

    private sealed class SourceResponseException(string message) : Exception(message);
}
