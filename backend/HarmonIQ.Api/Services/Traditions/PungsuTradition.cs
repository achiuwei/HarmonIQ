using HarmonIQ.Api.Models;
using static HarmonIQ.Api.Services.Traditions.SiteRules;

namespace HarmonIQ.Api.Services.Traditions;

/// <summary>
/// Pungsu-jiri (풍수지리 / 風水地理), Korean.
///
/// Ungated. Pungsu leads with landform rather than compass: its central doctrine, baesan-imsu
/// (배산임수, "mountain at the back, water in front"), is read relative to the site's own
/// orientation, so a facing sharpens the reading but is not a precondition for one.
///
/// The four-guardian scheme (사신사) names the flanks Cheongnyong and Baekho, which are a left/right
/// distinction. This catalogue deliberately treats the flanks as an <b>unordered pair</b> — see
/// <see cref="SiteRules.FlankSides"/> — matching the chirality prohibition the floor-plan lens
/// already enforces, since plans are routinely mirrored between opposite building stacks.
/// </summary>
public sealed class PungsuTradition : ITradition
{
    public string Id => PrincipleSets.Pungsu;
    public string DisplayName => "Pungsu-jiri";
    public int Order => 3;
    public string RulesVersion => "pungsu-1.0";
    public bool RequiresOrientation => false;
    public bool UsesWuxing => true;
    public string TraditionPhrase => "in Pungsu-jiri terms";

    public IReadOnlyList<string> SearchSynonyms { get; } =
        ["pungsu", "pungsu-jiri", "pungsujiri", "poongsu", "pungsu jiri", "풍수", "풍수지리"];

    public string OrientationGateExplanation =>
        "Pungsu-jiri reads landform before compass, so it does not require a facing.";

    public IReadOnlyList<RuleOutcome> SiteCatalogue(ListingEnvironment env, string? cardinal)
    {
        const string set = PrincipleSets.Pungsu;
        var outcomes = new List<RuleOutcome>(11);

        // Sal (살) — a straight run of hostile energy — is read from any side, facing or not.
        foreach (var s in Sides)
        {
            var road = env.Side(s).Road;
            outcomes.Add(Simple($"ps.site.no_sal.{s}", set, road,
                v => v != "t-junction", 3,
                $"In Pungsu-jiri, a road terminating head-on at the {s} side drives sal (살) — a straight, hostile current — into the site."));
            outcomes.Add(Simple($"ps.site.calm_road.{s}", set, road,
                v => v is not ("highway" or "t-junction"), 2,
                $"In Pungsu-jiri, a highway on the {s} side scatters the site's gi (기) rather than letting it collect."));
        }

        var front = FrontSides(cardinal);
        var back = front.Select(Opposite).ToArray();
        var flanks = FlankSides(cardinal);
        var frontLabel = front.Length == 0 ? "the front" : $"the {Join(front)} (front) side";
        var backLabel = back.Length == 0 ? "the rear" : $"the {Join(back)} (rear) side";

        // Baesan (배산) — the mountain at the back, the Hyeonmu (현무) guardian.
        outcomes.Add(Directional("ps.site.baesan", set, back, s => env.Side(s).Structures,
            v => v is "taller-building" or "similar", 3,
            $"In Pungsu-jiri, higher mass at {backLabel} supplies baesan (배산) — the mountain at the back that the Hyeonmu guardian represents."));
        outcomes.Add(Directional("ps.site.rising_back", set, back, s => env.Side(s).Slope,
            v => v != "falls", 2,
            $"In Pungsu-jiri, ground that holds or rises at {backLabel} supports the site rather than letting it slide away behind."));

        // Imsu (임수) — water in front, the Jujak (주작) aspect.
        outcomes.Add(Directional("ps.site.imsu", set, front, s => env.Side(s).Water,
            v => v != "none", 2,
            $"In Pungsu-jiri, water at {frontLabel} completes imsu (임수); water before the site is where gi is said to pool."));

        // Myeongdang (명당) — the bright, open court before the site.
        outcomes.Add(Directional("ps.site.myeongdang", set, front, s => env.Side(s).Structures,
            v => v == "open", 3,
            $"In Pungsu-jiri, open ground at {frontLabel} forms the myeongdang (명당), the bright court in which gi gathers before the dwelling."));
        outcomes.Add(Directional("ps.site.low_front", set, front, s => env.Side(s).Slope,
            v => v != "rises", 2,
            $"In Pungsu-jiri, the land at {frontLabel} should sit lower than the site; ground rising in front closes off the myeongdang."));

        // Sasinsa (사신사) — the flanking guardians, read as an unordered pair (no left/right claim).
        outcomes.Add(Directional("ps.site.embraced_flanks", set, flanks, s => env.Side(s).Structures,
            v => v is "similar" or "taller-building", 2,
            flanks.Length == 0
                ? "In Pungsu-jiri, mass to either flank encloses the site in the embrace the four guardians describe."
                : $"In Pungsu-jiri, mass on the {Join(flanks)} flanks encloses the site in the embrace the four guardians (사신사) describe."));

        return outcomes;
    }

    public string? RuleTitle(string ruleId) => BaseId(ruleId) switch
    {
        "ps.site.no_sal" => "Straight-Line Sal",
        "ps.site.calm_road" => "Road Speed and Gi",
        "ps.site.baesan" => "Mountain at the Back",
        "ps.site.rising_back" => "Ground Behind",
        "ps.site.imsu" => "Water in Front",
        "ps.site.myeongdang" => "Myeongdang — the Bright Court",
        "ps.site.low_front" => "Land Falling Away in Front",
        "ps.site.embraced_flanks" => "Guardian Flanks",
        _ => null,
    };

    public Suggestion? RuleRemedy(string ruleId) => BaseId(ruleId) switch
    {
        "ps.site.no_sal" or "ps.site.calm_road" => new("Break the straight current",
            "Layered curtains and a dense plant grouping at the windows facing that side interrupt the straight run and let the room settle.", "low", "high"),
        "ps.site.baesan" or "ps.site.rising_back" => new("Build the back of the home",
            "Place the tallest, heaviest furniture — wardrobes, shelving — along the rear-facing walls so the dwelling has its own mountain behind it.", "low", "medium"),
        "ps.site.imsu" or "ps.site.myeongdang" or "ps.site.low_front" => new("Open the front rooms",
            "Keep the rooms on the front side low-furnished and clear, with a mirror or a water image widening the outlook, so a bright court forms indoors.", "low", "medium"),
        "ps.site.embraced_flanks" => new("Draw in the flanks",
            "Anchor the side walls with paired shelving or tall plants so the living space feels held on both sides rather than exposed.", "low", "low"),
        _ => null,
    };

    /// <summary>
    /// Korean numerology: tetraphobia is strong — 4 is read through the hanja 死 (사, death), and
    /// many Korean buildings label the fourth floor "F". 7 is broadly auspicious. Unlike Chinese
    /// practice, 8 carries no special charge, and unlike Kasō, 9 is not inauspicious.
    /// </summary>
    public NumerologyCheck Numerology(string subject, string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Contains('4'))
            return new(subject, value, "unlucky", Id,
                "Contains the digit 4 (사), which shares its sound with the hanja 死 for death — the reason many Korean buildings label the fourth floor \"F\".",
                "A name plate or an interior number plaque that changes the number as read at the door is the usual softening.");
        if (digits.Contains('7'))
            return new(subject, value, "lucky", Id,
                "Contains 7 (칠), widely held as a fortunate number in Korean practice.", null);
        if (digits.Contains('3'))
            return new(subject, value, "lucky", Id,
                "Contains 3 (삼), associated with completeness and good fortune in Korean tradition.", null);
        return new(subject, value, "neutral", Id,
            "No strongly charged digits (3, 4, 7) in Korean numerology.", null);
    }

    public string InterpretPrompt(string factSheet, string? orientationHint) =>
        InterpretPromptBuilder.Build(DisplayName, Doctrine, factSheet, orientationHint);

    private const string Doctrine = """
Pungsu-jiri reads a dwelling through landform first and compass second. Its governing image is
baesan-imsu (배산임수) — mountain at the back, water in front — and inside a home that image is
recreated with mass, height, and openness rather than with directions.

- **Baesan indoors** (배산): the occupant's back should be supported — a solid wall behind the bed and
  behind the primary seat, with the room's heaviest mass along the rear. An unsupported back is the
  single strongest negative reading in this tradition.
- **Imsu and the open front** (임수): the outlook from the main seat and bed should be open, low, and
  unobstructed. Height or clutter directly in front of where someone sits or sleeps closes the view
  that Pungsu wants left open.
- **Myeongdang** (명당): the entry should open into a bright, uncluttered court rather than directly
  into a wall, a corner, or a congested passage.
- **Sal** (살): straight, uninterrupted runs — a long corridor, a door aligned to a door, a hard edge
  aimed at a bed. Pungsu treats the straight line itself as the problem, more emphatically than the
  related Chinese reading does.
- **Gi retention** (기): the tradition asks whether energy *collects* or *drains*. A home that is
  visually all-throughput — entry straight to window, no place the eye rests — reads as draining.
- **Ohaeng** (오행): the Korean five phases, cognate with wǔxíng — wood, fire, earth, metal, water.
  Report the balance as five 0-100 values, reading from materials, colours, and shapes.
- **Anjeong** (안정, settledness): Pungsu weights calm, enclosure, and repose more heavily than
  auspicious placement of individual objects. Prefer a finding about the room's overall settledness
  over a finding about one item, where the record supports both.
""";
}
