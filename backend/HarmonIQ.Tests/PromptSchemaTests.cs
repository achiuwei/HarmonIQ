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

    // ---- RoomPerceptionTool (stage 1: facts, no tradition) ----

    [Fact]
    public void RoomPerceptionTool_IsRecordRoomPerception()
    {
        var root = Serialize(Prompts.RoomPerceptionTool);
        Assert.Equal("record_room_perception", root.GetProperty("name").GetString());
    }

    [Fact]
    public void RoomPerceptionTool_HasNoSystemsParameter()
    {
        var properties = Serialize(Prompts.RoomPerceptionTool)
            .GetProperty("input_schema").GetProperty("properties");

        Assert.False(properties.TryGetProperty("systems", out _));
        Assert.False(properties.TryGetProperty("system", out _));
    }

    /// <summary>
    /// Perception records facts and takes no view, so a fact carries no tradition and no severity
    /// (a severity is a judgement). With five traditions the old fengshui|vastu|both enum has no
    /// meaning - "both" was a two-tradition encoding.
    /// </summary>
    [Fact]
    public void RoomPerceptionTool_FactsCarryNoTraditionAndNoSeverity()
    {
        var factItem = Serialize(Prompts.RoomPerceptionTool)
            .GetProperty("input_schema").GetProperty("properties")
            .GetProperty("facts").GetProperty("items");

        var properties = factItem.GetProperty("properties");
        Assert.True(properties.TryGetProperty("ruleId", out _));
        Assert.True(properties.TryGetProperty("principle", out _));
        Assert.True(properties.TryGetProperty("observation", out _));
        Assert.True(properties.TryGetProperty("confidence", out _));

        Assert.False(properties.TryGetProperty("tradition", out _));
        Assert.False(properties.TryGetProperty("severity", out _));

        var required = factItem.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("confidence", required);
    }

    /// <summary>
    /// Wuxing is a reading, not an observation - and Vastu's pancha bhuta are a different five -
    /// so the perception pass must not emit an element balance at all.
    /// </summary>
    [Fact]
    public void RoomPerceptionTool_DoesNotEmitElementBalance()
    {
        var properties = Serialize(Prompts.RoomPerceptionTool)
            .GetProperty("input_schema").GetProperty("properties");

        Assert.False(properties.TryGetProperty("elementBalance", out _));
        Assert.True(properties.TryGetProperty("materials", out _));
    }

    // ---- InterpretationTool (stage 3: one tradition's reading) ----

    [Fact]
    public void InterpretationTool_IsRecordInterpretation()
    {
        var root = Serialize(Prompts.InterpretationTool);
        Assert.Equal("record_interpretation", root.GetProperty("name").GetString());
    }

    [Fact]
    public void InterpretationTool_ElementBalance_OptionalNotRequired()
    {
        var schema = Serialize(Prompts.InterpretationTool).GetProperty("input_schema");
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.DoesNotContain("elementBalance", required);
        Assert.True(schema.GetProperty("properties").TryGetProperty("elementBalance", out _));
    }

    /// <summary>
    /// The model reads exactly one tradition's prompt, so the tag is known by the caller and is
    /// stamped there. Asking the model to self-tag would only add a way for it to be wrong.
    /// </summary>
    [Fact]
    public void InterpretationTool_FindingsDoNotSelfTagTradition()
    {
        var properties = Serialize(Prompts.InterpretationTool)
            .GetProperty("input_schema").GetProperty("properties")
            .GetProperty("findings").GetProperty("items").GetProperty("properties");

        Assert.False(properties.TryGetProperty("tradition", out _));
        Assert.True(properties.TryGetProperty("severity", out _));
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
            Prompts.RoomPerceptionPrompt((string?)null),
            Prompts.RoomPerceptionPrompt("north"),
            Prompts.FloorPlanSystemPrompt(),
            Prompts.SummaryPrompt("digest"),
            JsonSerializer.Serialize(Prompts.RoomPerceptionTool, Json.Options),
            JsonSerializer.Serialize(Prompts.InterpretationTool, Json.Options),
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

    /// <summary>
    /// v3.0 is the perception/interpretation split. The room tool's shape changed incompatibly
    /// (tagged findings + elementBalance → untagged facts + materials), so v2.0 observations must
    /// not be reused; this version string is the only thing that forces re-perception.
    /// </summary>
    [Fact]
    public void PromptVersion_IsV3()
    {
        Assert.Equal("v3.0", Prompts.PromptVersion);
    }
}
