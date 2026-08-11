using System.Globalization;
using System.Text.RegularExpressions;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Extracts <see cref="ScrapedPlan"/>s from a multi-plan LDP's floor-plans section markup.
/// Reads <c>data-rentalkey</c> (primary identity — never the display name, which real LDPs
/// repeat across distinct plans), <c>data-modelname</c>, <c>data-attachmentid</c>, the plan
/// image, and the nested availability table, including rows behind a "Show More Units"
/// expander (a raw-markup scraper sees those rows regardless of the CSS/JS that hides them
/// until clicked).
/// </summary>
public interface IPlanScraper
{
    IReadOnlyList<ScrapedPlan> ExtractPlans(string html);
}

public class PlanScraper : IPlanScraper
{
    private static readonly Regex CardOpenRegex = new(
        "<div\\s+class=\"plan-card\"(?<attrs>[^>]*)>", RegexOptions.Compiled);

    private static readonly Regex ImageRegex = new(
        "<img\\s+class=\"plan-image\"[^>]*\\bsrc=\"([^\"]*)\"", RegexOptions.Compiled);

    private static readonly Regex BedsRegex = new(
        "class=\"beds\">\\s*(?:(\\d+)\\s*Bed|Studio)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex BathsRegex = new(
        "class=\"baths\">\\s*([\\d.]+)\\s*Bath", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Sq-ft range accepts a hyphen or an en dash between bounds ("1,091 – 1,204 Sq Ft").
    private static readonly Regex SqftRangeRegex = new(
        "class=\"sqft\">\\s*([\\d,]+)\\s*[\\u2013-]\\s*([\\d,]+)\\s*Sq Ft", RegexOptions.Compiled);

    private static readonly Regex SqftSingleRegex = new(
        "class=\"sqft\">\\s*([\\d,]+)\\s*Sq Ft", RegexOptions.Compiled);

    private static readonly Regex UnitRowRegex = new(
        "<tr\\s+class=\"unit-row[^\"]*\"[^>]*data-unit=\"([^\"]*)\"[\\s\\S]*?</tr>", RegexOptions.Compiled);

    private static readonly Regex UnitFloorRegex = new(
        "class=\"unit-floor\">\\s*([\\d]+)", RegexOptions.Compiled);

    private static readonly Regex UnitSqftRegex = new(
        "class=\"unit-sqft\">\\s*([\\d,]+)", RegexOptions.Compiled);

    private static readonly Regex UnitPriceRegex = new(
        "class=\"unit-price\">\\s*\\$?([\\d,]+)", RegexOptions.Compiled);

    public IReadOnlyList<ScrapedPlan> ExtractPlans(string html)
    {
        var opens = CardOpenRegex.Matches(html).Cast<Match>().ToList();
        var plans = new List<ScrapedPlan>(opens.Count);

        for (var i = 0; i < opens.Count; i++)
        {
            var start = opens[i].Index;
            var end = i + 1 < opens.Count ? opens[i + 1].Index : html.Length;
            var block = html[start..end];
            var attrs = opens[i].Groups["attrs"].Value;

            var rentalKey = Attr(attrs, "data-rentalkey") ?? "";
            var modelName = Attr(attrs, "data-modelname") ?? "";
            var attachmentId = Attr(attrs, "data-attachmentid");

            var imgMatch = ImageRegex.Match(block);
            var planImageUrl = imgMatch.Success ? imgMatch.Groups[1].Value : null;

            int? beds = null;
            var bedsMatch = BedsRegex.Match(block);
            if (bedsMatch.Success)
                beds = bedsMatch.Groups[1].Success ? int.Parse(bedsMatch.Groups[1].Value) : 0; // "Studio" => 0 beds

            double? baths = null;
            var bathsMatch = BathsRegex.Match(block);
            if (bathsMatch.Success)
                baths = double.Parse(bathsMatch.Groups[1].Value, CultureInfo.InvariantCulture);

            int? sqftMin = null, sqftMax = null;
            var rangeMatch = SqftRangeRegex.Match(block);
            if (rangeMatch.Success)
            {
                sqftMin = ParseInt(rangeMatch.Groups[1].Value);
                sqftMax = ParseInt(rangeMatch.Groups[2].Value);
            }
            else
            {
                var singleMatch = SqftSingleRegex.Match(block);
                if (singleMatch.Success)
                    sqftMin = sqftMax = ParseInt(singleMatch.Groups[1].Value);
            }

            var units = new List<ScrapedUnit>();
            foreach (Match unitMatch in UnitRowRegex.Matches(block))
            {
                var unitBlock = unitMatch.Value;
                var unitNumber = unitMatch.Groups[1].Value;
                var floorMatch = UnitFloorRegex.Match(unitBlock);
                var sqftMatch = UnitSqftRegex.Match(unitBlock);
                var priceMatch = UnitPriceRegex.Match(unitBlock);
                units.Add(new ScrapedUnit(
                    unitNumber,
                    floorMatch.Success ? int.Parse(floorMatch.Groups[1].Value) : null,
                    sqftMatch.Success ? ParseInt(sqftMatch.Groups[1].Value) : null,
                    priceMatch.Success ? decimal.Parse(priceMatch.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture) : null));
            }

            plans.Add(new ScrapedPlan(rentalKey, modelName, attachmentId, planImageUrl, beds, baths, sqftMin, sqftMax, units));
        }

        return plans;
    }

    private static string? Attr(string attrs, string name)
    {
        var m = Regex.Match(attrs, $"{Regex.Escape(name)}=\"([^\"]*)\"");
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    private static int ParseInt(string value) => int.Parse(value.Replace(",", ""), CultureInfo.InvariantCulture);
}
