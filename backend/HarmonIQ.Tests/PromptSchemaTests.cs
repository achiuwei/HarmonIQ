using System.Text.Json;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Services;

namespace HarmonIQ.Tests;

public class PromptSchemaTests
{
    private static JsonElement Serialize(object tool) =>
        JsonDocument.Parse(JsonSerializer.Serialize(tool, Json.Options)).RootElement;

    private static IEnumerable<JsonElement> EnumerateProperties(JsonElement schemaProperties)
    {
        foreach (var prop in schemaProperties.EnumerateObject())
            yield return prop.Value;
    }

    // ---- FloorPlanTool ----

    [Fact]
    public void FloorPlanTool_FindingsAndSuggestions_AllowZeroItems()
    {
        var root = Serialize(Prompts.FloorPlanTool);
        var schema = root.GetProperty("input_schema").GetProperty("properties");

        Assert.Equal(0, schema.GetProperty("findings").GetProperty("minItems").GetInt32());
        Assert.Equal(0, schema.GetProperty("suggestions").GetProperty("minItems").GetInt32());
    }

    [Fact]
    public void FloorPlanTool_HasNotDeterminableMarker()
    {
        var root = Serialize(Prompts.FloorPlanTool);
        var schema = root.GetProperty("input_schema");
        var properties = schema.GetProperty("properties");

        Assert.Equal("boolean", properties.GetProperty("notDeterminable").GetProperty("type").GetString());
        Assert.True(properties.TryGetProperty("notDeterminableReason", out var reasonProp));
        Assert.Equal("string", reasonProp.GetProperty("type").GetString());

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("notDeterminable", required);
        // notDeterminableReason must stay optional — only required when declining, which the
        // schema cannot express conditionally.
        Assert.DoesNotContain("notDeterminableReason", required);
    }

    [Fact]
    public void FloorPlanTool_EveryFindingRequiresConfidence()
    {
        var root = Serialize(Prompts.FloorPlanTool);
        var findingItem = root.GetProperty("input_schema").GetProperty("properties")
            .GetProperty("findings").GetProperty("items");

        Assert.Equal("number", findingItem.GetProperty("properties").GetProperty("confidence").GetProperty("type").GetString());
        var required = findingItem.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("confidence", required);
    }

    [Fact]
    public void FloorPlanTool_RuleIdEnum_EqualsFloorPlanRulesAllowedRuleIds()
    {
        var root = Serialize(Prompts.FloorPlanTool);
        var findingItem = root.GetProperty("input_schema").GetProperty("properties")
            .GetProperty("findings").GetProperty("items");

        var ruleIdEnum = findingItem.GetProperty("properties").GetProperty("ruleId")
            .GetProperty("enum").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.Equal(FloorPlanRules.AllowedRuleIds.ToList(), ruleIdEnum);
    }

    [Fact]
    public void FloorPlanTool_HasBoundaryFullyDrawnFlag()
    {
        var root = Serialize(Prompts.FloorPlanTool);
        var schema = root.GetProperty("input_schema");
        var properties = schema.GetProperty("properties");

        Assert.Equal("boolean", properties.GetProperty("boundaryFullyDrawn").GetProperty("type").GetString());
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("boundaryFullyDrawn", required);
    }

    [Fact]
    public void FloorPlanTool_HasCoverage_ZeroToOne()
    {
        var root = Serialize(Prompts.FloorPlanTool);
        var coverage = root.GetProperty("input_schema").GetProperty("properties").GetProperty("coverage");

        Assert.Equal("number", coverage.GetProperty("type").GetString());
        Assert.Equal(0, coverage.GetProperty("minimum").GetDouble());
        Assert.Equal(1, coverage.GetProperty("maximum").GetDouble());
    }

    [Fact]
    public void FloorPlanTool_IsRecordFloorplanObservation()
    {
        var root = Serialize(Prompts.FloorPlanTool);
        Assert.Equal("record_floorplan_observation", root.GetProperty("name").GetString());
    }

    // ---- RoomTool (tradition-agnostic) ----

    [Fact]
    public void RoomTool_ElementBalance_NotInRequired()
    {
        var root = Serialize(Prompts.RoomTool);
        var schema = root.GetProperty("input_schema");
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.DoesNotContain("elementBalance", required);
        // still present as an optional property
        Assert.True(schema.GetProperty("properties").TryGetProperty("elementBalance", out _));
    }

    [Fact]
    public void RoomTool_HasNoSystemsParameter()
    {
        var root = Serialize(Prompts.RoomTool);
        var properties = root.GetProperty("input_schema").GetProperty("properties");

        Assert.False(properties.TryGetProperty("systems", out _));
        Assert.False(properties.TryGetProperty("system", out _));
    }

    [Fact]
    public void RoomTool_FindingsCarryRuleIdPrincipleObservationTraditionConfidence()
    {
        var root = Serialize(Prompts.RoomTool);
        var findingItem = root.GetProperty("input_schema").GetProperty("properties")
            .GetProperty("findings").GetProperty("items");

        var properties = findingItem.GetProperty("properties");
        Assert.True(properties.TryGetProperty("ruleId", out _));
        Assert.True(properties.TryGetProperty("principle", out _));
        Assert.True(properties.TryGetProperty("observation", out _));
        Assert.True(properties.TryGetProperty("tradition", out _));
        Assert.True(properties.TryGetProperty("confidence", out _));

        var traditionEnum = properties.GetProperty("tradition").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "fengshui", "vastu", "both" }, traditionEnum);

        var required = findingItem.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("confidence", required);
    }

    [Fact]
    public void RoomTool_IsRecordRoomObservation()
    {
        var root = Serialize(Prompts.RoomTool);
        Assert.Equal("record_room_observation", root.GetProperty("name").GetString());
    }

    // ---- Prompt text content ----

    [Fact]
    public void FloorPlanSystemPrompt_StatesMirroringProhibition()
    {
        var prompt = Prompts.FloorPlanSystemPrompt();
        Assert.Contains("mirrored", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("left/right", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FloorPlanSystemPrompt_StatesOutOfScopeList()
    {
        var prompt = Prompts.FloorPlanSystemPrompt();
        foreach (var term in new[] { "furniture", "mirrors", "beams", "clutter", "natural-light", "five-element", "dimensional", "door swing" })
        {
            Assert.Contains(term, prompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FloorPlanSystemPrompt_DoesNotInstructDerivingNorth()
    {
        var prompt = Prompts.FloorPlanSystemPrompt();
        // The prompt may mention compass words only to forbid inferring them, never to instruct
        // deriving/inferring/determining north from the drawing.
        var forbiddenPhrases = new[]
        {
            "infer north", "derive north", "determine north", "figure out north",
            "infer the north", "derive the north", "determine the compass",
        };
        foreach (var phrase in forbiddenPhrases)
        {
            Assert.DoesNotContain(phrase, prompt, StringComparison.OrdinalIgnoreCase);
        }
        // Must explicitly say it never infers north.
        Assert.Contains("never infer", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FloorPlanSystemPrompt_MentionsBrahmasthanOnlyWithBoundaryCaveat()
    {
        var prompt = Prompts.FloorPlanSystemPrompt();
        Assert.Contains("Brahmasthan", prompt);
        Assert.Contains("boundary", prompt, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Banned superlatives ----

    [Fact]
    public void NoPromptText_ContainsBannedSuperlatives()
    {
        var texts = new[]
        {
            Prompts.RoomSystemPrompt((string?)null),
            Prompts.RoomSystemPrompt("north"),
            Prompts.FloorPlanSystemPrompt(),
            Prompts.SummaryPrompt("digest"),
            JsonSerializer.Serialize(Prompts.RoomTool, Json.Options),
            JsonSerializer.Serialize(Prompts.FloorPlanTool, Json.Options),
        };

        foreach (var text in texts)
        {
            foreach (var banned in Prompts.BannedSuperlatives)
            {
                Assert.DoesNotContain(banned, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void PromptVersion_IsV2()
    {
        Assert.Equal("v2.0", Prompts.PromptVersion);
    }
}
