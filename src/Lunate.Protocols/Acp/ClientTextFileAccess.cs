using Acp;
using Acp.Schema;
using Lunate.Agent;

namespace Lunate.Protocols.Acp;

/// <summary>
/// <see cref="ITextFileAccess"/> over the client's <c>fs/read_text_file</c> and
/// <c>fs/write_text_file</c>; built only when the client advertised both at initialize. Byte-order
/// marks are the client's concern: reads report none and writes carry only the text.
/// </summary>
internal sealed class ClientTextFileAccess(AgentSideConnection connection, SessionId sessionId)
    : ITextFileAccess
{
    public bool Exists(string path)
    {
        try
        {
            Read(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string ReadAllText(string path) => Read(path);

    public (string Text, bool HasBom) ReadRaw(string path) => (Read(path), false);

    public void WriteAllText(string path, string content) => Write(path, content);

    public void WriteRaw(string path, string text, bool hasBom) => Write(path, text);

    private string Read(string path) =>
        connection
            .ReadTextFileAsync(
                new ReadTextFileRequest { SessionId = sessionId, Path = path },
                CancellationToken.None
            )
            .GetAwaiter()
            .GetResult()
            .Content;

    private void Write(string path, string content) =>
        connection
            .WriteTextFileAsync(
                new WriteTextFileRequest
                {
                    SessionId = sessionId,
                    Path = path,
                    Content = content,
                },
                CancellationToken.None
            )
            .GetAwaiter()
            .GetResult();
}
