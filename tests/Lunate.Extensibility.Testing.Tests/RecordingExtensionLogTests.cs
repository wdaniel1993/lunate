namespace Lunate.Extensibility.Testing.Tests;

public sealed class RecordingExtensionLogTests
{
    [Fact]
    public void Entries_preserve_levels_and_emission_order()
    {
        var log = new RecordingExtensionLog();

        log.Info("first");
        log.Warn("second");
        log.Error("third");

        Assert.Equal(
            [
                new ExtensionLogEntry(ExtensionLogLevel.Info, "first"),
                new ExtensionLogEntry(ExtensionLogLevel.Warn, "second"),
                new ExtensionLogEntry(ExtensionLogLevel.Error, "third"),
            ],
            log.Entries
        );
        Assert.Equal(["first", "second", "third"], log.Messages);
    }

    [Fact]
    public void Entries_can_be_selected_by_level_and_text()
    {
        var log = new RecordingExtensionLog();
        log.Info("template service started");
        log.Info("unrelated");
        log.Error("template service failed");

        Assert.Equal(2, log.Count(ExtensionLogLevel.Info));
        Assert.Equal(1, log.Count(ExtensionLogLevel.Error));
        Assert.Equal(1, log.Count(ExtensionLogLevel.Info, "template"));
        Assert.True(log.Contains("service started"));
    }
}
