using HarmonIQ.Api.Models;
using static HarmonIQ.Api.Services.Traditions.SiteRules;

namespace HarmonIQ.Api.Services.Traditions;

/// <summary>
/// Phong Thủy, Vietnamese.
///
/// Ungated. Phong Thủy is the closest of the five to Chinese Feng Shui — it shares the form-school
/// (hình thế) vocabulary directly — and this file does not pretend otherwise. What it does carry
/// distinctly is its own rule ids, its own naming, a markedly stronger reading of the road driving
/// at the house (đường đâm thẳng), and a numerology in which <b>7 is inauspicious</b> (thất, loss)
/// where Chinese practice treats it as unremarkable.
/// </summary>
public sealed class PhongThuyTradition : ITradition
{
    public string Id => PrincipleSets.PhongThuy;
    public string DisplayName => "Phong Thủy";
    public string Culture => "Vietnamese";
    public int Order => 5;
    public string RulesVersion => "phongthuy-1.0";
    public bool RequiresOrientation => false;
    public bool UsesWuxing => true;
    public string TraditionPhrase => "in Phong Thủy terms";

    public IReadOnlyList<string> SearchSynonyms { get; } =
        ["phong thuy", "phongthuy", "phong thủy", "phong-thuy"];

    public string OrientationGateExplanation =>
        "Phong Thủy reads hình thế (landform) without requiring a facing.";

    public IReadOnlyList<RuleOutcome> SiteCatalogue(ListingEnvironment env, string? cardinal)
    {
        const string set = PrincipleSets.PhongThuy;
        var outcomes = new List<RuleOutcome>(12);

        // Đường đâm thẳng — the road "stabbing" at the house. Weighted heavily in Vietnamese practice.
        foreach (var s in Sides)
        {
            var road = env.Side(s).Road;
            outcomes.Add(Simple($"pt.site.no_duong_dam.{s}", set, road,
                v => v != "t-junction", 3,
                $"In Phong Thủy, a road running straight at the {s} side is đường đâm thẳng — a direct thrust the tradition treats as a serious configuration."));
            outcomes.Add(Simple($"pt.site.calm_road.{s}", set, road,
                v => v is not ("highway" or "t-junction"), 2,
                $"In Phong Thủy, heavy fast traffic on the {s} side disperses khí rather than letting it gather."));
        }

        var front = FrontSides(cardinal);
        var back = front.Select(Opposite).ToArray();
        var frontLabel = front.Length == 0 ? "the facing direction" : $"the {Join(front)} (facing) side";
        var backLabel = back.Length == 0 ? "the rear" : $"the {Join(back)} (rear) side";

        outcomes.Add(Directional("pt.site.minh_duong", set, front, s => env.Side(s).Structures,
            v => v == "open", 3,
            $"In Phong Thủy, open ground at {frontLabel} forms the minh đường (明堂), the bright court where khí collects before the door."));
        outcomes.Add(Directional("pt.site.tu_thuy", set, front, s => env.Side(s).Water,
            v => v != "none", 2,
            $"In Phong Thủy, water at {frontLabel} gives tụ thủy — gathering water, read as accumulating fortune before the entrance."));
        outcomes.Add(Directional("pt.site.huyen_vu", set, back, s => env.Side(s).Structures,
            v => v is "taller-building" or "similar", 3,
            $"In Phong Thủy, solid mass at {backLabel} provides the huyền vũ backing — the support behind the house."));
        outcomes.Add(Directional("pt.site.no_overshadow", set, front, s => env.Side(s).Structures,
            v => v != "taller-building", 2,
            $"In Phong Thủy, a much taller building at {frontLabel} presses down on the house's outlook and its minh đường."));
        outcomes.Add(Directional("pt.site.dry_back", set, back, s => env.Side(s).Water,
            v => v == "none", 1,
            $"In Phong Thủy, water at {backLabel} is read as undercutting the huyền vũ support behind the house."));

        return outcomes;
    }

    public string? RuleTitle(string ruleId) => BaseId(ruleId) switch
    {
        "pt.site.no_duong_dam" => "Đường Đâm Thẳng — Road Thrust",
        "pt.site.calm_road" => "Traffic and Khí",
        "pt.site.minh_duong" => "Minh Đường — Bright Court",
        "pt.site.tu_thuy" => "Tụ Thủy — Gathering Water",
        "pt.site.huyen_vu" => "Huyền Vũ Backing",
        "pt.site.no_overshadow" => "Overshadowed Facing",
        "pt.site.dry_back" => "Water Behind",
        _ => null,
    };

    public Suggestion? RuleRemedy(string ruleId) => BaseId(ruleId) switch
    {
        "pt.site.no_duong_dam" => new("Deflect the thrust",
            "Vietnamese practice answers a road thrust with something that visually deflects it: a screen or a dense plant grouping inside the window on that side, and heavy curtains kept drawn when the room is not in use.", "low", "high"),
        "pt.site.calm_road" => new("Quiet the traffic side",
            "Layered curtains and dense planting at the windows facing the traffic slow the visual rush and dampen the noise.", "low", "medium"),
        "pt.site.minh_duong" or "pt.site.no_overshadow" => new("Open the bright court indoors",
            "Keep the rooms behind the entrance clear and low-furnished, with warm lighting and a mirror set to widen the view — never facing the door directly.", "low", "medium"),
        "pt.site.tu_thuy" => new("Bring gathering water inside",
            "A small tabletop water feature or a water image placed in the front-facing room stands in for tụ thủy where the site provides none.", "low", "low"),
        "pt.site.huyen_vu" or "pt.site.dry_back" => new("Anchor the rear",
            "Put the tallest and heaviest furniture against the rear-facing walls so the home carries its own huyền vũ support.", "low", "medium"),
        _ => null,
    };

    /// <summary>
    /// Vietnamese numerology: tứ (4) ≈ tử, death; bát (8) ≈ phát, to prosper; cửu (9) ≈ long-lasting.
    /// Distinctly, <b>7 (thất) is inauspicious</b> — it shares its sound with thất bại, loss/failure —
    /// where Chinese practice treats 7 as unremarkable and Korean and Japanese practice both read it
    /// favourably.
    /// </summary>
    public NumerologyCheck Numerology(string subject, string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Contains('4'))
            return new(subject, value, "unlucky", Id,
                "Contains the digit 4 (tứ), which shares its sound with tử, death, in Vietnamese numerology.",
                "An interior number plaque that changes the number as read at the door is the usual remedy; a red accent at the threshold is also common.");
        if (digits.Contains('7'))
            return new(subject, value, "unlucky", Id,
                "Contains 7 (thất), which shares its sound with thất bại — loss or failure — in Vietnamese practice. (Korean and Japanese numerology both read 7 favourably; the traditions differ here.)",
                "A door plaque adding a digit shifts the number as read at the entrance.");
        if (digits.Contains('8'))
            return new(subject, value, "lucky", Id,
                "Contains 8 (bát), a homophone of phát — to prosper — the most sought-after digit in Vietnamese practice.", null);
        if (digits.Contains('9'))
            return new(subject, value, "lucky", Id,
                "Contains 9 (cửu), a homophone of long-lasting, associated with durability and longevity.", null);
        return new(subject, value, "neutral", Id,
            "No strongly charged digits (4, 7, 8, 9) in Vietnamese numerology.", null);
    }

    public string InterpretPrompt(string factSheet, string? orientationHint) =>
        InterpretPromptBuilder.Build(DisplayName, Culture, Doctrine, factSheet, orientationHint);

    private const string Doctrine = """
Phong Thủy shares its form-school (hình thế) foundation with Chinese Feng Shui, and where the two
agree you should read them alike. Vietnamese practice diverges in emphasis, and where it does,
follow Phong Thủy.

- **Đường đâm thẳng** (the road driving straight at the house): the strongest negative reading in
  Vietnamese practice, weighted more heavily than the cognate Chinese sha-chi reading. Any straight
  run aimed at the entrance — a corridor, an aligned pair of doors, a road terminating at the
  building — is a primary finding, not a secondary one.
- **Minh đường** (明堂, bright court): the space immediately inside and in front of the entrance
  should be open and bright. A door opening onto a wall, a column, or a congested passage closes it.
- **Tụ thủy** (gathering water): water, or its stand-in, before the facing is read as accumulating
  fortune. Placement matters more than quantity.
- **Huyền vũ** (the backing): the bed and the principal seat need solid support behind them. A bed
  under a window, or a sofa with its back to an open room, is the reading to flag.
- **Bếp — the kitchen** (táo quân, the kitchen god): the stove carries unusual weight in Vietnamese
  homes. The stove should not be visible from the front door, should not sit directly opposite or
  adjacent to a sink or refrigerator (the water–fire clash, thủy hỏa xung khắc), and should not back
  onto a bathroom wall. Treat any stove observation as a high-priority finding.
- **Bàn thờ** (the ancestral altar): where the record shows an altar or a high shelf used as one, it
  should sit high, against a solid wall, facing into the main room, and never directly opposite a
  bathroom or bedroom door, and never beneath a beam.
- **Ngũ hành** (five phases: kim, mộc, thủy, hỏa, thổ — metal, wood, water, fire, earth). Report the
  balance as five 0-100 values, reading from materials, colours, and shapes.
- **Hướng nhà** (house direction): apply compass readings only when a facing is given; otherwise
  restrict yourself to hình thế.
""";
}
