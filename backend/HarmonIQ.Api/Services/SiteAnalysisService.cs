using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class SiteAnalysisService
{
    private static readonly string[] Sides = ["north", "east", "south", "west"];

    public SiteAnalysis Analyze(ListingEnvironment? env, string orientation, string systems)
    {
        env ??= ListingEnvironment.AllUnknown;
        var adhering = new List<Finding>();
        var violations = new List<ViolationFinding>();
        var suggestions = new List<Suggestion>();
        bool Fs() => systems is "both" or "fengshui";
        bool Va() => systems is "both" or "vastu";

        var front = FrontSides(orientation);
        var back = front.Select(Opposite).ToArray();

        // --- Feng Shui: sha chi roads (any side; the road points at the building regardless of door) ---
        if (Fs())
            foreach (var s in Sides)
            {
                var road = env.Side(s).Road;
                if (road is "t-junction")
                {
                    violations.Add(new("T-Junction Facing the Building",
                        $"A T-junction on the {s} side aims a straight line of fast-moving energy (sha chi) directly at the building.",
                        "major", "fengshui"));
                    suggestions.Add(new("Screen the entrance line",
                        "Break the straight-on rush with a hedge, a pair of planters, or a screen inside the lobby/entry line; heavy curtains on windows facing that side also soften it.",
                        "low", "high"));
                }
                else if (road is "highway")
                {
                    violations.Add(new("Rushing Road (Sha Chi)",
                        $"A highway runs along the {s} side — fast, cutting energy in form-school terms (and real noise).",
                        "moderate", "fengshui"));
                    suggestions.Add(new("Soften the rushing side",
                        $"Dense plants and layered curtains on {s}-facing windows slow the visual rush and dampen noise.",
                        "low", "medium"));
                }
            }

        // --- Feng Shui: orientation-dependent rules ---
        if (Fs() && front.Length > 0)
        {
            foreach (var f in front)
            {
                var side = env.Side(f);
                if (side.Structures == "open")
                    adhering.Add(new("Bright Hall",
                        $"Open space to the {f} (the facing direction) forms a 'bright hall' where chi can gather before the entrance.",
                        "fengshui"));
                if (side.Structures == "taller-building")
                {
                    violations.Add(new("Overshadowed Facing",
                        $"A much taller structure to the {f} looms over the facing direction, pressing on the building's outlook.",
                        "moderate", "fengshui"));
                    suggestions.Add(new("Lift the entry light",
                        "Brighten the entrance and front-facing rooms with warm lighting and a mirror placed to widen the view — not facing the door.",
                        "low", "medium"));
                }
                if (side.Water is not ("none" or "unknown"))
                    adhering.Add(new("Water at the Facing",
                        $"Water ({side.Water}) at the {f} front is classically auspicious — wealth gathers where water settles before the entrance.",
                        "fengshui"));
                if (side.Road == "busy")
                    violations.Add(new("Rushing Chi at the Entrance",
                        $"A busy road at the {f} front rushes chi past the entrance rather than letting it settle.",
                        "moderate", "fengshui"));
            }
            foreach (var b in back)
            {
                var side = env.Side(b);
                if (side.Structures is "taller-building" or "similar")
                    adhering.Add(new("Armchair Position",
                        $"Solid structures to the {b} give the building 'mountain' backing — the classic armchair arrangement.",
                        "fengshui"));
                else if (side.Structures == "open")
                {
                    violations.Add(new("Missing Backing",
                        $"Open ground to the {b} leaves the building without support behind — an exposed armchair.",
                        "minor", "fengshui"));
                    suggestions.Add(new("Weight the rear rooms",
                        "Place heavier furniture and earthy tones in rooms on the rear side to symbolically anchor the back.",
                        "low", "low"));
                }
                if (side.Water is not ("none" or "unknown"))
                    violations.Add(new("Water Behind",
                        $"Water ({side.Water}) behind the building ({b}) undermines its backing in form-school terms.",
                        "minor", "fengshui"));
            }
        }

        // --- Vastu: absolute-direction rules (no orientation needed) ---
        if (Va())
        {
            foreach (var s in new[] { "north", "east" })
            {
                var side = env.Side(s);
                if (side.Water is not ("none" or "unknown"))
                    adhering.Add(new("Water in the North/East",
                        $"A {side.Water} to the {s} sits in the auspicious water zone (toward NE, the zone of Jala).", "vastu"));
                if (side.Slope == "falls")
                    adhering.Add(new("Auspicious Slope",
                        $"Ground falling away to the {s} lets energy and water flow toward the favorable NE.", "vastu"));
                if (side.Slope == "rises")
                    violations.Add(new("Rising Slope in the North/East",
                        $"Ground rising to the {s} blocks the light, open NE quadrant.", "minor", "vastu"));
                if (side.Structures == "taller-building")
                    violations.Add(new("Mass in the North/East",
                        $"A taller structure to the {s} weighs down the quadrant Vastu keeps light and open.", "minor", "vastu"));
                if (side.Road is not ("none" or "unknown"))
                    adhering.Add(new("Approach from the North/East",
                        $"Road access on the {s} side is a favorable approach direction in Vastu.", "vastu"));
            }
            foreach (var s in new[] { "south", "west" })
            {
                var side = env.Side(s);
                if (side.Water is not ("none" or "unknown"))
                {
                    violations.Add(new("Water in the South/West",
                        $"A {side.Water} to the {s} places water in the quadrant Vastu reserves for weight and stability.",
                        "moderate", "vastu"));
                    suggestions.Add(new("Counterweight the south-west",
                        "Keep the SW corner of the home visually heavy — bookshelves, earthy colors, stone or ceramic decor.",
                        "low", "medium"));
                }
                if (side.Slope == "falls")
                {
                    violations.Add(new("Falling Slope in the South/West",
                        $"Ground falling away to the {s} drains support from the quadrant that should be highest.",
                        "moderate", "vastu"));
                    suggestions.Add(new("Anchor the south-west corner",
                        "Weight the SW rooms with the heaviest furniture and warm, dark tones.", "low", "medium"));
                }
                if (side.Slope == "rises")
                    adhering.Add(new("Higher Ground in the South/West",
                        $"Rising ground to the {s} gives the SW the height and weight Vastu favors.", "vastu"));
                if (side.Structures is "taller-building" or "similar")
                    adhering.Add(new("Mass in the South/West",
                        $"Substantial structures to the {s} provide the heaviness Vastu wants in the SW.", "vastu"));
            }
        }

        var score = Math.Clamp(
            70 + 5 * adhering.Count
               - violations.Sum(v => v.Severity switch { "major" => 18, "moderate" => 10, _ => 5 }),
            5, 98);
        return new SiteAnalysis(score, adhering, violations, suggestions);
    }

    // Facing side(s): intercardinal orientations touch two sides.
    private static string[] FrontSides(string orientation) => orientation switch
    {
        "north" or "east" or "south" or "west" => [orientation],
        "northeast" => ["north", "east"], "southeast" => ["south", "east"],
        "southwest" => ["south", "west"], "northwest" => ["north", "west"],
        _ => [],
    };

    private static string Opposite(string side) => side switch
    {
        "north" => "south", "south" => "north", "east" => "west", _ => "east",
    };
}
