namespace Lunate.Agent;

/// <summary>
/// The text-file seam the read, write and edit tools use. The tools default to the local-disk
/// implementation; ACP mode routes it through the editor's file system so unsaved buffers are
/// respected. The local implementation preserves byte-order marks; a client-backed implementation
/// leaves that to the client (the ACP change documents the pin).
/// </summary>
public interface ITextFileAccess
{
    /// <summary>Whether a text file exists at <paramref name="path"/>.</summary>
    bool Exists(string path);

    /// <summary>Reads the whole file as UTF-8 text, without a byte-order mark.</summary>
    string ReadAllText(string path);

    /// <summary>Reads the whole file as UTF-8 text and reports whether it carried a byte-order mark.</summary>
    (string Text, bool HasBom) ReadRaw(string path);

    /// <summary>Writes <paramref name="content"/> as UTF-8 without a byte-order mark, creating parent directories.</summary>
    void WriteAllText(string path, string content);

    /// <summary>Writes <paramref name="text"/>, preserving whether the file carried a byte-order mark.</summary>
    void WriteRaw(string path, string text, bool hasBom);
}
