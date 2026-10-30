using System.Security.Cryptography;
using System.Text;

namespace Lunate.Agent;

/// <summary>Tracks the newest text read of each canonical path for one harness history.</summary>
internal sealed class ReadLedger : IReadLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ReadVersion> _live = new(PathComparer);
    private readonly Dictionary<string, string> _invalidated = new(PathComparer);
    private readonly Dictionary<string, string> _staleReadNotices = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> StaleReadNotices
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, string>(_staleReadNotices, StringComparer.Ordinal);
            }
        }
    }

    public void Record(string path, string text, string callId, string displayPath)
    {
        string canonical = Canonical(path);
        string digest = Digest(text);
        lock (_gate)
        {
            if (_live.Remove(canonical, out ReadVersion? previous))
            {
                _staleReadNotices[previous.CallId] = SupersededNotice(previous.DisplayPath);
            }

            _invalidated.Remove(canonical);
            _live[canonical] = new ReadVersion(digest, callId, displayPath);
        }
    }

    public void Seed(string path, string callId, string displayPath)
    {
        string canonical = Canonical(path);
        lock (_gate)
        {
            if (_live.Remove(canonical, out ReadVersion? previous))
            {
                _staleReadNotices[previous.CallId] = SupersededNotice(previous.DisplayPath);
            }

            _live[canonical] = new ReadVersion(null, callId, displayPath);
        }
    }

    public string? Check(string path, string text, string displayPath)
    {
        string canonical = Canonical(path);
        string digest = Digest(text);
        lock (_gate)
        {
            if (_invalidated.TryGetValue(canonical, out string? invalidatedPath))
            {
                return Refusal(invalidatedPath);
            }

            if (!_live.TryGetValue(canonical, out ReadVersion? version))
            {
                return null;
            }

            if (version.Digest is not null && string.Equals(version.Digest, digest, StringComparison.Ordinal))
            {
                return null;
            }

            Invalidate(canonical, version);
            return Refusal(displayPath);
        }
    }

    public void Invalidate(string path)
    {
        string canonical = Canonical(path);
        lock (_gate)
        {
            if (_live.TryGetValue(canonical, out ReadVersion? version))
            {
                Invalidate(canonical, version);
            }
        }
    }

    private void Invalidate(string canonical, ReadVersion version)
    {
        _live.Remove(canonical);
        _invalidated[canonical] = version.DisplayPath;
        _staleReadNotices[version.CallId] = ChangedNotice(version.DisplayPath);
    }

    private static string Canonical(string path) => GitIdentity.Canonicalize(path);

    private static string Digest(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));

    private static string Refusal(string path) =>
        $"{path} changed after your last read (an edit, bash, or another process). Re-read it and base old_text on the current text.";

    private static string SupersededNotice(string path) =>
        $"[stale: superseded by a later read of {path}; this copy is no longer current]";

    private static string ChangedNotice(string path) =>
        $"[stale: {path} changed after this read; this copy is no longer current]";

    private static StringComparer PathComparer => OperatingSystem.IsLinux()
        ? StringComparer.Ordinal
        : StringComparer.OrdinalIgnoreCase;

    private sealed record ReadVersion(string? Digest, string CallId, string DisplayPath);
}
