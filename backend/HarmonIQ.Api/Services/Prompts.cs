using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public static class Prompts
{
    /// <summary>
    /// Bumping this invalidates observations. Nothing else in the codebase defines a prompt version.
    /// </summary>
    public const string PromptVersion = "v2.0";

    /// <summary>
    /// Phrases that must never appear in any prompt or rule text (design §10 / NFR-8).
    /// </summary>
    public static readonly IReadOnlyList<string> BannedSuperlatives = new[]
    {
        "worst", "terrible", "cursed", "avoid this unit",
    };

    /// <summary>
    /// Tradition-agnostic room-photo system prompt. One vision call records every finding it can
    /// see, tagged with the tradition it belongs to ("fengshui", "vastu", or "both"); tradition
    /// FILTERING moves to score time, which is what lets a single call serve both principle sets
    /// and halves the model bill (design §2). This is the v2 contract — no `systems` parameter.
    /// </summary>
    public static string RoomSystemPrompt(string? orientationHint)
    {
        var orient = string.IsNullOrWhiteSpace(orientationHint) || orientationHint == "unknown"
            ? "The unit's entrance orientation is unknown — skip principles that require compass directions rather than guessing."
            : $"The unit's entrance faces {orientationHint} — you may apply directional principles relative to that.";
        return $"""
You are HarmonIQ, an expert consultant recording what a single apartment room photo shows, for
later grading against both Feng Shui (form school and Black Hat) and Vastu Shastra. Tag every
finding with the tradition it comes from: "fengshui", "vastu", or "both" when the finding is
shared by both traditions. Record every finding you can support from the image — do not decide
which tradition the renter cares about; that filtering happens later, downstream of this call.
{orient}

Hard rules:
- Reference ONLY what is actually visible in the photo. Never invent furniture, windows, or directions you cannot see.
- Findings to look for include: commanding position (bed/desk/stove), chi flow and clutter, five-element balance, mirror placement, bed under window or beam, under-bed storage, pairs and symmetry, natural light, poison arrows (sharp corners aimed at seating/bed); for Vastu: room-appropriate colors, heavy furniture placement, openness of the center, water element placement, sleep/work orientation.
- Tag every finding's tradition ("fengshui", "vastu", or "both") and give it a confidence between 0 and 1 reflecting how clearly the photo supports it.
- Estimate the five-element balance (wood/fire/earth/metal/water, each 0-100) only when the room supports a Feng Shui reading; omit it otherwise.
- Return 2-4 findings and 2-4 suggestions.
- Every suggestion must be renter-feasible: rearranging furniture, decor, plants, mirrors, textiles, lighting. Never structural work.
- Phrase observations concretely, naming the visible objects ("the wardrobe mirror directly faces the bed").
- Frame every tradition-based reading as belonging to that tradition, never as an objective claim about safety, health, or value. Never use negative superlatives — describe the configuration and the tradition's reading of it, without judging the unit itself.
- Record your analysis by calling the record_room_observation tool. If the room type was provided, keep it; otherwise identify it from the image.
""";
    }

    private static readonly object RoomFindingItem = new
    {
        type = "object",
        properties = new
        {
            ruleId = new { type = "string" },
            principle = new { type = "string" },
            observation = new { type = "string" },
            tradition = new { type = "string", @enum = new[] { "fengshui", "vastu", "both" } },
            confidence = new { type = "number", minimum = 0, maximum = 1 },
            severity = new { type = "string", @enum = new[] { "minor", "moderate", "major" } },
        },
        required = new[] { "ruleId", "principle", "observation", "tradition", "confidence" },
    };

    /// <summary>
    /// Forced tool `record_room_observation`. Tradition-agnostic: no `systems` parameter — every
    /// finding is self-tagged with its tradition. `elementBalance` is optional (Feng-Shui-only).
    /// </summary>
    public static readonly object RoomTool = new
    {
        name = "record_room_observation",
        description = "Record the structured, tradition-tagged observation of one room photo.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                roomType = new { type = "string" },
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
                findings = new { type = "array", minItems = 0, maxItems = 8, items = RoomFindingItem },
                suggestions = new
                {
                    type = "array", minItems = 0, maxItems = 4,
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
                coverage = new { type = "number", minimum = 0, maximum = 1 },
            },
            // Note: elementBalance is deliberately NOT required — it is Feng-Shui-only.
            required = new[] { "roomType", "findings", "suggestions", "coverage" },
        },
    };

    /// <summary>
    /// System prompt for the floor-plan lens. States the out-of-scope list as prohibitions and
    /// forbids inferring north — directional Vastu placement is a separate layer applied only
    /// when orientation is supplied externally.
    /// </summary>
    public static string FloorPlanSystemPrompt() => $"""
You are HarmonIQ, an expert consultant recording adjacency-only observations from a single
apartment floor-plan drawing, for later grading against Feng Shui and Vastu Shastra principles.

Scope — you may only report findings about these adjacency relationships:
- a bathroom adjacent to or directly over a kitchen
- a bathroom door opening onto a kitchen or dining area
- a straight sightline from the entry to the rear of the unit (chi rush)
- a toilet sharing a wall with the head of a bed
- an obstruction at the center of the unit (Brahmasthan) — report this ONLY when the full unit
  boundary is visible and clearly drawn; otherwise leave it unreported
- a kitchen positioned at or immediately inside the entry
- the wall options available for bed placement

Out of scope — do not report on any of the following, even if visible in the drawing:
- furniture or staging (what art, rugs, or decor are placed where)
- mirrors, beams, or clutter
- natural-light quality
- five-element balance
- anything dimensional (room sizes, distances, square footage)
- door swing direction
- any left/right (chirality-dependent) claim — floor plans are frequently mirrored for opposite
  building stacks, so every finding you record must be adjacency-only and hold true whichever
  way the plan is mirrored

Orientation rule: never infer compass direction or "north" from the drawing itself. Directional
Vastu placement is applied only as a separate downstream layer when an external orientation
source has already supplied the unit's facing — you do not determine or guess it here.

Other rules:
- Reference ONLY what the drawing actually shows. If the drawing does not let you evaluate a
  rule, omit that finding rather than guessing.
- If the drawing is illegible, incomplete, or otherwise does not support any adjacency finding,
  you may decline: set notDeterminable to true, give a notDeterminableReason, and return empty
  findings and suggestions arrays. A forced tool call is allowed to come back empty.
- Give every finding a confidence between 0 and 1 reflecting how clearly the drawing supports it.
- State your own coverage (0-1): how much of the rule catalogue this drawing actually let you evaluate.
- Frame every tradition-based reading as belonging to that tradition, never as an objective claim
  about safety, health, or value. Never use negative superlatives — describe the configuration
  and the tradition's reading of it, without judging the unit itself.
- Record your analysis by calling the record_floorplan_observation tool.
""";

    private static readonly object FloorPlanFindingItem = new
    {
        type = "object",
        properties = new
        {
            ruleId = new { type = "string", @enum = FloorPlanRules.AllowedRuleIds },
            principle = new { type = "string" },
            observation = new { type = "string" },
            tradition = new { type = "string", @enum = new[] { "fengshui", "vastu", "both" } },
            confidence = new { type = "number", minimum = 0, maximum = 1 },
            severity = new { type = "string", @enum = new[] { "minor", "moderate", "major" } },
        },
        required = new[] { "ruleId", "principle", "observation", "tradition", "confidence" },
    };

    /// <summary>
    /// Forced tool `record_floorplan_observation`. `findings`/`suggestions` allow zero items so a
    /// forced call can decline via `notDeterminable`. `ruleId` is drawn from the closed,
    /// adjacency-only enum in <see cref="FloorPlanRules"/>. `center_obstruction` may only be
    /// reported when `boundaryFullyDrawn` is true (enforced downstream by the deriving code —
    /// the schema cannot express that conditional, so the system prompt states it as a rule).
    /// </summary>
    public static readonly object FloorPlanTool = new
    {
        name = "record_floorplan_observation",
        description = "Record the structured, adjacency-only observation of one apartment floor plan. May decline via notDeterminable.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                notDeterminable = new { type = "boolean" },
                notDeterminableReason = new { type = "string" },
                boundaryFullyDrawn = new { type = "boolean" },
                findings = new { type = "array", minItems = 0, maxItems = 9, items = FloorPlanFindingItem },
                suggestions = new
                {
                    type = "array", minItems = 0, maxItems = 4,
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
                coverage = new { type = "number", minimum = 0, maximum = 1 },
            },
            required = new[] { "notDeterminable", "boundaryFullyDrawn", "findings", "suggestions", "coverage" },
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
