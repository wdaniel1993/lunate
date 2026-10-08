namespace CalculatorLib;

public static class Calculator
{
    public static int Add(int left, int right) => left + right;

    public static int Divide(int left, int right) =>
        right == 0 ? throw new DivideByZeroException() : left / right;
}
