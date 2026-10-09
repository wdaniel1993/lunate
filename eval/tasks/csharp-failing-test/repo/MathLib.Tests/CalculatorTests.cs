using MathLib;

namespace MathLib.Tests;

public sealed class CalculatorTests
{
    [Fact]
    public void Add_adds_both_numbers()
    {
        Assert.Equal(7, Calculator.Add(3, 4));
    }

    [Fact]
    public void Multiply_multiplies_both_numbers()
    {
        Assert.Equal(12, Calculator.Multiply(3, 4));
    }
}
