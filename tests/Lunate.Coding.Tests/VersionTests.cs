using System.Reflection;

namespace Lunate.Coding.Tests;

public sealed class VersionTests
{
    [Fact]
    public void Run_with_version_flag_writes_product_version_and_returns_zero()
    {
        using var writer = new StringWriter();
        var informationalVersion = typeof(Cli)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        var exitCode = Cli.Run(
            ["--version"],
            writer,
            TextWriter.Null,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(0, exitCode);
        Assert.NotNull(informationalVersion);
        Assert.Equal(informationalVersion + writer.NewLine, writer.ToString());
    }
}
