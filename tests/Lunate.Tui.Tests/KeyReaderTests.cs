using System.Text;
using Microsoft.Reactive.Testing;

namespace Lunate.Tui.Tests;

public sealed class KeyReaderTests
{
    [Fact]
    public void Lone_escape_resolves_after_the_quiet_window()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed([0x1B]);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(74).Ticks);
        Assert.Empty(keys);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1).Ticks);
        AssertKeys(keys, new KeyEvent(KeyKind.Escape, null, false, false, false));
    }

    [Fact]
    public void A_sequence_after_escape_suppresses_the_escape()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed([0x1B]);
        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(50).Ticks);
        reader.Feed([(byte)'[', (byte)'A']);
        scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);

        AssertKeys(keys, new KeyEvent(KeyKind.Up, null, false, false, false));
    }

    [Fact]
    public void Partial_sequences_buffer_until_decodable()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        foreach (byte b in new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'5' })
        {
            reader.Feed([b]);
            Assert.Empty(keys);
        }

        reader.Feed([(byte)'C']);

        AssertKeys(keys, new KeyEvent(KeyKind.Right, null, true, false, false));
    }

    [Fact]
    public void Utf8_split_across_chunks_decodes_once_complete()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed([0xC3]);
        Assert.Empty(keys);

        reader.Feed([0xA4]);

        AssertKeys(keys, new KeyEvent(KeyKind.Character, "ä", false, false, false));
    }

    [Fact]
    public void Alt_utf8_split_across_chunks_decodes_once_complete()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed([0x1B, 0xC3]);
        Assert.Empty(keys);

        reader.Feed([0xA4]);

        AssertKeys(keys, new KeyEvent(KeyKind.Character, "ä", false, false, true));
    }

    [Fact]
    public void Bracketed_paste_is_one_event_with_normalized_newlines()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed(Ascii("\u001b[200~hello\r\nworld\rline\u001b[201~"));

        AssertKeys(keys, new KeyEvent(KeyKind.Paste, "hello\nworld\nline", false, false, false));
    }

    [Fact]
    public void Paste_keeps_key_lookalikes_as_text()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed(Ascii("\u001b[200~a\u001b[Ab\u001b[201~"));

        AssertKeys(keys, new KeyEvent(KeyKind.Paste, "a\u001b[Ab", false, false, false));
    }

    [Fact]
    public void Paste_spanning_chunks_is_captured_whole()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed(Ascii("\u001b[200~first"));
        Assert.Empty(keys);

        reader.Feed(Ascii("\nsecond\u001b[20"));
        Assert.Empty(keys);

        reader.Feed(Ascii("1~"));

        AssertKeys(keys, new KeyEvent(KeyKind.Paste, "first\nsecond", false, false, false));
    }

    [Fact]
    public void A_key_after_paste_decodes_next()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed(Ascii("\u001b[200~x\u001b[201~y"));

        AssertKeys(
            keys,
            new KeyEvent(KeyKind.Paste, "x", false, false, false),
            new KeyEvent(KeyKind.Character, "y", false, false, false)
        );
    }

    [Fact]
    public void Unknown_sequences_resync_to_the_next_key()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed(Ascii("\u001b[99~a"));

        AssertKeys(
            keys,
            new KeyEvent(KeyKind.Unknown, null, false, false, false),
            new KeyEvent(KeyKind.Character, "a", false, false, false)
        );
    }

    [Fact]
    public void A_partial_csi_waits_without_emitting_an_escape()
    {
        var scheduler = new TestScheduler();
        var keys = new List<KeyEvent>();
        using var reader = new KeyReader(scheduler, keys.Add);

        reader.Feed([0x1B, (byte)'[']);
        scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);

        Assert.Empty(keys);
    }

    [Fact]
    public void Fake_console_routes_scripted_bytes_through_the_reader()
    {
        var scheduler = new TestScheduler();
        using var console = new FakeConsoleIO(scheduler);

        console.FeedBytes([0x1B, 0x5B, 0x41]);
        console.FeedBytes(Ascii("h"));
        console.FeedBytes(Ascii("\u001b[200~x\r\ny\u001b[201~"));

        AssertKeys(
            console.Keys,
            new KeyEvent(KeyKind.Up, null, false, false, false),
            new KeyEvent(KeyKind.Character, "h", false, false, false),
            new KeyEvent(KeyKind.Paste, "x\ny", false, false, false)
        );
    }

    private static void AssertKeys(IReadOnlyList<KeyEvent> actual, params KeyEvent[] expected) =>
        Assert.Equal(expected, actual);

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
}
