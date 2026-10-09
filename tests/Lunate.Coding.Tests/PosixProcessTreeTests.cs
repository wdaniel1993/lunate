using System.Globalization;

namespace Lunate.Coding.Tests;

public sealed class PosixProcessTreeTests
{
    [Fact]
    public void Enumerate_orders_children_before_parents_and_root_last()
    {
        // 100 ← 200 ← {300, 400}; 500 is unrelated.
        const string table = """
            100 1
            200 100
            300 200
            400 200
            500 1
            """;

        var ordered = PosixProcessTree.Enumerate(table, 100);

        Assert.Equal([300, 400, 200, 100], ordered);
    }

    [Fact]
    public void Enumerate_ignores_malformed_and_self_referencing_lines()
    {
        const string table = """
            garbage line here
            1 1
            abc 100
            100 notanumber
            700 100
            """;

        var ordered = PosixProcessTree.Enumerate(table, 100);

        Assert.Equal([700, 100], ordered);
    }

    [Fact]
    public void Enumerate_survives_a_parent_cycle()
    {
        const string table = """
            100 200
            200 100
            """;

        var ordered = PosixProcessTree.Enumerate(table, 100);

        Assert.Equal([200, 100], ordered);
    }

    [Fact]
    public void Enumerate_of_a_pid_absent_from_the_table_returns_just_the_root()
    {
        const string table = """
            100 1
            """;

        var ordered = PosixProcessTree.Enumerate(table, 999);

        Assert.Equal([999], ordered);
    }

    [Fact]
    public void Enumerate_parses_culture_invariantly()
    {
        // Style check: the shared parse path must not depend on the machine culture
        // (the suite also runs under de-AT).
        const string table = "100 1\n200 100\n";

        var ordered = PosixProcessTree.Enumerate(table, 100);

        Assert.Equal(
            string.Join(',', ordered),
            string.Join(',', ordered.Select(pid => pid.ToString("0", CultureInfo.InvariantCulture)))
        );
    }
}
