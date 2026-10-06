using Microsoft.Extensions.Time.Testing;
using Microsoft.Reactive.Testing;
using S5.Harness;
using S5.VariantA;
using S5.VariantB;
using S5.VariantBPlus;

namespace S5.Tests;

public interface IVariantFixture
{
    string Name { get; }

    ISession CreateSession(int width = Scenario.InitialWidth, int height = Scenario.InitialHeight);
}

public sealed class VariantAFixture : IVariantFixture
{
    public string Name => "A (plain async)";

    public ISession CreateSession(
        int width = Scenario.InitialWidth,
        int height = Scenario.InitialHeight
    )
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        return new VariantASession(time, new FakeTerminal(width, height));
    }
}

public sealed class VariantBFixture : IVariantFixture
{
    public string Name => "B (System.Reactive)";

    public ISession CreateSession(
        int width = Scenario.InitialWidth,
        int height = Scenario.InitialHeight
    )
    {
        var scheduler = new TestScheduler();
        return new VariantBSession(scheduler, new FakeTerminal(width, height));
    }
}

public sealed class VariantBPlusFixture : IVariantFixture
{
    public string Name => "B+ (ReactiveUI view models)";

    public ISession CreateSession(
        int width = Scenario.InitialWidth,
        int height = Scenario.InitialHeight
    )
    {
        var scheduler = new TestScheduler();
        return new VariantBPlusSession(scheduler, new FakeTerminal(width, height));
    }
}
