using HarmonIQ.Api.Services.Orientation;

namespace HarmonIQ.Tests;

public class OrientationResolutionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static UnitPlacement U(double? facing) => new("u", null, null, facing);

    [Fact]
    public void AtExactly80Percent_Resolves()
    {
        // 4/5 = 80% north, 1/5 south.
        var placements = new[] { U(0), U(10), U(20), U(350), U(180) };
        var result = OrientationResolution.Resolve("plan", placements, Now);

        Assert.NotNull(result);
        Assert.Equal("sightmap", result!.Source);
        Assert.Equal("north", result.Cardinal);
        Assert.Equal(0.8, result.Confidence);
        Assert.NotNull(result.FacingDegrees);
    }

    [Fact]
    public void JustBelow80Percent_ResolvesToNone()
    {
        // 3/4 = 75% north, 1/4 south — below the 80% threshold.
        var placements = new[] { U(0), U(10), U(20), U(180) };
        var result = OrientationResolution.Resolve("plan", placements, Now);

        Assert.NotNull(result);
        Assert.Equal("none", result!.Source);
        Assert.Null(result.FacingDegrees);
        Assert.Null(result.Cardinal);
        Assert.Equal(0.75, result.Confidence);
    }

    [Fact]
    public void UnplacedUnits_ExcludedFromDenominator()
    {
        // 3 units with no facing (excluded) + 4 faced units, all north => 100% of the faced ones.
        var placements = new[] { U(null), U(null), U(null), U(0), U(5), U(10), U(15) };
        var result = OrientationResolution.Resolve("plan", placements, Now);

        Assert.NotNull(result);
        Assert.Equal("sightmap", result!.Source);
        Assert.Equal(1.0, result.Confidence);
    }

    [Fact]
    public void EvenSplit_ResolvesToNone()
    {
        var placements = new[] { U(0), U(10), U(180), U(190) };
        var result = OrientationResolution.Resolve("plan", placements, Now);

        Assert.NotNull(result);
        Assert.Equal("none", result!.Source);
        Assert.Equal(0.5, result.Confidence);
    }

    [Fact]
    public void EmptyPlacements_ReturnsNull()
    {
        var result = OrientationResolution.Resolve("plan", [], Now);
        Assert.Null(result);
    }

    [Fact]
    public void AllUnitsUnplaced_ReturnsNull()
    {
        var placements = new[] { U(null), U(null) };
        var result = OrientationResolution.Resolve("plan", placements, Now);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(0, "north")]
    [InlineData(44, "north")]
    [InlineData(315, "north")]
    [InlineData(45, "east")]
    [InlineData(134, "east")]
    [InlineData(135, "south")]
    [InlineData(224, "south")]
    [InlineData(225, "west")]
    [InlineData(314, "west")]
    public void CardinalOf_BucketsCorrectly(double degrees, string expected) =>
        Assert.Equal(expected, OrientationResolution.CardinalOf(degrees));

    [Fact]
    public void ConfidenceEqualsConcentrationRatio()
    {
        // 6/8 = 75% in one sector.
        var placements = new[] { U(0), U(5), U(10), U(15), U(20), U(25), U(180), U(190) };
        var result = OrientationResolution.Resolve("plan", placements, Now);

        Assert.NotNull(result);
        Assert.Equal(6.0 / 8.0, result!.Confidence);
    }
}

public class FixtureOrientationProviderTests
{
    private static string FixturePath() => Path.Combine(
        AppContext.BaseDirectory, "Data", "sample-orientation.json");

    [Fact]
    public async Task ClearMajorityPlan_Resolves()
    {
        var provider = new FixtureOrientationProvider(FixturePath());
        var result = await provider.ResolveAsync("sample-multiplan", "plan-a", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("sightmap", result!.Source);
        Assert.Equal("north", result.Cardinal);
    }

    [Fact]
    public async Task SplitPlan_ResolvesToNone()
    {
        var provider = new FixtureOrientationProvider(FixturePath());
        var result = await provider.ResolveAsync("sample-multiplan", "plan-b", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("none", result!.Source);
    }

    [Fact]
    public async Task NoPlacementsPlan_ReturnsNull()
    {
        var provider = new FixtureOrientationProvider(FixturePath());
        var result = await provider.ResolveAsync("sample-multiplan", "plan-c", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UncoveredProperty_ReturnsNull()
    {
        var provider = new FixtureOrientationProvider(FixturePath());
        var result = await provider.ResolveAsync("does-not-exist", "plan-a", CancellationToken.None);

        Assert.Null(result);
    }
}
