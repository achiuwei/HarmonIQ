using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services.Traditions;

/// <summary>
/// Shared rule-construction helpers for every tradition's site catalogue. Extracted from
/// <see cref="SiteAnalysisService"/> so the five catalogues build outcomes identically — the
/// unknown-is-not-a-violation invariant (FR-16) is enforced here once rather than per tradition.
/// </summary>
public static class SiteRules
{
    public static readonly string[] Sides = ["north", "east", "south", "west"];

    public static readonly string[] Compass =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    /// <summary>An environment value we can actually judge. Unknown/blank is never a violation.</summary>
    public static bool Known(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value != "unknown";

    /// <summary>A rule read from a single side's value. Unknown ⇒ not applicable, never a violation.</summary>
    public static RuleOutcome Simple(
        string id, string set, string value, Func<string, bool> satisfied, int severity, string text)
    {
        var applicable = Known(value);
        return new RuleOutcome(id, set, applicable, applicable && satisfied(value), severity, text);
    }

    /// <summary>
    /// A rule read from the facing (or rear, or flank) side(s). Applicable when at least one of
    /// those sides reports a known value; satisfied when every known one satisfies it. With no
    /// resolved facing there are no sides, so the rule is emitted not-applicable and coverage
    /// falls — the score is untouched.
    /// </summary>
    public static RuleOutcome Directional(
        string id, string set, string[] sides, Func<string, string> read,
        Func<string, bool> satisfied, int severity, string text)
    {
        var values = sides.Select(read).Where(Known).ToList();
        var applicable = values.Count > 0;
        return new RuleOutcome(id, set, applicable, applicable && values.All(satisfied), severity, text);
    }

    public static string Join(string[] sides) => string.Join("/", sides);

    /// <summary>Facing side(s): an intercardinal orientation touches two sides.</summary>
    public static string[] FrontSides(string? cardinal) => cardinal switch
    {
        "north" or "east" or "south" or "west" => [cardinal],
        "northeast" => ["north", "east"], "southeast" => ["south", "east"],
        "southwest" => ["south", "west"], "northwest" => ["north", "west"],
        _ => [],
    };

    public static string Opposite(string side) => side switch
    {
        "north" => "south", "south" => "north", "east" => "west", _ => "east",
    };

    /// <summary>
    /// The two sides perpendicular to the facing — the "flanks". Returned as an unordered pair:
    /// no tradition rule here may depend on which flank is left and which is right, matching the
    /// chirality prohibition the floor-plan lens already enforces.
    /// </summary>
    public static string[] FlankSides(string? cardinal)
    {
        var front = FrontSides(cardinal);
        if (front.Length == 0) return [];
        var axis = new HashSet<string>(front.Concat(front.Select(Opposite)), StringComparer.Ordinal);
        return Sides.Where(s => !axis.Contains(s)).ToArray();
    }

    /// <summary>Strips a per-side suffix so <c>fs.site.calm_road.north</c> titles as <c>fs.site.calm_road</c>.</summary>
    public static string BaseId(string ruleId)
    {
        var parts = ruleId.Split('.');
        return parts.Length > 3 ? string.Join('.', parts.Take(3)) : ruleId;
    }
}
