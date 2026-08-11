using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class ScoreMathTests
{
    private static RoomAnalysis Room(int score, ElementBalance? el = null) =>
        new("p1", "Bedroom", score, el ?? new ElementBalance(20, 20, 20, 20, 20), [], [], []);
    private static SiteAnalysis Site(int score) => new(score, [], [], []);

    [Theory]
    [InlineData(95, "A+")] [InlineData(94, "A")] [InlineData(85, "A-")]
    [InlineData(80, "B+")] [InlineData(75, "B")] [InlineData(70, "B-")]
    [InlineData(65, "C+")] [InlineData(60, "C")] [InlineData(55, "C-")]
    [InlineData(50, "D+")] [InlineData(45, "D")] [InlineData(40, "D-")] [InlineData(39, "F")]
    public void GradeBands(int score, string grade) => Assert.Equal(grade, ScoreMath.Grade(score));

    [Fact]
    public void Overall_Weights70_30_ThenAdjusts()
    {
        // rooms avg 80, site 60 → 0.7*80 + 0.3*60 = 74; adj -2 → 72
        var overall = ScoreMath.Overall([Room(70), Room(90)], Site(60), -2);
        Assert.Equal(72, overall);
    }

    [Fact]
    public void Overall_NoRooms_UsesSiteScore()
    {
        Assert.Equal(63, ScoreMath.Overall([], Site(60), 3));
    }

    [Fact]
    public void Overall_ClampsTo0_100()
    {
        Assert.Equal(100, ScoreMath.Overall([Room(100)], Site(100), 3));
    }

    [Fact]
    public void AverageElements_MeansEachElement()
    {
        var avg = ScoreMath.AverageElements(
            [Room(50, new ElementBalance(10, 0, 30, 0, 0)), Room(50, new ElementBalance(30, 20, 50, 0, 10))]);
        Assert.Equal(new ElementBalance(20, 10, 40, 0, 5), avg);
    }

    [Fact]
    public void LocalSummary_NamesStrongestAssetAndTopFix()
    {
        var rooms = new List<RoomAnalysis> {
            new("p1", "Bedroom", 62, new ElementBalance(20,20,20,20,20),
                [new Finding("Natural Light", "Large window floods the room", "fengshui")],
                [new ViolationFinding("Mirror Facing Bed", "Wardrobe mirror faces the bed", "major", "fengshui")],
                [new Suggestion("Reposition the mirror", "Angle it away from the bed", "low", "high")]),
        };
        var s = ScoreMath.LocalSummary(rooms, Site(70), new NumerologyResult(0, []));
        Assert.Contains("Bedroom", s);
        Assert.Contains("Reposition the mirror", s);
    }
}
