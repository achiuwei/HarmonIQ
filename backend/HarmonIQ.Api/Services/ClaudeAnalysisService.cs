using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public record RoomInput(string PhotoId, string? RoomType, byte[] ImageJpeg);

/// <summary>
/// Live-mode perception and interpretation for the photo path.
///
/// <b>Stage 1</b> — one tradition-agnostic vision call per photo forces
/// <c>record_room_perception</c>, recording plain facts and taking no view.
/// <b>Stage 2</b> — those per-photo records are assembled into one shared fact sheet for the subject.
/// <b>Stage 3</b> — one text call per tradition reads that same fact sheet through that culture's
/// own prompt (<c>ITradition.InterpretPrompt</c>) and forces <c>record_interpretation</c>.
///
/// Vision spend therefore stays at 1× however many traditions are scored, and — the property that
/// actually matters for a listing page — all five reason over identical evidence, so a difference
/// between two traditions' scores is attributable to the traditions, not to what one call happened
/// to notice.
/// </summary>
public class ClaudeAnalysisService(IClaudeClient claude, ILogger<ClaudeAnalysisService> log)
{
    /// <summary>
    /// One tradition-agnostic observation per photo, fanned out (SPEC §5.3) so the batch stays
    /// under the proxy's 29 s gateway timeout.
    /// </summary>
    public async Task<IReadOnlyList<RoomObservation>> ObserveRoomsAsync(
        IReadOnlyList<RoomInput> rooms, string? orientationHint, CancellationToken ct)
    {
        var results = await Task.WhenAll(rooms.Select(r => ObserveRoomAsync(r, orientationHint, ct)));
        return results;
    }

    public async Task<RoomObservation> ObserveRoomAsync(
        RoomInput room, string? orientationHint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(room);
        var hint = string.IsNullOrWhiteSpace(room.RoomType) || room.RoomType == "Auto-detect"
            ? "Identify the room type from the image, then record what it shows."
            : $"This photo is tagged as: {room.RoomType}. Record it as that room.";

        var resp = await claude.MessagesAsync(new
        {
            model = claude.Model,
            max_tokens = 4096,
            system = Prompts.RoomPerceptionPrompt(orientationHint),
            tools = new[] { Prompts.RoomPerceptionTool },
            tool_choice = new { type = "tool", name = "record_room_perception" },
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
                            source = new { type = "base64", media_type = "image/jpeg", data = Convert.ToBase64String(room.ImageJpeg) },
                        },
                        new { type = "text", text = hint },
                    },
                },
            },
        }, ct);

        var input = resp.GetProperty("content").EnumerateArray()
            .First(c => c.GetProperty("type").GetString() == "tool_use")
            .GetProperty("input");

        // Perception emits untagged facts. They deserialize into LensFinding with an empty
        // Tradition, which scoring skips and the report's room cards still render.
        var facts = input.TryGetProperty("facts", out var f)
            ? f.Deserialize<List<LensFinding>>(Json.Options) ?? []
            : [];

        return new RoomObservation(
            room.PhotoId,
            input.TryGetProperty("roomType", out var rt) ? rt.GetString() ?? room.RoomType ?? "Room" : room.RoomType ?? "Room",
            facts,
            [], // suggestions are a reading, not an observation — stage 3 produces them
            null, // wǔxíng likewise: each tradition derives its own from Materials
            input.TryGetProperty("coverage", out var c) ? Math.Clamp(c.GetDouble(), 0, 1) : 0,
            input.TryGetProperty("materials", out var m) ? m.Deserialize<List<string>>(Json.Options) : null);
    }

    /// <summary>
    /// Stage 2 — assembles every perception into the one fact sheet all traditions will read.
    /// Deliberately plain JSON: it is model input, and a stable shape keeps the five interpretation
    /// prompts cacheable against a common prefix.
    /// </summary>
    public static string BuildFactSheet(
        IReadOnlyList<RoomObservation> rooms, FloorPlanObservation? plan, string? orientationHint) =>
        JsonSerializer.Serialize(
            new
            {
                orientation = orientationHint ?? "unknown",
                rooms = rooms.Select(r => new
                {
                    photoId = r.PhotoId,
                    roomType = r.RoomType,
                    materials = r.Materials ?? [],
                    coverage = r.Coverage,
                    facts = r.Findings.Select(x => new { x.RuleId, x.Principle, x.Observation, x.Confidence }),
                }),
                floorPlan = plan is null
                    ? null
                    : new
                    {
                        notDeterminable = plan.NotDeterminable,
                        plan.NotDeterminableReason,
                        plan.BoundaryFullyDrawn,
                        coverage = plan.Coverage,
                        facts = plan.Findings.Select(x => new { x.RuleId, x.Principle, x.Observation, x.Confidence }),
                    },
            },
            Json.Options);

    /// <summary>
    /// Stage 3 — one tradition's reading of the shared fact sheet. Text-only: the images were
    /// already read in stage 1, so adding a tradition costs no additional vision spend.
    /// </summary>
    public async Task<TraditionInterpretation> InterpretAsync(
        Traditions.ITradition tradition, string factSheet, string? orientationHint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tradition);

        var resp = await claude.MessagesAsync(new
        {
            model = claude.Model,
            max_tokens = 4096,
            system = tradition.InterpretPrompt(factSheet, orientationHint),
            tools = new[] { Prompts.InterpretationTool },
            tool_choice = new { type = "tool", name = "record_interpretation" },
            messages = new object[]
            {
                new { role = "user", content = $"Read the fact sheet through {tradition.DisplayName} and record your interpretation." },
            },
        }, ct);

        var input = resp.GetProperty("content").EnumerateArray()
            .First(c => c.GetProperty("type").GetString() == "tool_use")
            .GetProperty("input");

        // Only the wǔxíng traditions report a balance; the rest omit the section rather than zero it.
        ElementBalance? elements = tradition.UsesWuxing
            && input.TryGetProperty("elementBalance", out var eb)
            && eb.ValueKind == JsonValueKind.Object
            ? eb.Deserialize<ElementBalance>(Json.Options)
            : null;
        if (elements is { IsAllZero: true }) elements = null;

        var findings = input.TryGetProperty("findings", out var f)
            ? f.Deserialize<List<LensFinding>>(Json.Options) ?? []
            : [];

        return new TraditionInterpretation(
            tradition.Id,
            // The model is not asked to tag its own output — it read one tradition's prompt, so
            // the tag is known here and stamping it removes a way for it to be wrong.
            findings.Select(x => x with { Tradition = tradition.Id }).ToList(),
            input.TryGetProperty("suggestions", out var s) ? s.Deserialize<List<Suggestion>>(Json.Options) ?? [] : [],
            elements,
            input.TryGetProperty("coverage", out var c) ? Math.Clamp(c.GetDouble(), 0, 1) : 0);
    }

    /// <summary>
    /// Stage 3 for every requested tradition, fanned out. Each call is independent and reads the
    /// same fact sheet, so they run concurrently.
    /// </summary>
    public async Task<IReadOnlyList<TraditionInterpretation>> InterpretAllAsync(
        IEnumerable<Traditions.ITradition> traditions, string factSheet, string? orientationHint, CancellationToken ct) =>
        await Task.WhenAll(traditions.Select(t => InterpretAsync(t, factSheet, orientationHint, ct)));

    /// <summary>
    /// Lazy narrative for the report drawer. Never on the scoring path — the pipeline stores the
    /// deterministic <see cref="LocalSummary"/> so an engine bump needs no model call.
    /// </summary>
    public async Task<string> SummarizeAsync(string digest, string fallback, CancellationToken ct)
    {
        try
        {
            var resp = await claude.MessagesAsync(new
            {
                model = claude.Model,
                max_tokens = 300,
                messages = new object[] { new { role = "user", content = Prompts.SummaryPrompt(digest) } },
            }, ct);
            var text = resp.GetProperty("content").EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("type").GetString() == "text");
            var s = text.ValueKind == JsonValueKind.Object ? text.GetProperty("text").GetString() : null;
            return string.IsNullOrWhiteSpace(s) ? fallback : s.Trim();
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Summary call failed; using the deterministic local summary");
            return fallback;
        }
    }
}
