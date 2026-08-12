using HarmonIQ.Api.Models;
using static HarmonIQ.Api.Services.Traditions.SiteRules;

namespace HarmonIQ.Api.Services.Traditions;

/// <summary>
/// Feng Shui (風水), Chinese. Form school (巒頭) plus Black Hat interior readings.
///
/// Ungated: form-school doctrine is landform- and sightline-driven, so a reading without a compass
/// is still a real reading. The compass-school rules simply drop to not-applicable, lowering
/// coverage and therefore the site lens's weight.
/// </summary>
public sealed class FengShuiTradition : ITradition
{
    public string Id => PrincipleSets.FengShui;
    public string DisplayName => "Feng Shui";
    public int Order => 1;
    public string RulesVersion => "fengshui-2.0";
    public bool RequiresOrientation => false;
    public bool UsesWuxing => true;
    public string TraditionPhrase => "in form-school Feng Shui terms";

    public IReadOnlyList<string> SearchSynonyms { get; } =
        ["feng shui", "fengshui", "feng-shui", "fung shui", "风水", "風水"];

    public string OrientationGateExplanation =>
        "Feng Shui does not require a facing to produce a reading.";

    /// <summary>The rules that can only be judged once a facing has resolved.</summary>
    public static IReadOnlyList<string> OrientationDependentRuleIds { get; } =
    [
        "fs.site.bright_hall", "fs.site.unobstructed_facing", "fs.site.water_at_facing",
        "fs.site.settled_approach", "fs.site.armchair_backing", "fs.site.dry_back",
    ];

    public IReadOnlyList<RuleOutcome> SiteCatalogue(ListingEnvironment env, string? cardinal)
    {
        const string set = PrincipleSets.FengShui;
        var outcomes = new List<RuleOutcome>(14);

        // Sha-chi rules read every side; a road points at the building regardless of the door.
        foreach (var s in Sides)
        {
            var road = env.Side(s).Road;
            outcomes.Add(Simple($"fs.site.no_t_junction.{s}", set, road,
                v => v != "t-junction", 3,
                $"In form-school Feng Shui, a T-junction on the {s} side aims a straight run of fast-moving chi (sha chi) at the building."));
            outcomes.Add(Simple($"fs.site.calm_road.{s}", set, road,
                v => v is not ("highway" or "t-junction"), 2,
                $"In form-school Feng Shui, a highway or straight-on road line on the {s} side keeps chi moving quickly past instead of letting it settle."));
        }

        var front = FrontSides(cardinal);
        var back = front.Select(Opposite).ToArray();
        var frontLabel = front.Length == 0 ? "the facing direction" : $"the {Join(front)} (facing) side";
        var backLabel = back.Length == 0 ? "the rear" : $"the {Join(back)} (rear) side";

        outcomes.Add(Directional("fs.site.bright_hall", set, front, s => env.Side(s).Structures,
            v => v == "open", 3,
            $"In form-school Feng Shui, open ground at {frontLabel} forms a bright hall (ming tang) where chi can gather before the entrance."));
        outcomes.Add(Directional("fs.site.unobstructed_facing", set, front, s => env.Side(s).Structures,
            v => v != "taller-building", 2,
            $"In form-school Feng Shui, a much taller structure at {frontLabel} presses on the building's outlook."));
        outcomes.Add(Directional("fs.site.water_at_facing", set, front, s => env.Side(s).Water,
            v => v != "none", 1,
            $"In form-school Feng Shui, water at {frontLabel} is read as auspicious — wealth is said to gather where water settles before the entrance."));
        outcomes.Add(Directional("fs.site.settled_approach", set, front, s => env.Side(s).Road,
            v => v is not ("busy" or "highway" or "t-junction"), 2,
            $"In form-school Feng Shui, a calm approach at {frontLabel} lets chi settle at the entrance rather than rush past it."));
        outcomes.Add(Directional("fs.site.armchair_backing", set, back, s => env.Side(s).Structures,
            v => v is "taller-building" or "similar", 3,
            $"In form-school Feng Shui, solid structures at {backLabel} give the building 'mountain' support — the classic armchair arrangement."));
        outcomes.Add(Directional("fs.site.dry_back", set, back, s => env.Side(s).Water,
            v => v == "none", 1,
            $"In form-school Feng Shui, water at {backLabel} is read as softening the building's backing."));

        return outcomes;
    }

    public string? RuleTitle(string ruleId) => BaseId(ruleId) switch
    {
        "fs.site.no_t_junction" => "T-Junction Line",
        "fs.site.calm_road" => "Road Speed and Chi",
        "fs.site.bright_hall" => "Bright Hall",
        "fs.site.unobstructed_facing" => "Facing Outlook",
        "fs.site.water_at_facing" => "Water at the Facing",
        "fs.site.settled_approach" => "Approach to the Entrance",
        "fs.site.armchair_backing" => "Armchair Backing",
        "fs.site.dry_back" => "Water Behind",
        _ => null,
    };

    public Suggestion? RuleRemedy(string ruleId) => BaseId(ruleId) switch
    {
        "fs.site.no_t_junction" => new("Screen the entrance line",
            "Break the straight-on rush with a hedge, a pair of planters, or a screen inside the entry line; heavy curtains on windows facing that side also soften it.", "low", "high"),
        "fs.site.calm_road" => new("Soften the rushing side",
            "Dense plants and layered curtains on the windows facing that side slow the visual rush and dampen noise.", "low", "medium"),
        "fs.site.unobstructed_facing" or "fs.site.bright_hall" => new("Lift the entry light",
            "Brighten the entrance and front-facing rooms with warm lighting and a mirror placed to widen the view — not facing the door.", "low", "medium"),
        "fs.site.settled_approach" => new("Slow the approach",
            "Layer the threshold: a rug, a console, and a plant just inside the door let arriving chi settle.", "low", "medium"),
        "fs.site.armchair_backing" or "fs.site.dry_back" => new("Weight the rear rooms",
            "Place heavier furniture and earthy tones in rooms on the rear side to symbolically anchor the back.", "low", "low"),
        _ => null,
    };

    /// <summary>
    /// Chinese numerology: homophone-driven. 4 (sì) ≈ death (sǐ); 8 (bā) ≈ prosper (fā);
    /// 9 (jiǔ) ≈ long-lasting. Note 9 is auspicious here and inauspicious in Kasō — the two
    /// traditions read the same digit oppositely, which is exactly why they score separately.
    /// </summary>
    public NumerologyCheck Numerology(string subject, string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Contains('4'))
        {
            var combo = digits.Contains("14") ? " The pair 14 (yao sì) sounds like \"will die\" — considered especially inauspicious."
                      : digits.Contains("24") ? " The pair 24 (èr sì) sounds like \"easy to die\" — considered especially inauspicious." : "";
            return new(subject, value, "unlucky", Id,
                $"Contains the digit 4 (sì), a homophone of death (sǐ) in Chinese numerology.{combo}",
                "Add a small interior plaque so the number read at the door sums to an auspicious digit, or place a red accent at the threshold.");
        }
        if (digits.Contains('8'))
            return new(subject, value, "lucky", Id,
                "Contains 8 (bā), a homophone of prosperity (fā) — the most auspicious digit in Chinese numerology.", null);
        if (digits.Contains('9'))
            return new(subject, value, "lucky", Id,
                "Contains 9 (jiǔ), a homophone of long-lasting — associated with longevity in Chinese numerology.", null);
        return new(subject, value, "neutral", Id,
            "No strongly charged digits (4, 8, 9) in Chinese numerology.", null);
    }

    public string InterpretPrompt(string factSheet, string? orientationHint) =>
        InterpretPromptBuilder.Build(DisplayName, Doctrine, factSheet, orientationHint);

    private const string Doctrine = """
Feng Shui reads a home as a vessel for qi (氣) — how it enters, circulates, and settles. Work from
the form school (巒頭) first, which needs no compass, and add compass-school readings only when a
facing is given.

- **Commanding position** (最佳位置): the bed, desk, and stove should let the occupant see the door
  without being in line with it. Directly in line with the door is the "coffin position".
- **Qi flow and the mouth of qi**: the entry should be uncluttered. A straight, unbroken sightline
  from the front door to a window or rear door lets qi rush through rather than circulate.
- **Poison arrows** (煞氣): sharp corners, exposed beams, or column edges aimed at a bed or seating.
- **Wǔxíng balance** (五行): wood, fire, earth, metal, water — read from materials, colours, and
  shapes. Report the balance as five 0-100 values. An excess is as notable as an absence.
- **Mirrors**: a mirror facing the bed, or facing the front door, is read as pushing qi back out.
- **Bed placement**: under a window or beam, or sharing a wall with a toilet, weakens rest. Solid
  headboard against a solid wall is the ideal.
- **Clutter and storage**: under-bed storage and blocked pathways are read as stagnant qi.
- **Light and air**: natural light and unobstructed circulation are the strongest positive signals.
- **Pairs and symmetry**: paired nightstands and lamps are read as supporting partnership.
""";
}
