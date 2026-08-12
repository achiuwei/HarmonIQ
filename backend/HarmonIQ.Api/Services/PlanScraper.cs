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

/// <summary>
/// Two markup dialects, tried in order: the mock LDP's (<c>div.plan-card</c>, authored in this
/// repo) and apartments.com's own (<c>div.pricingGridItem</c>). They are separate paths rather
/// than one set of loosened regexes because the two shapes disagree on more than class names —
/// see <see cref="ExtractApartmentsPlans"/> for what actually differs.
/// </summary>
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

    // ------------------------------------------------------------------ apartments.com markup

    private static readonly Regex ApartmentsItemOpenRegex = new(
        "<div\\s+class=\"pricingGridItem[^\"]*\"[^>]*>", RegexOptions.Compiled);

    // Plan identity sits on the model wrapper. Scoping to that class matters: a plan block also
    // contains one data-rentalkey per unit row, and those are unit keys, not the plan's.
    private static readonly Regex ApartmentsPlanKeyRegex = new(
        "class=\"priceGridModelWrapper[^\"]*\"\\s+data-rentalkey=\"([^\"]*)\"", RegexOptions.Compiled);

    private static readonly Regex ApartmentsModelNameRegex = new(
        "data-modelname=\"([^\"]*)\"", RegexOptions.Compiled);

    private static readonly Regex ApartmentsAttachmentRegex = new(
        "data-attachmentid=\"([^\"]*)\"", RegexOptions.Compiled);

    private static readonly Regex ApartmentsPlanImageRegex = new(
        "data-background-image=\"([^\"]*)\"", RegexOptions.Compiled);

    // Beds/baths/sqft are three bare <span>s in one wrapper — no per-field class to anchor on.
    private static readonly Regex ApartmentsDetailsRegex = new(
        "class=\"detailsTextWrapper\">([\\s\\S]*?)</span>\\s*</span>", RegexOptions.Compiled);

    private static readonly Regex ApartmentsBedsRegex = new(
        ">\\s*(?:(\\d+)\\s*Beds?|(Studio))\\s*<", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ApartmentsBathsRegex = new(
        ">\\s*([\\d.]+)\\s*Baths?\\s*<", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // The sqft span is the last one in the wrapper, so the captured details end at "Sq Ft" with
    // its closing tag already consumed by ApartmentsDetailsRegex — hence "< or end of input".
    private static readonly Regex ApartmentsSqftRangeRegex = new(
        ">\\s*([\\d,]+)\\s*[\\u2013-]\\s*([\\d,]+)\\s*Sq Ft\\s*(?:<|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ApartmentsSqftSingleRegex = new(
        ">\\s*([\\d,]+)\\s*Sq Ft\\s*(?:<|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ApartmentsUnitRowRegex = new(
        "<li\\s+class=\"unitContainer[^\"]*\"[^>]*\\bdata-unit=\"([^\"]*)\"[\\s\\S]*?</li>", RegexOptions.Compiled);

    private static readonly Regex ApartmentsUnitSqftRegex = new(
        "class=\"sqftColumn[^\"]*\">\\s*<span>\\s*([\\d,]+)", RegexOptions.Compiled);

    private static readonly Regex ApartmentsUnitPriceRegex = new(
        "class=\"pricingColumn[^\"]*\">\\s*<span>\\s*\\$?([\\d,]+)", RegexOptions.Compiled);

    public IReadOnlyList<ScrapedPlan> ExtractPlans(string html)
    {
        var mock = ExtractMockPlans(html);
        return mock.Count > 0 ? mock : ExtractApartmentsPlans(html);
    }

    /// <summary>
    /// The real apartments.com floor-plans section. Differs from the mock in three ways that a
    /// shared regex set could not straddle:
    ///
    /// <list type="number">
    /// <item><description>
    /// Identity is nested, not on the block: the block is <c>div.pricingGridItem</c>, the plan key
    /// is on an inner <c>div.priceGridModelWrapper</c>, and the model name on an inner
    /// <c>div.floorplanButton</c>. Reading <c>data-rentalkey</c> blockwise would pick up a unit's
    /// key instead of the plan's.
    /// </description></item>
    /// <item><description>
    /// Beds/baths/sqft carry no per-field class — they are three bare <c>&lt;span&gt;</c>s inside
    /// <c>.detailsTextWrapper</c>, so they are matched by content rather than by selector.
    /// </description></item>
    /// <item><description>
    /// Unit rows are <c>&lt;li class="unitContainer"&gt;</c> and publish <b>no floor number</b>.
    /// A real page simply does not carry one, so <see cref="ScrapedUnit.Floor"/> stays null rather
    /// than being inferred from the unit number — at Enzo every unit number begins with the same
    /// digit as the street address, so that inference would be wrong as well as invented.
    /// </description></item>
    /// </list>
    ///
    /// Plans are folded by rental key: a real page renders each plan once per bedroom-count tab
    /// ("All", "1 Bedroom", …), so the same plan legitimately appears several times and a naive
    /// scan would double every one of them. The first occurrence wins — the "All" tab is rendered
    /// first and carries the complete unit grid.
    /// </summary>
    private static List<ScrapedPlan> ExtractApartmentsPlans(string html)
    {
        var opens = ApartmentsItemOpenRegex.Matches(html).Cast<Match>().ToList();
        var plans = new List<ScrapedPlan>(opens.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < opens.Count; i++)
        {
            var start = opens[i].Index;
            var end = i + 1 < opens.Count ? opens[i + 1].Index : html.Length;
            var block = html[start..end];

            var keyMatch = ApartmentsPlanKeyRegex.Match(block);
            if (!keyMatch.Success) continue;
            var rentalKey = System.Net.WebUtility.HtmlDecode(keyMatch.Groups[1].Value);
            if (!seen.Add(rentalKey)) continue;

            var nameMatch = ApartmentsModelNameRegex.Match(block);
            var modelName = nameMatch.Success
                ? System.Net.WebUtility.HtmlDecode(nameMatch.Groups[1].Value) : "";

            // "-1" is the sentinel for "no attachment", not an id.
            var attachMatch = ApartmentsAttachmentRegex.Match(block);
            var attachmentId = attachMatch.Success && attachMatch.Groups[1].Value != "-1"
                ? attachMatch.Groups[1].Value : null;

            var imgMatch = ApartmentsPlanImageRegex.Match(block);
            var planImageUrl = imgMatch.Success
                ? System.Net.WebUtility.HtmlDecode(imgMatch.Groups[1].Value) : null;

            int? beds = null;
            double? baths = null;
            int? sqftMin = null, sqftMax = null;
            var detailsMatch = ApartmentsDetailsRegex.Match(block);
            if (detailsMatch.Success)
            {
                var details = detailsMatch.Groups[1].Value;

                var bedsMatch = ApartmentsBedsRegex.Match(details);
                if (bedsMatch.Success)
                    beds = bedsMatch.Groups[1].Success ? int.Parse(bedsMatch.Groups[1].Value) : 0;

                var bathsMatch = ApartmentsBathsRegex.Match(details);
                if (bathsMatch.Success)
                    baths = double.Parse(bathsMatch.Groups[1].Value, CultureInfo.InvariantCulture);

                var rangeMatch = ApartmentsSqftRangeRegex.Match(details);
                if (rangeMatch.Success)
                {
                    sqftMin = ParseInt(rangeMatch.Groups[1].Value);
                    sqftMax = ParseInt(rangeMatch.Groups[2].Value);
                }
                else
                {
                    var singleMatch = ApartmentsSqftSingleRegex.Match(details);
                    if (singleMatch.Success) sqftMin = sqftMax = ParseInt(singleMatch.Groups[1].Value);
                }
            }

            var units = new List<ScrapedUnit>();
            foreach (Match unitMatch in ApartmentsUnitRowRegex.Matches(block))
            {
                var unitBlock = unitMatch.Value;
                var sqftMatch = ApartmentsUnitSqftRegex.Match(unitBlock);
                var priceMatch = ApartmentsUnitPriceRegex.Match(unitBlock);
                units.Add(new ScrapedUnit(
                    System.Net.WebUtility.HtmlDecode(unitMatch.Groups[1].Value),
                    null, // apartments.com publishes no floor
                    sqftMatch.Success ? ParseInt(sqftMatch.Groups[1].Value) : null,
                    priceMatch.Success
                        ? decimal.Parse(priceMatch.Groups[1].Value.Replace(",", ""), CultureInfo.InvariantCulture)
                        : null));
            }

            plans.Add(new ScrapedPlan(
                rentalKey, modelName, attachmentId, planImageUrl, beds, baths, sqftMin, sqftMax, units));
        }

        return plans;
    }

    // ------------------------------------------------------------------ mock LDP markup

    private static List<ScrapedPlan> ExtractMockPlans(string html)
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
