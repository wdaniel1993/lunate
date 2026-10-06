using Microsoft.Reactive.Testing;
using S5.Harness;

var scheduler = new TestScheduler();
return SpikeProgram.Run(
    args,
    () =>
        new S5.VariantB.VariantBSession(
            scheduler,
            new FakeTerminal(Scenario.InitialWidth, Scenario.InitialHeight)
        )
);
