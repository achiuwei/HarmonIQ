namespace HarmonIQ.Api.Models;

/// <summary>
/// A single finding from the room lens, the floor-plan lens, or a tradition's interpretation.
///
/// <see cref="Tradition"/> is <b>empty for a stage-1 perception fact</b> — perception records what
/// is there and takes no view — and carries the tradition's id on a stage-3 interpretation finding.
/// Scoring reads the tagged ones; the untagged facts render on the report's room cards.
/// </summary>
/// <param name="Severity">
/// The magnitude of an adverse reading. Optional, and live reads often omit it — so it is a
/// weight, never the signal for whether the configuration is there at all.
/// </param>
/// <param name="Present">
/// Whether the drawing shows the configuration this <paramref name="RuleId"/> names. For the
/// adverse rules (nearly all of them) present means unsatisfied; for a positive-evidence rule it
/// means satisfied. Defaults to true because a model that files a finding against a rule is
/// reporting that rule's configuration unless it says otherwise — the opposite default would let
/// an omitted field silently clear a violation.
/// </param>
/// <param name="Satisfied">
/// The <b>interpretation</b> path's polarity signal: whether the home meets this principle as the
/// tradition reads it. Deliberately separate from <paramref name="Present"/>, which the floor-plan
/// path uses: <c>Present</c> resolves to a polarity only through the rule catalogue that knows
/// which rule ids name adverse configurations, and an interpretation's rule ids are free-form
/// prose ("sightline", "greenery"), so there is no catalogue to ask.
///
/// Null means a finding recorded before this field existed. Those fall back to the old
/// severity-inference at the point of use, which is wrong in a known direction — it reads every
/// legacy live finding as unsatisfied — rather than silently flipping stored history to satisfied.
/// </param>
public record LensFinding(
    string RuleId,
    string Principle,
    string Observation,
    string Tradition,
    double Confidence,
    string? Severity,
    bool Present = true,
    bool? Satisfied = null);

/// <summary>
/// Stage-1 perception for a single room photo: the plain facts the image shows, with no tradition
/// attached. <see cref="Materials"/> feeds each tradition's own element reading in stage 3 —
/// wǔxíng for the Sinitic traditions, pancha bhuta described in prose for Vastu.
///
/// <see cref="ElementBalance"/> remains for the demo/mock path, which supplies a precomputed
/// balance directly; the live perception pass leaves it null and lets stage 3 derive it.
/// </summary>
public record RoomObservation(
    string PhotoId,
    string RoomType,
    IReadOnlyList<LensFinding> Findings,
    IReadOnlyList<Suggestion> Suggestions,
    ElementBalance? ElementBalance,
    double Coverage,
    IReadOnlyList<string>? Materials = null);

/// <summary>
/// Stage-3 output: one tradition's reading of the whole subject, derived from the shared fact
/// sheet that every tradition sees. Subject-level rather than per-photo, because a tradition reads
/// the home as a whole — Kasō's kimon axis and Vastu's Brahmasthan are properties of the dwelling,
/// not of any one photograph.
/// </summary>
public record TraditionInterpretation(
    string PrincipleSet,
    IReadOnlyList<LensFinding> Findings,
    IReadOnlyList<Suggestion> Suggestions,
    ElementBalance? ElementBalance,
    double Coverage);

/// <summary>
/// Observation from the floor-plan lens. A forced tool call must still be able to decline
/// (NotDeterminable = true), which is why Findings/Suggestions allow zero items.
/// </summary>
public record FloorPlanObservation(
    bool NotDeterminable,
    string? NotDeterminableReason,
    bool BoundaryFullyDrawn,
    IReadOnlyList<LensFinding> Findings,
    IReadOnlyList<Suggestion> Suggestions,
    double Coverage);

/// <summary>
/// Closed enum of adjacency-only floor-plan rule ids. Single source of truth shared by the
/// FloorPlanTool JSON schema and the deriving/scoring code (Task 7).
/// </summary>
public static class FloorPlanRules
{
    public const string BathAdjacentKitchen = "bath_adjacent_kitchen";
    public const string BathOverKitchen = "bath_over_kitchen";
    public const string BathDoorOntoKitchenDining = "bath_door_onto_kitchen_dining";
    public const string BathDoorOntoDining = "bath_door_onto_dining";
    public const string EntryToRearStraightLine = "entry_to_rear_straight_line";
    public const string ToiletSharesBedHeadWall = "toilet_shares_bed_head_wall";
    public const string CenterObstruction = "center_obstruction";
    public const string KitchenAtEntry = "kitchen_at_entry";
    public const string BedWallOptions = "bed_wall_options";

    public static readonly IReadOnlyList<string> AllowedRuleIds = new[]
    {
        BathAdjacentKitchen,
        BathOverKitchen,
        BathDoorOntoKitchenDining,
        BathDoorOntoDining,
        EntryToRearStraightLine,
        ToiletSharesBedHeadWall,
        CenterObstruction,
        KitchenAtEntry,
        BedWallOptions,
    };
}
