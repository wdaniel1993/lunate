namespace ConsoleApp;

public class Geometry
{
    private readonly int _sides;

    public Geometry(int sides)
    {
        _sides = sides;
    }

    public enum Kind
    {
        Polygon,
        Circle,
    }

    public event EventHandler? Changed;

    public int Sides => _sides;

    public Kind Shape => _sides == 0 ? Kind.Circle : Kind.Polygon;

    public int Perimeter(int length) => length * _sides;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
