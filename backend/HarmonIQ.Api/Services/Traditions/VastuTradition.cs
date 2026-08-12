using HarmonIQ.Api.Models;
using static HarmonIQ.Api.Services.Traditions.SiteRules;

namespace HarmonIQ.Api.Services.Traditions;

/// <summary>
/// Vastu Shastra (वास्तु शास्त्र), Indian.
///
/// <b>Orientation-gated.</b> Vastu's core — directional room placement and sleep orientation — cannot
/// run without a facing, and the leftovers are "Vastu with Vastu removed". The site catalogue itself
/// is absolute-direction only and so is orientation-independent; the dependence on a facing is
/// enforced by <see cref="OrientationGate"/>, not by coverage.
///
/// Vastu's five elements are the pancha bhuta (earth/water/fire/air/space) — not wǔxíng — so it
/// carries no <see cref="ElementBalance"/>; the section is omitted rather than zeroed (FR-27).
/// </summary>
public sealed class VastuTradition : ITradition
{
    public string Id => PrincipleSets.Vastu;
    public string DisplayName => "Vastu Shastra";
    public int Order => 2;
    public string RulesVersion => "vastu-2.0";
    public bool RequiresOrientation => true;
    public bool UsesWuxing => false;
    public string TraditionPhrase => "in Vastu Shastra terms";

    public IReadOnlyList<string> SearchSynonyms { get; } =
        ["vastu", "vaastu", "vasthu", "vastu shastra", "vaastu shastra", "वास्तु"];

    public string OrientationGateExplanation =>
        "Vastu Shastra reads a home from its facing direction, and this unit's placement data has not "
        + "resolved a facing. Rather than publish a Vastu reading with the directional half missing, "
        + "HarmonIQ leaves it unscored.";

    public IReadOnlyList<RuleOutcome> SiteCatalogue(ListingEnvironment env, string? cardinal)
    {
        const string set = PrincipleSets.Vastu;
        var outcomes = new List<RuleOutcome>(10);

        foreach (var s in new[] { "north", "east" })
        {
            outcomes.Add(Simple($"va.site.open_ne.{s}", set, env.Side(s).Structures,
                v => v != "taller-building", 3,
                $"In Vastu Shastra, the {s} side belongs to the light, open north-east quadrant, which is kept free of heavy mass."));
            outcomes.Add(Simple($"va.site.slope_ne.{s}", set, env.Side(s).Slope,
                v => v != "rises", 2,
                $"In Vastu Shastra, ground that falls away toward the {s} lets water and energy run toward the favourable north-east."));
        }

        foreach (var s in new[] { "south", "west" })
        {
            outcomes.Add(Simple($"va.site.grounded_sw.{s}", set, env.Side(s).Structures,
                v => v is "taller-building" or "similar", 2,
                $"In Vastu Shastra, substantial structures on the {s} side give the south-west the weight the tradition asks for."));
            outcomes.Add(Simple($"va.site.slope_sw.{s}", set, env.Side(s).Slope,
                v => v != "falls", 2,
                $"In Vastu Shastra, the {s} side is meant to sit high; rising or level ground there supports the south-west quadrant."));
        }

        var waters = Sides.Select(s => (side: s, value: env.Side(s).Water)).Where(x => Known(x.value)).ToList();
        outcomes.Add(new RuleOutcome("va.site.water_in_ne_quadrant", set,
            waters.Count > 0,
            waters.Count > 0 && !waters.Any(w => w.side is "south" or "west" && w.value != "none"),
            2,
            "In Vastu Shastra, standing water sits in the north-east water zone (Jala); water in the south-west sits in the quadrant reserved for weight and stability."));

        var roads = Sides.Select(s => (side: s, value: env.Side(s).Road)).Where(x => Known(x.value)).ToList();
        outcomes.Add(new RuleOutcome("va.site.approach_from_ne", set,
            roads.Count > 0,
            roads.Any(r => r.side is "north" or "east" && r.value != "none"),
            1,
            "In Vastu Shastra, road access from the north or east is the favourable approach direction."));

        return outcomes;
    }

    public string? RuleTitle(string ruleId) => BaseId(ruleId) switch
    {
        "va.site.open_ne" => "Open North-East",
        "va.site.slope_ne" => "Slope Toward the North-East",
        "va.site.grounded_sw" => "Weight in the South-West",
        "va.site.slope_sw" => "Height in the South-West",
        "va.site.water_in_ne_quadrant" => "Water Placement",
        "va.site.approach_from_ne" => "Approach Direction",
        _ => null,
    };

    public Suggestion? RuleRemedy(string ruleId) => BaseId(ruleId) switch
    {
        "va.site.open_ne" or "va.site.slope_ne" => new("Keep the north-east light",
            "Keep the north-east corner of the home uncluttered and well lit, with light colours and low furniture.", "low", "medium"),
        "va.site.grounded_sw" or "va.site.slope_sw" or "va.site.water_in_ne_quadrant" => new("Counterweight the south-west",
            "Keep the south-west corner of the home visually heavy — bookshelves, earthy colours, stone or ceramic decor.", "low", "medium"),
        _ => null,
    };

    /// <summary>Indian numerology: digit-sum reduction to a single ruling planet (FR-18).</summary>
    public NumerologyCheck Numerology(string subject, string value)
    {
        var digits = value.Where(char.IsDigit).Select(c => c - '0').ToList();
        if (digits.Count == 0)
            return new(subject, value, "neutral", Id, "No digits to reduce.", null);
        var sum = digits.Sum();
        while (sum > 9) sum = sum.ToString().Sum(c => c - '0');
        var (verdict, meaning) = sum switch
        {
            1 => ("lucky", "1 (Sun) — leadership and new beginnings"),
            2 => ("neutral", "2 (Moon) — sensitivity and partnership; balanced, not charged"),
            3 => ("lucky", "3 (Jupiter) — growth, learning, and family expansion"),
            4 => ("unlucky", "4 (Rahu) — instability and sudden change"),
            5 => ("lucky", "5 (Mercury) — communication, adaptability, and movement"),
            6 => ("lucky", "6 (Venus) — harmony and domestic wellbeing"),
            7 => ("neutral", "7 (Ketu) — introspection and spirituality; suits quiet households"),
            8 => ("unlucky", "8 (Saturn) — heaviness and karmic lessons"),
            _ => ("lucky", "9 (Mars) — energy, courage, and completion"),
        };
        return new(subject, value, verdict, Id,
            $"Digit sum {sum} — in Indian numerology, {meaning}.",
            verdict == "unlucky"
                ? "Add an interior door plaque with an extra digit so the number as read reduces to 1, 3, 5, 6, or 9."
                : null);
    }

    public string InterpretPrompt(string factSheet, string? orientationHint) =>
        InterpretPromptBuilder.Build(DisplayName, Doctrine, factSheet, orientationHint);

    private const string Doctrine = """
Vastu Shastra reads a dwelling as a body laid over the Vastu Purusha Mandala — a directional grid in
which each zone belongs to a deity and an element. Its readings are **absolute-directional**: north,
east, south and west mean what the compass says, never "relative to the front door". Without a
resolved facing, omit every directional finding.

- **Brahmasthan** (ब्रह्मस्थान): the centre of the dwelling should be open and unobstructed — no heavy
  furniture, no storage, no structural mass at the core.
- **North-east (Ishanya)**: the lightest, most open quadrant. Water, prayer, and light belong here;
  heavy mass, storage, and toilets do not.
- **South-west (Nairutya)**: the heaviest quadrant. The primary bedroom, wardrobes, and the most
  substantial furniture belong here. It should never be lighter than the north-east.
- **South-east (Agni)**: the fire zone — the kitchen and cooking appliances belong here, with the
  cook facing east where possible.
- **Sleeping orientation**: the head to the south or east is favourable; the head to the north is
  the reading to flag.
- **Pancha bhuta** (earth, water, fire, air, space): note the balance in words. Do **not** emit a
  wǔxíng element balance — wood and metal are not Vastu elements and the five do not map across.
- **Room colour and light**: light tones in the north-east, earthen and darker tones in the
  south-west; natural light from the east is strongly favourable.
- **Entrance**: an entrance in the north, east, or north-east is favourable; the reading of a
  south-west entrance is one to note with its remedy.
""";
}
