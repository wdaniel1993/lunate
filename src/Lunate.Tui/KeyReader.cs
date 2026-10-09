using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using System.Text;

namespace Lunate.Tui;

internal sealed class KeyReader : IDisposable
{
    internal static readonly TimeSpan DefaultQuietWindow = TimeSpan.FromMilliseconds(75);

    private static readonly byte[] PasteStart = "\u001b[200~"u8.ToArray();
    private static readonly byte[] PasteEnd = "\u001b[201~"u8.ToArray();

    private readonly object _gate = new();
    private readonly IScheduler _scheduler;
    private readonly Action<KeyEvent> _onKey;
    private readonly TimeSpan _quietWindow;
    private readonly List<byte> _buffer = [];
    private IDisposable? _escapeTimer;
    private bool _inPaste;
    private bool _disposed;

    public KeyReader(IScheduler scheduler, Action<KeyEvent> onKey, TimeSpan? quietWindow = null)
    {
        _scheduler = scheduler;
        _onKey = onKey;
        _quietWindow = quietWindow ?? DefaultQuietWindow;
    }

    public void Feed(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _escapeTimer?.Dispose();
            _escapeTimer = null;
            foreach (byte b in bytes)
            {
                _buffer.Add(b);
            }

            Process();
            ArmEscapeTimer();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _escapeTimer?.Dispose();
            _escapeTimer = null;
        }
    }

    private void Process()
    {
        while (_buffer.Count > 0)
        {
            if (_inPaste)
            {
                int end = IndexOf(_buffer, PasteEnd);
                if (end < 0)
                {
                    return;
                }

                var text = NormalizeNewlines(
                    Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(_buffer)[..end])
                );
                _buffer.RemoveRange(0, end + PasteEnd.Length);
                _inPaste = false;
                _onKey(new KeyEvent(KeyKind.Paste, text, false, false, false));
                continue;
            }

            if (StartsWith(_buffer, PasteStart))
            {
                _buffer.RemoveRange(0, PasteStart.Length);
                _inPaste = true;
                continue;
            }

            if (_buffer.Count == 1 && _buffer[0] == 0x1B)
            {
                return;
            }

            if (
                !VtInputDecoder.TryDecode(
                    CollectionsMarshal.AsSpan(_buffer),
                    out var key,
                    out int consumed
                )
                || consumed == 0
            )
            {
                return;
            }

            _buffer.RemoveRange(0, consumed);
            _onKey(key!);
        }
    }

    private void ArmEscapeTimer()
    {
        if (_inPaste || _buffer.Count != 1 || _buffer[0] != 0x1B)
        {
            return;
        }

        _escapeTimer = _scheduler.Schedule(_quietWindow, EmitBufferedEscape);
    }

    private void EmitBufferedEscape()
    {
        lock (_gate)
        {
            _escapeTimer = null;
            if (_disposed || _inPaste || _buffer.Count != 1 || _buffer[0] != 0x1B)
            {
                return;
            }

            _buffer.Clear();
            _onKey(new KeyEvent(KeyKind.Escape, null, false, false, false));
        }
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool StartsWith(List<byte> buffer, ReadOnlySpan<byte> prefix) =>
        buffer.Count >= prefix.Length
        && CollectionsMarshal.AsSpan(buffer)[..prefix.Length].SequenceEqual(prefix);

    private static int IndexOf(List<byte> buffer, ReadOnlySpan<byte> marker) =>
        CollectionsMarshal.AsSpan(buffer).IndexOf(marker);
}
