using S5.Harness;

return SpikeProgram.Run(args, () => new S5.Baseline.BaselineSession(new FakeTerminal(Scenario.InitialWidth, Scenario.InitialHeight)));
