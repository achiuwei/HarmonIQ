using System.Text;
using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public record RoomInput(string PhotoId, string? RoomType, byte[] ImageJpeg);

public class ClaudeAnalysisService(IClaudeClient claude, ILogger<ClaudeAnalysisService> log)
{
    public async Task<List<RoomAnalysis>> AnalyzeRoomsAsync(
        IReadOnlyList<RoomInput> rooms, string systems, string orientation, CancellationToken ct)
    {
        // One request per photo, fanned out (SPEC §5.3): stays under the proxy's 29 s gateway timeout.
        var results = await Task.WhenAll(rooms.Select(r => AnalyzeOneAsync(r, systems, orientation, ct)));
        return results.ToList();
    }

    private async Task<RoomAnalysis> AnalyzeOneAsync(
        RoomInput room, string systems, string orientation, CancellationToken ct)
    {
        var hint = string.IsNullOrWhiteSpace(room.RoomType) || room.RoomType == "Auto-detect"
            ? "Identify the room type from the image, then analyze it."
            : $"This photo is tagged as: {room.RoomType}. Analyze it as that room.";
        var resp = await claude.MessagesAsync(new
        {
            model = claude.Model,
            max_tokens = 4096,
            system = Prompts.RoomSystemPrompt(systems, orientation),
            tools = new[] { Prompts.RoomTool },
            tool_choice = new { type = "tool", name = "record_room_analysis" },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "image", source = new { type = "base64", media_type = "image/jpeg", data = Convert.ToBase64String(room.ImageJpeg) } },
                        new { type = "text", text = hint },
                    },
                },
            },
        }, ct);

        var input = resp.GetProperty("content").EnumerateArray()
            .First(c => c.GetProperty("type").GetString() == "tool_use")
            .GetProperty("input");
        return new RoomAnalysis(
            room.PhotoId,
            input.GetProperty("roomType").GetString() ?? room.RoomType ?? "Room",
            Math.Clamp(input.GetProperty("score").GetInt32(), 0, 100),
            input.GetProperty("elementBalance").Deserialize<ElementBalance>(Json.Options)!,
            input.GetProperty("adhering").Deserialize<List<Finding>>(Json.Options)!,
            input.GetProperty("violations").Deserialize<List<ViolationFinding>>(Json.Options)!,
            input.GetProperty("suggestions").Deserialize<List<Suggestion>>(Json.Options)!);
    }

    public async Task<string> SummarizeAsync(
        IReadOnlyList<RoomAnalysis> rooms, SiteAnalysis site, NumerologyResult numerology, CancellationToken ct)
    {
        try
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

            var resp = await claude.MessagesAsync(new
            {
                model = claude.Model,
                max_tokens = 300,
                messages = new object[] { new { role = "user", content = Prompts.SummaryPrompt(digest.ToString()) } },
            }, ct);
            var text = resp.GetProperty("content").EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("type").GetString() == "text");
            var s = text.ValueKind == JsonValueKind.Object ? text.GetProperty("text").GetString() : null;
            return string.IsNullOrWhiteSpace(s)
                ? ScoreMath.LocalSummary(rooms, site, numerology) : s.Trim();
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Summary call failed; using local summary");
            return ScoreMath.LocalSummary(rooms, site, numerology);
        }
    }

    // Live-mode polish of the deterministic site templates (SPEC §5.1). Any failure → input unchanged.
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
