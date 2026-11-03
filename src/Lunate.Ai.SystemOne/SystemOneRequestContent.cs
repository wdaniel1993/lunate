using System.IO.Compression;

namespace Lunate.Ai.SystemOne;

internal static class SystemOneRequestContent
{
    public static byte[] Gzip(byte[] body)
    {
        using MemoryStream compressed = new();
        using (GZipStream gzip = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(body);
        }
        return compressed.ToArray();
    }
}
