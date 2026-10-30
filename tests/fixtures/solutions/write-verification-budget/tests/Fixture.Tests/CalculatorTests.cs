using Fixture.Lib;

namespace Fixture.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_returns_sum() => Assert.Equal(3, Calculator.Add(1, 2));

    [Fact]
    public void Divide_by_zero_throws() =>
        Assert.Throws<DivideByZeroException>(() => Calculator.Divide(1, 0));
}
