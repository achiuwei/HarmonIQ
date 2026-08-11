using System.Text;
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

    // ---------------------------------------------------------------- v1 adapters (deleted by Task 11)

    /// <summary>
    /// v1 blended-analysis shape, kept only so the v1 <c>AnalysisController</c> (owned by Task 11)
    /// keeps compiling while the v2 API is built. Derived from the v2 observation — there is no
    /// second prompt or tool.
    /// </summary>
    [Obsolete("Use ObserveRoomsAsync(...) and the analysis pipeline. Removed with the v1 controller (Task 11).")]
    public async Task<List<RoomAnalysis>> AnalyzeRoomsAsync(
        IReadOnlyList<RoomInput> rooms, string systems, string orientation, CancellationToken ct)
    {
        var observations = await ObserveRoomsAsync(rooms, orientation, ct);
        return observations.Select(o =>
        {
            bool Fits(string t) => systems == "both" || t == "both" || t == systems;
            var kept = o.Findings.Where(f => Fits(f.Tradition)).ToList();
            var outcomes = kept.Select(f => new RuleOutcome(
                f.RuleId, systems, true, f.Severity is null,
                f.Severity switch { "major" => 3, "moderate" => 2, _ => 1 }, f.Observation)).ToList();
            var score = outcomes.Count == 0
                ? 70
                : (int)Math.Round(100 * RuleEvaluation.NormalizedScore(outcomes), MidpointRounding.AwayFromZero);
            return new RoomAnalysis(
                o.PhotoId, o.RoomType, Math.Clamp(score, 0, 100),
                o.ElementBalance ?? new ElementBalance(0, 0, 0, 0, 0),
                kept.Where(f => f.Severity is null)
                    .Select(f => new Finding(f.Principle, f.Observation, f.Tradition)).ToList(),
                kept.Where(f => f.Severity is not null)
                    .Select(f => new ViolationFinding(f.Principle, f.Observation, f.Severity!, f.Tradition)).ToList(),
                o.Suggestions.ToList());
        }).ToList();
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

    [Obsolete("Use SummarizeAsync(digest, fallback, ct). Removed with the v1 controller (Task 11).")]
    public async Task<string> SummarizeAsync(
        IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, NumerologyResult numerology, CancellationToken ct)
    {
        var digest = new StringBuilder();
        foreach (var r in rooms)
        {
            digest.AppendLine($"{r.RoomType} (score {r.Score}): " +
                $"strengths: {string.Join("; ", r.Adhering.Select(a => a.Principle))}. " +
                $"violations: {string.Join("; ", r.Violations.Select(v => $"{v.Principle} ({v.Severity})"))}.");
        }
        digest.AppendLine($"Site (score {site.Score}): " +
            $"strengths: {string.Join("; ", site.Adhering.Select(a => a.Principle))}. " +
            $"violations: {string.Join("; ", site.Violations.Select(v => $"{v.Principle} ({v.Severity})"))}.");
        digest.AppendLine($"Numerology adjustment {numerology.ScoreAdjustment}: " +
            string.Join("; ", numerology.Checks.Select(c => $"{c.Subject} {c.Value} {c.Verdict} ({c.Tradition})")));

        return await SummarizeAsync(digest.ToString(), ScoreMath.LocalSummary(rooms, site, numerology), ct);
    }

    // Live-mode polish of the deterministic site templates (SPEC §5.1). Any failure → input unchanged.
    [Obsolete("Report copy moves to ReportBodyWriter. Removed with the v1 controller (Task 11).")]
    public async Task<SiteAnalysis> RephraseSiteAsync(SiteAnalysis site, CancellationToken ct)
    {
        if (site.Adhering.Count + site.Violations.Count == 0) return site;
        try
        {
            var lines = site.Adhering.Select(a => a.Observation)
                .Concat(site.Violations.Select(v => v.Observation)).ToList();
            var resp = await claude.MessagesAsync(new
            {
                model = claude.Model,
                max_tokens = 1024,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = "Rewrite each numbered observation below in one warm, concrete sentence for a renter. " +
                                  "Keep the meaning exactly; keep cultural framing. Return the same count of lines, numbered.\n" +
                                  string.Join("\n", lines.Select((l, i) => $"{i + 1}. {l}")),
                    },
                },
            }, ct);
            var text = resp.GetProperty("content").EnumerateArray()
                .First(c => c.GetProperty("type").GetString() == "text").GetProperty("text").GetString() ?? "";
            var rewritten = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => System.Text.RegularExpressions.Regex.Replace(l.Trim(), @"^\d+[.)]\s*", ""))
                .ToList();
            if (rewritten.Count != lines.Count) return site;
            var a = site.Adhering.Select((f, i) => f with { Observation = rewritten[i] }).ToList();
            var v = site.Violations.Select((f, i) => f with { Observation = rewritten[site.Adhering.Count + i] }).ToList();
            return site with { Adhering = a, Violations = v };
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Site rephrase failed; keeping template phrasing");
            return site;
        }
    }
}
