namespace HarmonIQ.Api.Services;

public static class Prompts
{
    public static string RoomSystemPrompt(string systems, string orientation)
    {
        var tradition = systems switch
        {
            "fengshui" => "Feng Shui (form school and Black Hat) only. Tag every finding with system \"fengshui\".",
            "vastu" => "Vastu Shastra only. Tag every finding with system \"vastu\".",
            _ => "both Feng Shui and Vastu Shastra. Tag each finding with the system it comes from (\"fengshui\", \"vastu\", or \"both\" when shared).",
        };
        var orient = orientation == "unknown"
            ? "The unit's entrance orientation is unknown — skip principles that require compass directions rather than guessing."
            : $"The unit's entrance faces {orientation} — you may apply directional principles relative to that.";
        return $"""
You are HarmonIQ, an expert consultant grading apartment rooms against {tradition}
{orient}

Hard rules:
- Reference ONLY what is actually visible in the photo. Never invent furniture, windows, or directions you cannot see.
- Findings to look for include: commanding position (bed/desk/stove), chi flow and clutter, five-element balance, mirror placement, bed under window or beam, under-bed storage, pairs and symmetry, natural light, poison arrows (sharp corners aimed at seating/bed); for Vastu: room-appropriate colors, heavy furniture placement, openness of the center, water element placement, sleep/work orientation.
- Score the room 0-100 (100 = textbook harmony). Estimate the five-element balance (wood/fire/earth/metal/water, each 0-100) from visible materials and colors.
- Return 2-4 adhering findings, 0-4 violations (severity minor|moderate|major), and 2-4 suggestions.
- Every suggestion must be renter-feasible: rearranging furniture, decor, plants, mirrors, textiles, lighting. Never structural work.
- Phrase observations concretely, naming the visible objects ("the wardrobe mirror directly faces the bed").
- Record your analysis by calling the record_room_analysis tool. If the room type was provided, keep it; otherwise identify it from the image.
""";
    }

    private static readonly object FindingItems = new
    {
        type = "object",
        properties = new
        {
            principle = new { type = "string" },
            observation = new { type = "string" },
            system = new { type = "string", @enum = new[] { "fengshui", "vastu", "both" } },
        },
        required = new[] { "principle", "observation", "system" },
    };

    public static readonly object RoomTool = new
    {
        name = "record_room_analysis",
        description = "Record the structured harmony analysis of one room photo.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                roomType = new { type = "string" },
                score = new { type = "integer", minimum = 0, maximum = 100 },
                elementBalance = new
                {
                    type = "object",
                    properties = new
                    {
                        wood = new { type = "integer", minimum = 0, maximum = 100 },
                        fire = new { type = "integer", minimum = 0, maximum = 100 },
                        earth = new { type = "integer", minimum = 0, maximum = 100 },
                        metal = new { type = "integer", minimum = 0, maximum = 100 },
                        water = new { type = "integer", minimum = 0, maximum = 100 },
                    },
                    required = new[] { "wood", "fire", "earth", "metal", "water" },
                },
                adhering = new { type = "array", minItems = 2, maxItems = 4, items = FindingItems },
                violations = new
                {
                    type = "array", maxItems = 4,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            principle = new { type = "string" },
                            observation = new { type = "string" },
                            severity = new { type = "string", @enum = new[] { "minor", "moderate", "major" } },
                            system = new { type = "string", @enum = new[] { "fengshui", "vastu", "both" } },
                        },
                        required = new[] { "principle", "observation", "severity", "system" },
                    },
                },
                suggestions = new
                {
                    type = "array", minItems = 2, maxItems = 4,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            title = new { type = "string" },
                            detail = new { type = "string" },
                            effort = new { type = "string", @enum = new[] { "low", "medium", "high" } },
                            impact = new { type = "string", @enum = new[] { "low", "medium", "high" } },
                        },
                        required = new[] { "title", "detail", "effort", "impact" },
                    },
                },
            },
            required = new[] { "roomType", "score", "elementBalance", "adhering", "violations", "suggestions" },
        },
    };

    public static readonly object ClassifyTool = new
    {
        name = "classify_photos",
        description = "Classify each listing photo, in the order given.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                categories = new
                {
                    type = "array",
                    items = new { type = "string", @enum = new[] { "interior", "exterior", "floorplan", "amenity", "other" } },
                },
            },
            required = new[] { "categories" },
        },
    };

    public static string SummaryPrompt(string digest) => $"""
You are HarmonIQ. Below is the findings digest for one apartment listing (rooms, site, numerology).
Write a 2-3 sentence overall assessment for a renter: name the strongest asset and the single
highest-impact fix across all three lenses. Warm, concrete, no headers, no bullet points,
under 80 words. Frame tradition-based claims as tradition ("in Vastu terms...").

{digest}
""";
}
