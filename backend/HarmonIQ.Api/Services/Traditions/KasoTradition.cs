using HarmonIQ.Api.Models;
using static HarmonIQ.Api.Services.Traditions.SiteRules;

namespace HarmonIQ.Api.Services.Traditions;

/// <summary>
/// Kasō (家相) / Fūsui (風水), Japanese house physiognomy.
///
/// <b>Orientation-gated</b>, for the same structural reason as Vastu: Kasō's signature doctrine is
/// the kimon (鬼門, the north-east "demon gate") and its opposite the urakimon (裏鬼門, south-west).
/// Both are <b>absolute compass positions</b>. A Kasō reading with no facing has had its defining
/// rule removed, so it returns <c>insufficient_evidence</c> rather than a renormalized score.
///
/// Note the deliberate divergence from Vastu on the same fact: Vastu wants water in the north-east
/// (the Jala zone), while Kasō reads water and drainage in the kimon as the configuration to flag.
/// The two traditions read one observation oppositely — which is precisely why they are scored
/// separately and never blended.
/// </summary>
public sealed class KasoTradition : ITradition
{
    public string Id => PrincipleSets.Kaso;
    public string DisplayName => "Kasō";
    public string Culture => "Japanese";
    public int Order => 4;
    public string RulesVersion => "kaso-1.0";
    public bool RequiresOrientation => true;
    public bool UsesWuxing => true;
    public string TraditionPhrase => "in Kasō terms";

    /// <summary>
    /// 風水 is deliberately absent: the Japanese fūsui and the Chinese feng shui are written with
    /// the same characters, and that query resolves to Feng Shui, the older and far more commonly
    /// searched of the two. Kasō keeps 家相, which is unambiguous.
    /// </summary>
    public IReadOnlyList<string> SearchSynonyms { get; } =
        ["kaso", "kasou", "kasō", "fusui", "fūsui", "家相"];

    public string OrientationGateExplanation =>
        "Kasō is organised around the kimon (鬼門), the north-east axis, which is an absolute compass "
        + "position. This unit's placement data has not resolved a facing, and a Kasō reading without "
        + "the kimon is missing the rule the tradition is built on, so HarmonIQ leaves it unscored.";

    public IReadOnlyList<RuleOutcome> SiteCatalogue(ListingEnvironment env, string? cardinal)
    {
        const string set = PrincipleSets.Kaso;
        var outcomes = new List<RuleOutcome>(11);

        // Kimon (鬼門) — the north-east. Absolute, like Vastu's quadrants; approximated by N+E sides.
        foreach (var s in new[] { "north", "east" })
        {
            outcomes.Add(Simple($"ks.site.kimon_clear.{s}", set, env.Side(s).Structures,
                v => v != "taller-building", 3,
                $"In Kasō, the {s} side falls on the kimon (鬼門) axis, which the tradition asks to be kept clear and unpressed rather than built up heavily."));
            outcomes.Add(Simple($"ks.site.kimon_dry.{s}", set, env.Side(s).Water,
                v => v == "none", 2,
                $"In Kasō, standing water on the {s} side sits on the kimon axis — the quarter the tradition asks to keep clean and dry. (Vastu Shastra reads the same placement favourably; the two traditions differ here.)"));
        }

        // Urakimon (裏鬼門) — the south-west, the kimon's opposite pole.
        foreach (var s in new[] { "south", "west" })
        {
            outcomes.Add(Simple($"ks.site.urakimon_settled.{s}", set, env.Side(s).Structures,
                v => v is "taller-building" or "similar", 2,
                $"In Kasō, the {s} side lies on the urakimon (裏鬼門), which is steadied by solid neighbouring mass."));
        }

        // Michi-tsukiatari (道路突き当り) — the road that dead-ends at the house.
        foreach (var s in Sides)
        {
            outcomes.Add(Simple($"ks.site.no_road_thrust.{s}", set, env.Side(s).Road,
                v => v != "t-junction", 3,
                $"In Kasō, a road that dead-ends at the {s} side is michi-tsukiatari (道路突き当り), a configuration the tradition consistently flags."));
        }

        // Nanmen (南面) — the southern aspect, valued strongly in Japanese siting.
        outcomes.Add(Simple("ks.site.south_aspect", set, env.Side("south").Structures,
            v => v != "taller-building", 3,
            "In Kasō, a southern aspect open to light (南面) is the most favoured siting; heavy mass to the south is the reading to note."));

        outcomes.Add(Simple("ks.site.settled_ground", set, env.Side("north").Slope,
            v => v != "falls", 1,
            "In Kasō, ground falling away to the north is read as leaving the dwelling's cold quarter unsupported."));

        return outcomes;
    }

    public string? RuleTitle(string ruleId) => BaseId(ruleId) switch
    {
        "ks.site.kimon_clear" => "Kimon Kept Clear",
        "ks.site.kimon_dry" => "Water on the Kimon",
        "ks.site.urakimon_settled" => "Urakimon Steadied",
        "ks.site.no_road_thrust" => "Road Dead-Ending at the House",
        "ks.site.south_aspect" => "Southern Aspect",
        "ks.site.settled_ground" => "Ground to the North",
        _ => null,
    };

    public Suggestion? RuleRemedy(string ruleId) => BaseId(ruleId) switch
    {
        "ks.site.kimon_clear" or "ks.site.kimon_dry" => new("Keep the north-east clean and bright",
            "Kasō asks for the kimon corner to be the tidiest part of the home: keep the north-east corner clear of storage and refuse, well lit, and easy to clean. A small salt dish or a white ceramic piece is the traditional touch.", "low", "high"),
        "ks.site.urakimon_settled" => new("Steady the south-west",
            "Place the home's heaviest, most permanent furniture in the south-west corner so the urakimon reads as settled rather than unanchored.", "low", "medium"),
        "ks.site.no_road_thrust" => new("Interrupt the approach line",
            "A screen, a tall plant, or a layered curtain set inside the window that faces the road-end breaks the thrust the tradition describes.", "low", "high"),
        "ks.site.south_aspect" => new("Maximise the southern light",
            "Keep the south-facing windows unobstructed and low-furnished, with pale, reflective surfaces to carry the light further into the home.", "low", "medium"),
        _ => null,
    };

    /// <summary>
    /// Japanese numerology: both 4 (shi, 死 death) and 9 (ku, 苦 suffering) are inauspicious —
    /// so <b>9 is unlucky here and lucky in Chinese practice</b>. 8 (hachi) is favourable for the
    /// widening fan shape of the character 八 (末広がり, suehirogari).
    /// </summary>
    public NumerologyCheck Numerology(string subject, string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        var bad = new List<string>();
        if (digits.Contains('4')) bad.Add("4 (shi), a homophone of 死, death");
        if (digits.Contains('9')) bad.Add("9 (ku), a homophone of 苦, suffering");
        if (bad.Count > 0)
            return new(subject, value, "unlucky", Id,
                $"Contains {string.Join(" and ", bad)} — the digits Japanese practice avoids, which is why many buildings skip these floor and room numbers. (Chinese numerology reads 9 favourably; the two traditions differ here.)",
                "An interior plaque that adds a digit changes the number as read at the door; a white or pale accent at the threshold is the conventional touch.");
        if (digits.Contains('8'))
            return new(subject, value, "lucky", Id,
                "Contains 8 (hachi), whose character 八 widens toward the base — suehirogari (末広がり), growing prosperity.", null);
        if (digits.Contains('7'))
            return new(subject, value, "lucky", Id,
                "Contains 7 (shichi), associated with good fortune in Japanese tradition.", null);
        return new(subject, value, "neutral", Id,
            "No strongly charged digits (4, 7, 8, 9) in Japanese numerology.", null);
    }

    public string InterpretPrompt(string factSheet, string? orientationHint) =>
        InterpretPromptBuilder.Build(DisplayName, Culture, Doctrine, factSheet, orientationHint);

    private const string Doctrine = """
Kasō (家相) judges a dwelling by the placement of its functions against an absolute compass grid. It
is descended from Chinese practice but has diverged: where a reading below differs from Feng Shui,
follow Kasō.

- **Kimon** (鬼門, the demon gate): the north-east. This is the tradition's defining rule. The kimon
  should carry nothing "unclean" — no toilet, no bath, no kitchen, no refuse or heavy storage — and
  should be kept scrupulously clean and bright. A kitchen or bathroom in the north-east is the single
  most significant Kasō finding.
- **Urakimon** (裏鬼門): the south-west, the kimon's opposite pole, held to the same restriction. A
  toilet or kitchen here is read the same way.
- **Absolute directions only.** Kimon and urakimon are compass positions, not positions relative to
  the entrance. With no resolved facing, omit these findings rather than approximate them.
- **Nanmen** (南面): a southern aspect for the principal living space and the main windows is the
  most favoured arrangement; a dark south side is a real negative.
- **Chūshin** (中心): the centre of the dwelling should be open and unencumbered. A toilet, a
  staircase, or a heavy fixed mass at the centre is unfavourable.
- **Kimon line through the plan**: a straight north-east/south-west axis running unbroken through
  the dwelling — entry to window, or door aligned to door — is read as carrying the kimon through
  the home.
- **Kihō** (鬼方) protrusions and recesses: irregular plan shapes are read by which quarter they cut
  into or extend toward. Note these only where the record actually describes the plan outline.
- **Gogyō** (五行): the Japanese five phases, cognate with wǔxíng — wood, fire, earth, metal, water.
  Report the balance as five 0-100 values.
- **Seiketsu** (清潔, cleanliness): Kasō weights cleanliness and order more heavily than most related
  traditions. Visible clutter is a stronger negative here than it would be elsewhere.
""";
}
