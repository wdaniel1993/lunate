namespace Lunate.Agent;

/// <summary>The per-harness record of text versions served to tools.</summary>
internal interface IReadLedger
{
    /// <summary>Records a successful read of the canonical path.</summary>
    void Record(string path, string text, string callId, string displayPath);

    /// <summary>Seeds a read from history when its original file version is no longer known.</summary>
    void Seed(string path, string callId, string displayPath);

    /// <summary>Returns an instructing refusal when the current file is not the live read version.</summary>
    string? Check(string path, string text, string displayPath);

    /// <summary>Invalidates a live read after a published file change.</summary>
    void Invalidate(string path);

    /// <summary>Call ids of read results that should be replaced in the in-memory request history.</summary>
    IReadOnlyDictionary<string, string> StaleReadNotices { get; }
}
