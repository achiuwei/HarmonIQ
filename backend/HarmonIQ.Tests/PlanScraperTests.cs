using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class PlanScraperTests
{
    private static string FixtureHtml() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "multiplan-ldp.html"));

    [Fact]
    public void ExtractsFivePlans()
    {
        var plans = new PlanScraper().ExtractPlans(FixtureHtml());
        Assert.Equal(5, plans.Count);
    }

    [Fact]
    public void DuplicateModelNames_ProduceDistinctSubjectsByRentalKey()
    {
        var plans = new PlanScraper().ExtractPlans(FixtureHtml());
        var oneBedOneBath = plans.Where(p => p.ModelName == "1 Bed 1 Bath").ToList();

        Assert.Equal(2, oneBedOneBath.Count);
        Assert.NotEqual(oneBedOneBath[0].RentalKey, oneBedOneBath[1].RentalKey);
        Assert.Equal("rk-201", oneBedOneBath[0].RentalKey);
        Assert.Equal("rk-202", oneBedOneBath[1].RentalKey);
    }

    [Fact]
    public void HiddenUnitRows_BehindShowMore_AreCaptured()
    {
        var plans = new PlanScraper().ExtractPlans(FixtureHtml());
        var plan = plans.Single(p => p.RentalKey == "rk-201");

        Assert.Equal(3, plan.Units.Count);
        Assert.Contains(plan.Units, u => u.UnitNumber == "444");
    }

    [Fact]
    public void MissingImage_YieldsNullPlanImageUrl()
    {
        var plans = new PlanScraper().ExtractPlans(FixtureHtml());
        var studio = plans.Single(p => p.RentalKey == "rk-204");

        Assert.Null(studio.PlanImageUrl);
        Assert.Equal(0, studio.Beds); // "Studio" parses to 0 beds
    }

    [Fact]
    public void PresentImage_YieldsUrl()
    {
        var plans = new PlanScraper().ExtractPlans(FixtureHtml());
        var plan = plans.Single(p => p.RentalKey == "rk-201");

        Assert.Equal("/images/plans/rk-201.png", plan.PlanImageUrl);
        Assert.Equal("att-201", plan.AttachmentId);
    }

    [Theory]
    [InlineData("rk-201", 609, 650)]
    [InlineData("rk-202", 590, 611)]
    [InlineData("rk-205", 1091, 1204)] // comma-thousands + en dash: "1,091 – 1,204 Sq Ft"
    public void SqftRanges_Parse(string rentalKey, int expectedMin, int expectedMax)
    {
        var plans = new PlanScraper().ExtractPlans(FixtureHtml());
        var plan = plans.Single(p => p.RentalKey == rentalKey);

        Assert.Equal(expectedMin, plan.SqftMin);
        Assert.Equal(expectedMax, plan.SqftMax);
    }

    [Fact]
    public void BedsAndBaths_Parse()
    {
        var plans = new PlanScraper().ExtractPlans(FixtureHtml());
        var plan = plans.Single(p => p.RentalKey == "rk-203");

        Assert.Equal(2, plan.Beds);
        Assert.Equal(2.0, plan.Baths);
    }

    [Fact]
    public void UnitFields_ParseFloorSqftAndPrice()
    {
        var plans = new PlanScraper().ExtractPlans(FixtureHtml());
        var unit = plans.Single(p => p.RentalKey == "rk-201").Units.Single(u => u.UnitNumber == "101");

        Assert.Equal(1, unit.Floor);
        Assert.Equal(614, unit.Sqft);
        Assert.Equal(1800m, unit.Price);
    }
}
