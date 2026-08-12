using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

/// <summary>
/// The mock LDP's markup, which this repo authors. These cases are deliberately NOT retargeted at
/// real apartments.com markup: the mock host is the always-verifiable demo surface, so its shape
/// has to keep parsing. Real-markup coverage lives in <see cref="ApartmentsPlanScraperTests"/>.
/// </summary>
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

/// <summary>
/// The same scraper against real apartments.com markup, captured from the Enzo LDP
/// (<c>Fixtures/apartments-ldp.html</c>). Every regex the mock cases exercise is a different one
/// here, because the two dialects agree on almost nothing structurally — nested identity, classless
/// beds/baths/sqft spans, <c>&lt;li&gt;</c> unit rows, and no floor number at all.
/// </summary>
public class ApartmentsPlanScraperTests
{
    private static string FixtureHtml() => File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "apartments-ldp.html"));

    private static IReadOnlyList<ScrapedPlan> Plans() => new PlanScraper().ExtractPlans(FixtureHtml());

    [Fact]
    public void RepeatedPlans_AcrossBedroomTabs_FoldToOnePerRentalKey()
    {
        // Sandpiper is rendered twice — once under "All", once under the studio tab. A real page
        // does this for every plan, so without folding the section would show each plan twice.
        var plans = Plans();

        Assert.Equal(2, plans.Count);
        Assert.Equal(new[] { "bmgrv28", "bncrtt8" }, plans.Select(p => p.RentalKey).Order());
    }

    [Fact]
    public void PlanIdentity_ComesFromTheModelWrapper_NotFromAUnitRow()
    {
        // Every unit row carries its own data-rentalkey. Reading the block's first one would
        // silently key the plan by one of its units.
        var willow = Plans().Single(p => p.ModelName == "Willow");

        Assert.Equal("bmgrv28", willow.RentalKey);
        Assert.DoesNotContain(willow.RentalKey, new[] { "rzpxqk1", "db09k5k", "e4e78pm" });
    }

    [Fact]
    public void HiddenUnitRows_BehindShowMoreUnits_AreCaptured()
    {
        // Three of Willow's six rows carry hideOnCollapsed and are invisible until the expander
        // is clicked. Raw markup sees them regardless.
        var willow = Plans().Single(p => p.RentalKey == "bmgrv28");

        Assert.Equal(6, willow.Units.Count);
        Assert.Contains(willow.Units, u => u.UnitNumber == "3166"); // behind the expander
    }

    [Fact]
    public void UnitRows_CarryNoFloor_BecauseTheRealPagePublishesNone()
    {
        var willow = Plans().Single(p => p.RentalKey == "bmgrv28");

        Assert.All(willow.Units, u => Assert.Null(u.Floor));
    }

    [Fact]
    public void UnitFields_ParseSqftAndPrice()
    {
        var unit = Plans().Single(p => p.RentalKey == "bmgrv28").Units.Single(u => u.UnitNumber == "3356");

        Assert.Equal(1020, unit.Sqft);
        Assert.Equal(4250m, unit.Price);
    }

    [Fact]
    public void SqftRange_WithCommasAndEnDash_Parses()
    {
        var willow = Plans().Single(p => p.RentalKey == "bmgrv28");

        Assert.Equal(1020, willow.SqftMin);
        Assert.Equal(1103, willow.SqftMax);
        Assert.Equal(2, willow.Beds);
        Assert.Equal(2.0, willow.Baths);
    }

    [Fact]
    public void Studio_ParsesToZeroBeds_AndASingleSqft()
    {
        var sandpiper = Plans().Single(p => p.RentalKey == "bncrtt8");

        Assert.Equal(0, sandpiper.Beds);
        Assert.Equal(1.0, sandpiper.Baths);
        Assert.Equal(552, sandpiper.SqftMin);
        Assert.Equal(552, sandpiper.SqftMax);
        Assert.Empty(sandpiper.Units); // "Not Available" — no unit grid at all
    }

    [Fact]
    public void PlanImage_ComesFromTheLazyBackgroundAttribute_NotAnImgSrc()
    {
        var willow = Plans().Single(p => p.RentalKey == "bmgrv28");

        Assert.Equal(
            "https://images1.apartments.com/i2/hCEE9Cttm59TktIwsLrzLVcupTqAjLXe0VV_BF0pDYo/115/image.png?p=1",
            willow.PlanImageUrl);
    }

    [Fact]
    public void AttachmentIdSentinel_MinusOne_ReadsAsNoAttachment()
    {
        // Real pages write data-attachmentid="-1" for "none"; carrying that through as a string
        // would make every plan look like it had an attachment.
        Assert.All(Plans(), p => Assert.Null(p.AttachmentId));
    }
}
