using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// The floor-plan lens: exactly <b>one</b> tradition-agnostic vision call over the plan image,
/// serving <b>both</b> principle sets. Tradition filtering happens at score time
/// (<see cref="AnalysisDerivation"/>), never at prompt time — that is what halves the model bill
/// against a per-set call (design §2).
///
/// A forced tool call must still be able to decline: <see cref="FloorPlanObservation.NotDeterminable"/>
/// drives the interiors lens to <c>Coverage = 0</c>, which flows through the confidence floor to
/// <c>insufficient_evidence</c> — never to a low score.
/// </summary>
public interface IFloorPlanLens
{
    Task<FloorPlanObservation> ReadAsync(Subject subject, byte[] planImage, bool live, CancellationToken ct);
}

/// <summary>
/// The adjacency-only rule catalogue the floor-plan path is scored against. Closed set — the ids
/// come from <see cref="FloorPlanRules.AllowedRuleIds"/>, which is also the JSON-schema enum, so a
/// model cannot invent a rule. Each entry carries the severity weight the rule earns in the
/// normalized fraction and the sentence shown when the plan does <i>not</i> show the configuration.
/// Every sentence names the tradition doing the reading and carries no negative superlative
/// (design §10).
/// </summary>
public static class FloorPlanRuleCatalogue
{
    /// <param name="PositiveEvidence">
    /// True for the one rule that is judged from the <i>presence</i> of a favourable finding rather
    /// than from the absence of an adverse one. With no finding at all it is <b>not applicable</b>
    /// — a drawing that never mentioned bed walls has not failed the rule.
    /// </param>
    public record Entry(string RuleId, string Title, int Severity, string SatisfiedText, bool PositiveEvidence = false);

    private static readonly IReadOnlyDictionary<string, Entry> Entries =
        new Dictionary<string, Entry>(StringComparer.Ordinal)
        {
            [FloorPlanRules.BathAdjacentKitchen] = new(
                FloorPlanRules.BathAdjacentKitchen, "Water Room Beside the Cooking Zone", 2,
                "The drawing keeps the bathroom off the kitchen wall — the separation of the water room from the cooking zone that Feng Shui and Vastu Shastra both look for."),
            [FloorPlanRules.BathOverKitchen] = new(
                FloorPlanRules.BathOverKitchen, "Bathroom Above the Kitchen", 2,
                "Nothing of the water zone sits above the kitchen footprint on this stack, which is how Vastu Shastra reads a settled cooking zone."),
            [FloorPlanRules.BathDoorOntoKitchenDining] = new(
                FloorPlanRules.BathDoorOntoKitchenDining, "Bathroom Door onto the Kitchen", 1,
                "The bathroom door opens onto circulation space rather than onto the kitchen, which is the arrangement form-school Feng Shui describes."),
            [FloorPlanRules.BathDoorOntoDining] = new(
                FloorPlanRules.BathDoorOntoDining, "Bathroom Door onto the Dining Area", 1,
                "The bathroom door opens away from the dining area, keeping the water room and the eating place apart as form-school Feng Shui prefers."),
            [FloorPlanRules.EntryToRearStraightLine] = new(
                FloorPlanRules.EntryToRearStraightLine, "Entry to Rear Sightline", 3,
                "The run from the entry to the rear of the unit is broken rather than straight, so in form-school Feng Shui arriving chi has somewhere to settle."),
            [FloorPlanRules.ToiletSharesBedHeadWall] = new(
                FloorPlanRules.ToiletSharesBedHeadWall, "Toilet on the Bed-Head Wall", 2,
                "No toilet sits behind a wall the bed head would use, which is the placement Feng Shui and Vastu Shastra both describe for restful sleep."),
            [FloorPlanRules.CenterObstruction] = new(
                FloorPlanRules.CenterObstruction, "Open Centre (Brahmasthan)", 2,
                "The centre of the drawn unit is free of structure — the open Brahmasthan Vastu Shastra reads as the heart of a home."),
            [FloorPlanRules.KitchenAtEntry] = new(
                FloorPlanRules.KitchenAtEntry, "Kitchen at the Threshold", 1,
                "The kitchen sits back from the entry rather than at the threshold, which is how Vastu Shastra places the cooking zone."),
            [FloorPlanRules.BedWallOptions] = new(
                FloorPlanRules.BedWallOptions, "Wall Options for the Bed Head", 2,
                "The plan offers a solid wall for the bed head that is clear of the door line — the supported headboard placement Feng Shui and Vastu Shastra both look for.",
                PositiveEvidence: true),
        };

    public static Entry For(string ruleId) =>
        Entries.TryGetValue(ruleId, out var e) ? e : new Entry(ruleId, ruleId, 1, string.Empty);

    /// <summary>
    /// The Brahmasthan rule can only be judged when the whole unit outline is drawn; on a partial
    /// drawing it is <b>not applicable</b> (unknown is never a violation).
    /// </summary>
    public static bool IsApplicable(string ruleId, bool boundaryFullyDrawn) =>
        ruleId != FloorPlanRules.CenterObstruction || boundaryFullyDrawn;
}

/// <summary>
/// Live path forces <c>record_floorplan_observation</c>; demo path reads
/// <c>Data/mock-floorplan-analysis.json</c> keyed on the <b>subject id</b> (there is no PhotoId on
/// the floor-plan path). Both paths run through the same sanitizer.
/// </summary>
public class FloorPlanLensService(
    IClaudeClient claude, MockAnalysisService mock, ILogger<FloorPlanLensService> log) : IFloorPlanLens
{
    public async Task<FloorPlanObservation> ReadAsync(
        Subject subject, byte[] planImage, bool live, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!live) return Sanitize(mock.ObservePlan(subject.Id), log);

        if (planImage is null || planImage.Length == 0)
            throw new InvalidOperationException($"No plan image bytes for subject '{subject.Id}'.");

        var resp = await claude.MessagesAsync(new
        {
            model = claude.Model,
            max_tokens = 4096,
            system = Prompts.FloorPlanSystemPrompt(),
            tools = new[] { Prompts.FloorPlanTool },
            tool_choice = new { type = "tool", name = "record_floorplan_observation" },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new { type = "base64", media_type = "image/png", data = Convert.ToBase64String(planImage) },
                        },
                        new { type = "text", text = "Record the adjacency-only observation for this floor plan." },
                    },
                },
            },
        }, ct);

        return Sanitize(Parse(resp), log);
    }

    private static FloorPlanObservation Parse(JsonElement resp)
    {
        var input = resp.GetProperty("content").EnumerateArray()
            .First(c => c.GetProperty("type").GetString() == "tool_use")
            .GetProperty("input");

        var findings = input.TryGetProperty("findings", out var f)
            ? f.Deserialize<List<LensFinding>>(Json.Options) ?? []
            : [];
        var suggestions = input.TryGetProperty("suggestions", out var s)
            ? s.Deserialize<List<Suggestion>>(Json.Options) ?? []
            : [];

        return new FloorPlanObservation(
            input.TryGetProperty("notDeterminable", out var nd) && nd.GetBoolean(),
            input.TryGetProperty("notDeterminableReason", out var ndr) ? ndr.GetString() : null,
            input.TryGetProperty("boundaryFullyDrawn", out var b) && b.GetBoolean(),
            findings,
            suggestions,
            input.TryGetProperty("coverage", out var c) ? Math.Clamp(c.GetDouble(), 0, 1) : 0);
    }

    /// <summary>
    /// Applies the two hard drops the schema cannot express: a rule id outside the closed
    /// adjacency-only enum, and <c>center_obstruction</c> on a plan whose boundary is not fully
    /// drawn. A declined read is normalized to zero coverage and empty lists so downstream cannot
    /// mistake it for a clean plan.
    /// </summary>
    public static FloorPlanObservation Sanitize(FloorPlanObservation obs, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(obs);
        if (obs.NotDeterminable)
            return obs with { Findings = [], Suggestions = [], Coverage = 0 };

        var kept = new List<LensFinding>(obs.Findings.Count);
        foreach (var finding in obs.Findings)
        {
            if (!FloorPlanRules.AllowedRuleIds.Contains(finding.RuleId, StringComparer.Ordinal))
            {
                log?.LogWarning("Floor-plan finding dropped: rule id '{RuleId}' is outside the allowed adjacency-only set.", finding.RuleId);
                continue;
            }
            if (!FloorPlanRuleCatalogue.IsApplicable(finding.RuleId, obs.BoundaryFullyDrawn))
            {
                log?.LogWarning("Floor-plan finding dropped: '{RuleId}' requires a fully drawn unit boundary.", finding.RuleId);
                continue;
            }
            kept.Add(finding with { Confidence = Math.Clamp(finding.Confidence, 0, 1) });
        }

        return obs with { Findings = kept, Coverage = Math.Clamp(obs.Coverage, 0, 1) };
    }
}
