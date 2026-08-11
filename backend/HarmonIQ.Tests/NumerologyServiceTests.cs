using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class NumerologyServiceTests
{
    private readonly NumerologyService _svc = new();

    [Fact]
    public void Unit414_Fengshui_IsUnluckyWithReasonAndRemedy()
    {
        var r = _svc.Evaluate(new ListingNumbers("414", 4, "123"), "fengshui");
        var unit = r.Checks.Single(c => c.Subject == "unitNumber" && c.Tradition == "fengshui");
        Assert.Equal("unlucky", unit.Verdict);
        Assert.Contains("4", unit.Reason);
        Assert.NotNull(unit.Remedy);
    }

    [Fact]
    public void Unit88_Fengshui_IsLucky()
    {
        var r = _svc.Evaluate(new ListingNumbers("88", null, null), "fengshui");
        Assert.Equal("lucky", r.Checks.Single(c => c.Subject == "unitNumber").Verdict);
    }

    [Fact]
    public void Street123_Vastu_DigitSum6_IsLucky()
    {
        var r = _svc.Evaluate(new ListingNumbers(null, null, "123"), "vastu");
        var street = r.Checks.Single(c => c.Subject == "streetNumber" && c.Tradition == "vastu");
        Assert.Equal("lucky", street.Verdict);
        Assert.Contains("6", street.Reason);
    }

    [Fact]
    public void Floor13_TriggersWesternCheck_EvenUnderVastuFilter()
    {
        var r = _svc.Evaluate(new ListingNumbers(null, 13, null), "vastu");
        Assert.Contains(r.Checks, c => c.Subject == "floor" && c.Tradition == "western" && c.Verdict == "unlucky");
    }

    [Fact]
    public void SystemsVastu_ExcludesChineseChecks()
    {
        var r = _svc.Evaluate(new ListingNumbers("44", 4, "444"), "vastu");
        Assert.DoesNotContain(r.Checks, c => c.Tradition == "fengshui");
    }

    [Fact]
    public void AdjustmentIsClampedToMinusThree()
    {
        var r = _svc.Evaluate(new ListingNumbers("44", 4, "40"), "both"); // many unlucky hits
        Assert.Equal(-3, r.ScoreAdjustment);
    }

    [Fact]
    public void NullNumbers_YieldsNoChecksAndZeroAdjustment()
    {
        var r = _svc.Evaluate(null, "both");
        Assert.Empty(r.Checks);
        Assert.Equal(0, r.ScoreAdjustment);
    }
}
