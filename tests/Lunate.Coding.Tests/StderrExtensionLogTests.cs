using Lunate.Extensibility;

namespace Lunate.Coding.Tests;

public sealed class StderrExtensionLogTests
{
    [Fact]
    public void Warn_and_error_are_prefixed_on_the_writer_and_info_is_suppressed()
    {
        using var writer = new StringWriter();
        var log = new StderrExtensionLog(writer);

        log.Info("extension-internal detail");
        log.Warn("careful");
        log.Error("broken");

        string text = writer.ToString();
        Assert.DoesNotContain("extension-internal detail", text, StringComparison.Ordinal);
        Assert.Contains($"lunate: careful{Environment.NewLine}", text, StringComparison.Ordinal);
        Assert.Contains($"lunate: broken{Environment.NewLine}", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_are_serialized_so_parallel_contributors_cannot_interleave()
    {
        using var writer = new StringWriter();
        var log = new StderrExtensionLog(writer);

        Parallel.For(
            0,
            200,
            index =>
            {
                if (index % 2 == 0)
                {
                    log.Warn(
                        "w" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    );
                }
                else
                {
                    log.Error(
                        "e" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    );
                }
            }
        );

        string[] lines = writer
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(200, lines.Length);
        Assert.All(lines, line => Assert.StartsWith("lunate: ", line, StringComparison.Ordinal));
    }
}
