using System.Collections.Concurrent;

namespace Lunate.Coding.Tests;

/// <summary>
/// A read that lands while a save replaces the file must return the old bytes or the new ones: never
/// throw, never see a half-written document, never fall back to defaults — and the save itself must
/// not fail because a read is in flight. Windows is the leg that enforces this: a rename over a file
/// that a handle holds fails with access-denied (delete sharing included) until the handle closes,
/// and a replace in place instead takes the name away, so the store serializes its reads and writes
/// with a gate rather than retrying around them. Unix sharing is advisory, so this passes there
/// before and after the gate; the Windows leg is the one that bites.
/// </summary>
public sealed class SettingsStoreReadRaceTests
{
    private const int Writes = 200;

    private static readonly string[] One = ["gpt-4o-mini"];
    private static readonly string[] Two = ["gpt-4o-mini", "claude-sonnet-5-5"];

    [Fact]
    public async Task Reads_racing_repeated_saves_never_throw_and_never_see_a_partial_document()
    {
        using var temp = new TempDirectory();
        string path = temp.File("settings.json");
        SettingsStore.WriteFavorites(One, path);

        var failures = new ConcurrentQueue<string>();
        var observed = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        using var firstRead = new ManualResetEventSlim();
        int stop = 0;
        long reads = 0;

        Task reader = Task.Run(
            () =>
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    try
                    {
                        AgentSettings settings = SettingsStore.Resolve(path, _ => null);
                        string favorites = string.Join(", ", settings.Favorites);
                        observed[favorites.Length == 0 ? "<defaults>" : favorites] = 0;
                        Interlocked.Increment(ref reads);
                        firstRead.Set();
                    }
                    catch (Exception exception)
                    {
                        failures.Enqueue($"{exception.GetType().Name}: {exception.Message}");
                    }
                }
            },
            TestContext.Current.CancellationToken
        );

        // The first read must land before the first save, so at least one read overlaps the writes
        // instead of every read racing a finished file.
        Assert.True(
            firstRead.Wait(WaitBudgets.InProcess, TestContext.Current.CancellationToken),
            "the reader never completed a read"
        );

        try
        {
            for (var i = 0; i < Writes; i++)
            {
                SettingsStore.WriteFavorites(i % 2 == 0 ? Two : One, path);

                // Give the reader a turn after every write. Without it the whole loop can finish
                // inside one thread quantum, so the test could pass without a single read overlapping
                // a save — which is the only thing it exists to prove.
                Thread.Sleep(0);
            }
        }
        finally
        {
            Volatile.Write(ref stop, 1);
        }

        await reader;

        Assert.True(Interlocked.Read(ref reads) > 0, "the reader never completed a read");
        Assert.Empty(failures);
        Assert.Equal(
            [string.Join(", ", One), string.Join(", ", Two)],
            observed.Keys.Order(StringComparer.Ordinal)
        );

        // The last save, once the reader is gone, leaves exactly one complete document behind.
        SettingsStore.WriteFavorites(Two, path);
        Assert.Equal(Two, SettingsStore.Resolve(path, _ => null).Favorites);
        Assert.Empty(Directory.GetFiles(temp.Root, "*.tmp"));
    }
}
