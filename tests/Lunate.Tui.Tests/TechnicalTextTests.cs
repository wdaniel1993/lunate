using System.Globalization;

namespace Lunate.Tui.Tests;

public sealed class TechnicalTextTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1L, "1 B")]
    [InlineData(812L, "812 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(12595L, "12.3 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1610612736L, "1.5 GB")]
    [InlineData(1099511627776L, "1 TB")]
    public void Bytes_table(long bytes, string expected) =>
        Assert.Equal(expected, TechnicalText.Bytes(bytes));

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(999L, "999")]
    [InlineData(1000L, "1,000")]
    [InlineData(1540L, "1,540")]
    [InlineData(1234567L, "1,234,567")]
    public void Tokens_table(long tokens, string expected) =>
        Assert.Equal(expected, TechnicalText.Tokens(tokens));

    [Theory]
    [InlineData(0L, "0.0 s")]
    [InlineData(2400L, "2.4 s")]
    [InlineData(9200L, "9.2 s")]
    [InlineData(9924L, "9.9 s")]
    [InlineData(59900L, "59.9 s")]
    [InlineData(60000L, "1 m 00 s")]
    [InlineData(72000L, "1 m 12 s")]
    [InlineData(125000L, "2 m 05 s")]
    [InlineData(3599900L, "59 m 59 s")]
    [InlineData(3600000L, "1 h 00 m")]
    [InlineData(3630000L, "1 h 00 m")]
    [InlineData(3780000L, "1 h 03 m")]
    [InlineData(7320000L, "2 h 02 m")]
    public void Duration_table(long totalMilliseconds, string expected) =>
        Assert.Equal(
            expected,
            TechnicalText.Duration(TimeSpan.FromMilliseconds(totalMilliseconds))
        );

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(0.4, "0.4%")]
    [InlineData(12.5, "12.5%")]
    [InlineData(87.0, "87%")]
    [InlineData(100.0, "100%")]
    public void Percent_table(double percent, string expected) =>
        Assert.Equal(expected, TechnicalText.Percent(percent));

    [Fact]
    public void Helpers_are_invariant_under_de_AT()
    {
        using var culture = new CultureScope("de-AT");

        Assert.Equal("12.3 KB", TechnicalText.Bytes(12595));
        Assert.Equal("1,540", TechnicalText.Tokens(1540));
        Assert.Equal("1 m 12 s", TechnicalText.Duration(TimeSpan.FromSeconds(72)));
        Assert.Equal("12.5%", TechnicalText.Percent(12.5));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous;

        public CultureScope(string name)
        {
            _previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
