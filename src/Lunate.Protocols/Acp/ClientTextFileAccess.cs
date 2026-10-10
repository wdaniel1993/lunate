using System.Globalization;
using Acp;
using Acp.JsonRpc;
using Acp.Schema;
using Lunate.Agent;

namespace Lunate.Protocols.Acp;

/// <summary>
/// <see cref="ITextFileAccess"/> over the client's <c>fs/read_text_file</c> and
/// <c>fs/write_text_file</c>; built only when the client advertised both at initialize. Byte-order
/// marks are the client's concern: reads report none and writes carry only the text. The ACP read
/// is line-addressed, so a bounded byte probe cannot be expressed; the client owns the file and
/// <see cref="ReadPrefix"/> always reports null. Every round trip is bounded (default 30 s): a
/// request that outlives its timeout fails as an I/O error. A client error with code -32002 maps
/// to not-found and any other non-zero code to an I/O error — the code alone decides; only an
/// error without a code falls back to the message, so <see cref="Exists"/> never masks one
/// failure as another.
/// </summary>
internal sealed class ClientTextFileAccess(
    AgentSideConnection connection,
    SessionId sessionId,
    TimeSpan? timeout = null
) : ITextFileAccess
{
    internal const int DefaultTimeoutSeconds = 30;

    private const int ResourceNotFoundCode = -32002;

    private const int NoCode = 0;

    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(DefaultTimeoutSeconds);

    public bool Exists(string path)
    {
        try
        {
            Read(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    public string ReadAllText(string path) => Read(path);

    public (string Text, bool HasBom) ReadRaw(string path) => (Read(path), false);

    public (string Text, long Length)? ReadPrefix(string path, int maxBytes) => null;

    public void WriteAllText(string path, string content) => Write(path, content);

    public void WriteRaw(string path, string text, bool hasBom) => Write(path, text);

    private string Read(string path) =>
        Await(
            connection.ReadTextFileAsync(
                new ReadTextFileRequest { SessionId = sessionId, Path = path },
                CancellationToken.None
            )
        ).Content;

    private void Write(string path, string content) =>
        Await(
            connection.WriteTextFileAsync(
                new WriteTextFileRequest
                {
                    SessionId = sessionId,
                    Path = path,
                    Content = content,
                },
                CancellationToken.None
            )
        );

    private T Await<T>(Task<T> request)
    {
        try
        {
            return request.WaitAsync(_timeout).GetAwaiter().GetResult();
        }
        catch (TimeoutException exception)
        {
            throw new IOException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"the editor did not answer the file request within {_timeout.TotalSeconds}s"
                ),
                exception
            );
        }
        catch (RequestErrorException exception)
        {
            throw IsMissingFile(exception)
                ? new FileNotFoundException(exception.Message, exception)
                : new IOException(exception.Message, exception);
        }
    }

    private static bool IsMissingFile(RequestErrorException exception) =>
        exception.Code switch
        {
            ResourceNotFoundCode => true,
            NoCode => HasMissingFileMessage(exception.Message),
            _ => false,
        };

    private static bool HasMissingFileMessage(string message) =>
        message.Contains("not found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("ENOENT", StringComparison.OrdinalIgnoreCase)
        || message.Contains("no such file", StringComparison.OrdinalIgnoreCase);
}
