using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public static class Prompts
{
    /// <summary>
    /// Bumping this invalidates observations. Nothing else in the codebase defines a prompt version.
    ///
    /// v3.0 — the perception/interpretation split. The room tool changed from
    /// <c>record_room_observation</c> (tradition-tagged findings + elementBalance) to
    /// <c>record_room_perception</c> (untagged facts + materials), so a v2.0 observation cannot be
    /// read against the new shape and MUST NOT be reused. This bump is what forces re-perception.
    /// </summary>
    public const string PromptVersion = "v3.0";

    /// <summary>
    /// Phrases that must never appear in any prompt or rule text (design §10 / NFR-8).
    /// </summary>
    public static readonly IReadOnlyList<string> BannedSuperlatives = new[]
    {
        "worst", "terrible", "cursed", "avoid this unit",
    };

    /// <summary>
    /// The stage-1 room-photo perception prompt. Tradition-agnostic and, unlike v2.0, it does not
    /// tag findings with a tradition at all — with five traditions the old
    /// <c>"fengshui" | "vastu" | "both"</c> tag has no meaning ("both" was a two-tradition
    /// encoding).
    ///
    /// This call records <b>what is physically there</b>; every tradition's reading of it happens
    /// in stage 3, where each culture has its own prompt over this same shared record. That keeps
    /// vision spend at 1× however many traditions are scored, and — more importantly — guarantees
    /// all five reason over identical evidence, so a score difference is attributable to the
    /// tradition rather than to what one call happened to notice.
    ///
    /// Because a fact this pass fails to record is unavailable to every interpreter downstream,
    /// the instruction is deliberately to over-record: note the fact even when its significance
    /// is unclear.
    /// </summary>
    public static string RoomPerceptionPrompt(string? orientationHint)
    {
        var orient = string.IsNullOrWhiteSpace(orientationHint) || orientationHint == "unknown"
            ? "The unit's entrance orientation is unknown. Do not guess or infer compass directions; describe positions relative to the room itself."
            : $"The unit's entrance faces {orientationHint}. Where you can place something relative to that facing, say so.";
        return $"""
You are HarmonIQ's perception pass. Record what a single apartment room photo physically shows.

You are NOT evaluating the room. Do not say whether anything is auspicious, harmonious, good, or
bad, and do not name or apply any tradition. Five different cultural traditions will each read your
record afterwards, and each must be free to draw its own conclusion from it.
{orient}

Record, as plainly and concretely as you can:
- Room type, and the furniture present with its position relative to the room's door(s) and window(s)
  ("the bed's headboard is against the wall shared with the door, foot pointing at the window").
- Sightlines: what is directly in line with the door; whether an unobstructed line runs from the
  door through the room to a window or another door.
- Bed, desk, and stove placement specifically: what each faces, what is behind it, whether the
  door is visible from it, whether anything overhangs it (beam, shelf, sloped ceiling).
- Mirrors and other reflective surfaces: size, what each faces.
- Adjacencies you can actually see: which rooms open onto which; whether a bathroom or kitchen is
  visible from where someone sleeps or eats.
- Sharp corners, exposed beams, columns, or hard edges, and what they point toward.
- Clutter, visible storage, and whether circulation paths are blocked; under-bed storage.
- Natural light: how many windows, their size, and how much of the room reads as lit.
- Dominant materials, colours, and shapes (wood, metal, glass, stone, textile; round, angular).
- Whether furnishings are paired or symmetrical.
- The centre of the room: whether it is open or occupied.

Hard rules:
- Reference ONLY what is actually visible. Never invent furniture, windows, adjacencies, or
  directions you cannot see. Omission is always better than a guess.
- Record a fact even when you are unsure whether it matters. A fact you leave out cannot be
  recovered by any later pass.
- Give every observation a confidence between 0 and 1 reflecting how clearly the photo supports it.
- Do not use evaluative language. "The mirror faces the foot of the bed" — not "the mirror
  unfortunately faces the bed".
- Record your observations by calling the record_room_perception tool. If the room type was
  provided, keep it; otherwise identify it from the image.
""";
    }

    /// <summary>
    /// A stage-1 fact. Deliberately carries <b>no</b> <c>tradition</c> field: perception records
    /// what is there, and every tradition reads the same record in stage 3. The old
    /// <c>"fengshui" | "vastu" | "both"</c> enum was a two-tradition encoding with no meaning
    /// across five.
    /// </summary>
    private static readonly object RoomFactItem = new
    {
        type = "object",
        properties = new
        {
            ruleId = new { type = "string" },
            principle = new { type = "string" },
            observation = new { type = "string" },
            confidence = new { type = "number", minimum = 0, maximum = 1 },
        },
        required = new[] { "ruleId", "principle", "observation", "confidence" },
    };

    /// <summary>
    /// Forced tool `record_room_perception`. Facts only — no tradition tags, no severities (a
    /// severity is a judgement, and this pass makes none), and no `elementBalance`: wǔxíng is a
    /// reading, not an observation, and Vastu's pancha bhuta are a different five entirely, so
    /// each tradition derives its own in stage 3 from the recorded materials and colours.
    /// </summary>
    public static readonly object RoomPerceptionTool = new
    {
        name = "record_room_perception",
        description = "Record the plain, non-evaluative facts one room photo shows.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
                roomType = new { type = "string" },
                facts = new { type = "array", minItems = 0, maxItems = 20, items = RoomFactItem },
                materials = new
                {
                    type = "array", maxItems = 12,
                    items = new { type = "string" },
                    description = "Dominant materials, colours, and shapes, e.g. \"pale oak floor\", \"black metal frames\", \"round glass table\".",
                },
                coverage = new { type = "number", minimum = 0, maximum = 1 },
            },
            required = new[] { "roomType", "facts", "coverage" },
        },
    };

    /// <summary>
    /// Forced tool `record_interpretation` — the stage-3 output, shared by all five traditions.
    ///
    /// The <b>schema</b> is common on purpose so scores stay comparable; what differs per culture
    /// is the prompt driving it (<c>ITradition.InterpretPrompt</c>). <c>elementBalance</c> is
    /// optional because only the wǔxíng traditions report one; Vastu omits it rather than zeroing.
    /// </summary>
    public static readonly object InterpretationTool = new
    {
        name = "record_interpretation",
        description = "Record one tradition's reading of the shared fact sheet.",
        input_schema = new
        {
            type = "object",
            properties = new
            {
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
                findings = new
                {
                    type = "array", minItems = 0, maxItems = 8,
                    items = new
                    {
                        type = "object",
                        properties = new
                        {
                            ruleId = new { type = "string" },
                            principle = new { type = "string" },
                            observation = new { type = "string" },
                            confidence = new { type = "number", minimum = 0, maximum = 1 },
                            severity = new { type = "string", @enum = new[] { "minor", "moderate", "major" } },
                        },
                        required = new[] { "ruleId", "principle", "observation", "confidence" },
                    },
                },
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
            // elementBalance is deliberately NOT required — only wǔxíng traditions report one.
            required = new[] { "findings", "suggestions", "coverage" },
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
- Set `present` on every finding: true when the drawing SHOWS the configuration the ruleId names,
  false when you checked and the drawing rules it out. Both are useful readings — report the ones
  you can actually see either way, and do not leave `present` to be inferred from `severity`.
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
            // "both" predates the five-tradition model and now means "shared by every tradition".
            // It stays because floor-plan findings are adjacency facts, which genuinely are shared,
            // and because observations recorded under the two-tradition contract still carry it.
            tradition = new
            {
                type = "string",
                @enum = new[] { "both", "fengshui", "vastu", "pungsu", "kaso", "phongthuy" },
            },
            confidence = new { type = "number", minimum = 0, maximum = 1 },
            // Required, and the sole polarity signal. `severity` is optional and often omitted,
            // so it grades magnitude only — it can never be what decides whether the drawing
            // shows the configuration at all.
            present = new
            {
                type = "boolean",
                description =
                    "True when the drawing SHOWS the configuration this ruleId names; false when "
                    + "you looked and the drawing rules it out.",
            },
            severity = new { type = "string", @enum = new[] { "minor", "moderate", "major" } },
        },
        required = new[] { "ruleId", "principle", "observation", "tradition", "confidence", "present" },
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
