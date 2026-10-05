using Microsoft.Extensions.Time.Testing;
using S5.Harness;

var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
return SpikeProgram.Run(
    args,
    () =>
        new S5.VariantA.VariantASession(
            time,
            new FakeTerminal(Scenario.InitialWidth, Scenario.InitialHeight)
        )
);
