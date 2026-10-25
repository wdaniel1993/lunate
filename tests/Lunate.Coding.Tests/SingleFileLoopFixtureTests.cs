using System.Collections.Generic;
using Xunit;

namespace Lunate.Coding.Tests;

/// <summary>
/// The fixture seeds a reference pack so the compile is real and offline. It must choose the same
/// pack the product's rule chooses, and it must say "none" as null — CI ran a runtime ahead of the
/// feed, the old value-tuple FirstOrDefault answered (null, null), and the fixture linked to a null
/// target instead of skipping with a reason.
/// </summary>
public sealed class SingleFileLoopFixtureTests
{
    private static readonly List<(string Parent, string Version)> Packs =
    [
        ("/packs/10.0.4", "10.0.4"),
        ("/packs/10.0.8", "10.0.8"),
        ("/packs/10.1.0", "10.1.0"),
    ];

    [Fact]
    public void The_exact_runtime_pack_wins()
    {
        Assert.Equal(
            ("/packs/10.0.8", "10.0.8"),
            SingleFileLoopFixture.ChoosePack(Packs, new(10, 0, 8))
        );
    }

    [Fact]
    public void The_lowest_pack_at_or_above_the_runtime_is_next()
    {
        Assert.Equal(
            ("/packs/10.0.8", "10.0.8"),
            SingleFileLoopFixture.ChoosePack(Packs, new(10, 0, 5))
        );
    }

    [Fact]
    public void A_runtime_ahead_of_every_pack_falls_back_to_the_highest_below_it()
    {
        Assert.Equal(
            ("/packs/10.0.8", "10.0.8"),
            SingleFileLoopFixture.ChoosePack(Packs, new(10, 0, 14))
        );
    }

    [Fact]
    public void Another_minor_is_never_chosen()
    {
        Assert.Equal(
            ("/packs/10.0.8", "10.0.8"),
            SingleFileLoopFixture.ChoosePack(Packs, new(10, 0, 14))
        );
        Assert.Null(
            SingleFileLoopFixture.ChoosePack([("/packs/10.1.0", "10.1.0")], new(10, 0, 14))
        );
    }

    [Fact]
    public void An_empty_list_is_none_rather_than_a_pair_of_nulls()
    {
        Assert.Null(SingleFileLoopFixture.ChoosePack([], new(10, 0, 14)));
    }
}
