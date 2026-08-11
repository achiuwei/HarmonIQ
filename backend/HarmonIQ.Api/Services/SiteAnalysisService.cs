using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// The deterministic site lens. Emits one <see cref="RuleOutcome"/> per rule in the requested
/// principle set's catalogue. An unknown environment value makes a rule <b>not applicable</b>,
/// never violated; orientation-dependent Feng Shui rules drop to not-applicable when no facing
/// has resolved, which lowers coverage (and therefore the site lens's weight) rather than the
/// score. The Vastu catalogue is absolute-direction only and so is orientation-independent —
/// Vastu's dependence on a facing is enforced by <see cref="VastuGate"/>, not by coverage.
/// </summary>
public class SiteAnalysisService
{
    /// <summary>Rules versions are per principle set: a Vastu change must not invalidate Feng Shui.</summary>
    public const string RulesVersionFengShui = "fengshui-2.0";
    public const string RulesVersionVastu = "vastu-2.0";

    public static string RulesVersionFor(string principleSet) =>
        principleSet == PrincipleSets.Vastu ? RulesVersionVastu : RulesVersionFengShui;

    private static readonly string[] Sides = ["north", "east", "south", "west"];

    private static readonly string[] Compass =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    /// <summary>The Feng Shui rules that can only be judged once a facing has resolved.</summary>
    public static IReadOnlyList<string> OrientationDependentRuleIds { get; } =
    [
        "fs.site.bright_hall", "fs.site.unobstructed_facing", "fs.site.water_at_facing",
        "fs.site.settled_approach", "fs.site.armchair_backing", "fs.site.dry_back",
    ];

    // ---------------------------------------------------------------- public API

    public LensResult EvaluateSet(ListingEnvironment? env, SubjectOrientation? orientation, string principleSet)
    {
        env ??= ListingEnvironment.AllUnknown;
        var outcomes = principleSet == PrincipleSets.Vastu
            ? VastuCatalogue(env)
            : FengShuiCatalogue(env, ResolvedCardinal(orientation));
        return RuleEvaluation.ToLens(LensResult.Site, outcomes);
    }

    /// <summary>The resolved cardinal/intercardinal facing, or null when orientation is absent or unresolved.</summary>
    public static string? ResolvedCardinal(SubjectOrientation? o)
    {
        if (o is null || string.IsNullOrWhiteSpace(o.Source) || o.Source == "none") return null;
        if (!string.IsNullOrWhiteSpace(o.Cardinal))
        {
            var c = o.Cardinal.Trim().ToLowerInvariant();
            return Compass.Contains(c) ? c : null;
        }
        if (o.FacingDegrees is double d) return Compass[(int)Math.Round((((d % 360) + 360) % 360) / 45.0) % 8];
        return null;
    }

    public static bool HasResolvedOrientation(SubjectOrientation? o) => ResolvedCardinal(o) is not null;

    // ---------------------------------------------------------------- Feng Shui catalogue (14 rules)

    private static List<RuleOutcome> FengShuiCatalogue(ListingEnvironment env, string? cardinal)
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

    // ---------------------------------------------------------------- Vastu catalogue (10 rules)

    private static List<RuleOutcome> VastuCatalogue(ListingEnvironment env)
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

    // ---------------------------------------------------------------- helpers

    private static bool Known(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value != "unknown";

    /// <summary>A rule read from a single side's value. Unknown ⇒ not applicable, never a violation.</summary>
    private static RuleOutcome Simple(
        string id, string set, string value, Func<string, bool> satisfied, int severity, string text)
    {
        var applicable = Known(value);
        return new RuleOutcome(id, set, applicable, applicable && satisfied(value), severity, text);
    }

    /// <summary>
    /// A rule read from the facing (or rear) side(s). Applicable when at least one of those sides
    /// reports a known value; satisfied when every known one satisfies it. With no resolved facing
    /// there are no sides, so the rule is emitted as not-applicable and coverage falls.
    /// </summary>
    private static RuleOutcome Directional(
        string id, string set, string[] sides, Func<string, string> read,
        Func<string, bool> satisfied, int severity, string text)
    {
        var values = sides.Select(read).Where(Known).ToList();
        var applicable = values.Count > 0;
        return new RuleOutcome(id, set, applicable, applicable && values.All(satisfied), severity, text);
    }

    private static string Join(string[] sides) => string.Join("/", sides);

    // Facing side(s): intercardinal orientations touch two sides.
    private static string[] FrontSides(string? cardinal) => cardinal switch
    {
        "north" or "east" or "south" or "west" => [cardinal],
        "northeast" => ["north", "east"], "southeast" => ["south", "east"],
        "southwest" => ["south", "west"], "northwest" => ["north", "west"],
        _ => [],
    };

    private static string Opposite(string side) => side switch
    {
        "north" => "south", "south" => "north", "east" => "west", _ => "east",
    };

    private static string BaseId(string ruleId)
    {
        var parts = ruleId.Split('.');
        return parts.Length > 3 ? string.Join('.', parts.Take(3)) : ruleId;
    }

    /// <summary>Short human title for a rule id, for report rendering.</summary>
    public static string RuleTitle(string ruleId) => BaseId(ruleId) switch
    {
        "fs.site.no_t_junction" => "T-Junction Line",
        "fs.site.calm_road" => "Road Speed and Chi",
        "fs.site.bright_hall" => "Bright Hall",
        "fs.site.unobstructed_facing" => "Facing Outlook",
        "fs.site.water_at_facing" => "Water at the Facing",
        "fs.site.settled_approach" => "Approach to the Entrance",
        "fs.site.armchair_backing" => "Armchair Backing",
        "fs.site.dry_back" => "Water Behind",
        "va.site.open_ne" => "Open North-East",
        "va.site.slope_ne" => "Slope Toward the North-East",
        "va.site.grounded_sw" => "Weight in the South-West",
        "va.site.slope_sw" => "Height in the South-West",
        "va.site.water_in_ne_quadrant" => "Water Placement",
        "va.site.approach_from_ne" => "Approach Direction",
        _ => ruleId,
    };

    private static Suggestion? RuleRemedy(string ruleId) => BaseId(ruleId) switch
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
        "va.site.open_ne" or "va.site.slope_ne" => new("Keep the north-east light",
            "Keep the north-east corner of the home uncluttered and well lit, with light colours and low furniture.", "low", "medium"),
        "va.site.grounded_sw" or "va.site.slope_sw" or "va.site.water_in_ne_quadrant" => new("Counterweight the south-west",
            "Keep the south-west corner of the home visually heavy — bookshelves, earthy colours, stone or ceramic decor.", "low", "medium"),
        _ => null,
    };
}

/// <summary>
/// Vastu's core — directional room placement and sleep orientation — cannot run without a
/// facing, and the leftovers are "Vastu with Vastu removed". This gate therefore <b>overrides
/// renormalization for Vastu</b> (design §2): a Vastu set with no resolved orientation is
/// <c>insufficient_evidence</c>, not a renormalized score. Feng Shui degrades through coverage.
/// </summary>
public static class VastuGate
{
    public static bool CanScore(string principleSet, SubjectOrientation? orientation) =>
        principleSet != PrincipleSets.Vastu || SiteAnalysisService.HasResolvedOrientation(orientation);

    public static bool CanScore(string principleSet, Cohort cohort) =>
        principleSet != PrincipleSets.Vastu || cohort.OrientationPath == Cohort.With;

    /// <summary>Builds the cohort a score will be ranked within.</summary>
    public static Cohort CohortFor(string evidencePath, SubjectOrientation? orientation) =>
        new(evidencePath, SiteAnalysisService.HasResolvedOrientation(orientation) ? Cohort.With : Cohort.Without);
}
