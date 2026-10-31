using Lunate.Agent;

namespace Lunate.Coding.Tests;

public sealed class SettingsStoreVerifyTests
{
    [Fact]
    public void VerifyDefaultsToBlock()
    {
        using var temp = new TempDirectory();

        AgentSettings settings = SettingsStore.Resolve(temp.File("missing.json"), _ => null);

        Assert.Equal(WriteVerificationMode.Block, settings.Verify);
    }

    [Theory]
    [InlineData("warn", WriteVerificationMode.Warn)]
    [InlineData("off", WriteVerificationMode.Off)]
    public void VerifyAcceptsWarnAndOff(string value, WriteVerificationMode expected)
    {
        using var temp = new TempDirectory();
        File.WriteAllText(
            temp.File("settings.json"),
            $$"""{"schemaVersion":2,"verify":"{{value}}"}"""
        );

        AgentSettings settings = SettingsStore.Resolve(temp.File("settings.json"), _ => null);

        Assert.Equal(expected, settings.Verify);
    }

    [Fact]
    public void InvalidVerifySettingNamesAcceptedValues()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.File("settings.json"), """{"schemaVersion":2,"verify":"always"}""");

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            SettingsStore.Resolve(temp.File("settings.json"), _ => null)
        );

        Assert.Contains("'verify'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("block", exception.Message, StringComparison.Ordinal);
        Assert.Contains("warn", exception.Message, StringComparison.Ordinal);
        Assert.Contains("off", exception.Message, StringComparison.Ordinal);
    }
}
