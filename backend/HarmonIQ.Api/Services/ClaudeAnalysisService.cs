using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public record RoomInput(string PhotoId, string? RoomType, byte[] ImageJpeg);

/// <summary>
/// Live-mode perception for the photo path. One <b>tradition-agnostic</b> vision call per photo
/// forces <c>record_room_observation</c>; every finding is self-tagged with its tradition and
/// tradition FILTERING happens at score time, so a single call serves both principle sets
/// (design §2). There is no <c>systems</c> parameter on the perception contract any more.
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
            system = Prompts.RoomSystemPrompt(orientationHint),
            tools = new[] { Prompts.RoomTool },
            tool_choice = new { type = "tool", name = "record_room_observation" },
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

        // elementBalance is deliberately optional in the schema — it is Feng-Shui-only, and a
        // room that reported nothing must stay null rather than become five zero bars.
        ElementBalance? elements = input.TryGetProperty("elementBalance", out var eb)
            && eb.ValueKind == JsonValueKind.Object
            ? eb.Deserialize<ElementBalance>(Json.Options)
            : null;
        if (elements is { IsAllZero: true }) elements = null;

        return new RoomObservation(
            room.PhotoId,
            input.TryGetProperty("roomType", out var rt) ? rt.GetString() ?? room.RoomType ?? "Room" : room.RoomType ?? "Room",
            input.TryGetProperty("findings", out var f) ? f.Deserialize<List<LensFinding>>(Json.Options) ?? [] : [],
            input.TryGetProperty("suggestions", out var s) ? s.Deserialize<List<Suggestion>>(Json.Options) ?? [] : [],
            elements,
            input.TryGetProperty("coverage", out var c) ? Math.Clamp(c.GetDouble(), 0, 1) : 0);
    }

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
