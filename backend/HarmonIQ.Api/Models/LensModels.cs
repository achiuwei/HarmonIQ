namespace HarmonIQ.Api.Models;

/// <summary>
/// A single tagged finding from either the room lens or the floor-plan lens.
/// Tradition tagging happens at perception time; tradition FILTERING happens at score time
/// (see Prompts.RoomSystemPrompt — one vision call now serves both principle sets).
/// </summary>
public record LensFinding(
    string RuleId,
    string Principle,
    string Observation,
    string Tradition,
    double Confidence,
    string? Severity);

/// <summary>
/// Tradition-agnostic observation for a single room photo. ElementBalance is nullable
/// because it is Feng-Shui-only; Vastu-only or mixed-tradition observations omit it.
/// </summary>
public record RoomObservation(
    string PhotoId,
    string RoomType,
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
