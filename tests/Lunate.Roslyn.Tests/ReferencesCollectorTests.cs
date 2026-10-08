using System.Globalization;
using Lunate.Roslyn;

namespace Lunate.Roslyn.Tests;

public sealed class ReferencesCollectorTests
{
    [Fact]
    public void Duplicate_locations_collapse_to_one()
    {
        var bounded = ReferencesCollector.Build([
            new ReferenceLocation("src/A.cs", 1, 1),
            new ReferenceLocation("src/A.cs", 1, 1),
            new ReferenceLocation("src/A.cs", 2, 1),
        ]);

        Assert.Equal(2, bounded.Total);
        Assert.Equal(
            [new ReferenceLocation("src/A.cs", 1, 1), new ReferenceLocation("src/A.cs", 2, 1)],
            bounded.Items
        );
        Assert.False(bounded.Truncated);
    }

    [Fact]
    public void Locations_are_ordered_by_file_line_and_column()
    {
        var bounded = ReferencesCollector.Build([
            new ReferenceLocation("src/B.cs", 1, 1),
            new ReferenceLocation("src/A.cs", 9, 3),
            new ReferenceLocation("src/A.cs", 2, 8),
            new ReferenceLocation("src/A.cs", 2, 4),
        ]);

        Assert.Equal(
            [
                new ReferenceLocation("src/A.cs", 2, 4),
                new ReferenceLocation("src/A.cs", 2, 8),
                new ReferenceLocation("src/A.cs", 9, 3),
                new ReferenceLocation("src/B.cs", 1, 1),
            ],
            bounded.Items
        );
    }

    [Fact]
    public void The_cap_keeps_two_hundred_and_counts_the_rest()
    {
        var locations = Enumerable
            .Range(0, 250)
            .Select(index => new ReferenceLocation(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"src/File{index.ToString(CultureInfo.InvariantCulture)}.cs"
                ),
                index + 1,
                1
            ))
            .ToArray();

        var bounded = ReferencesCollector.Build(locations);

        Assert.Equal(200, bounded.Items.Count);
        Assert.Equal(250, bounded.Total);
        Assert.True(bounded.Truncated);
    }
}
