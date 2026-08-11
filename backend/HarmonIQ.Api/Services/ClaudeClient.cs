using System.Net;
using System.Text;
using System.Text.Json;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

public class ClaudeUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public interface IClaudeClient
{
    bool IsConfigured { get; }
    string Model { get; }
    Task<JsonElement> MessagesAsync(object payload, CancellationToken ct = default);
}

public class ClaudeClient(HttpClient http, IConfiguration cfg, ILogger<ClaudeClient> log) : IClaudeClient
{
    public bool IsConfigured =>
        !string.IsNullOrEmpty(cfg["Claude:ApiKey"]) && !string.IsNullOrEmpty(cfg["Claude:BaseUrl"]);
    public string Model => cfg["Claude:Model"] is { Length: > 0 } m ? m : "claude-sonnet-5";

    public async Task<JsonElement> MessagesAsync(object payload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new ClaudeUnavailableException("Claude API key / base URL not configured.");
        var url = $"{cfg["Claude:BaseUrl"]!.TrimEnd('/')}/v1/messages";
        var body = JsonSerializer.Serialize(payload, Json.Options);

        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage resp;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url);
                req.Headers.Add("x-api-key", cfg["Claude:ApiKey"]);
                req.Headers.Add("anthropic-version", "2023-06-01");
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                resp = await http.SendAsync(req, ct);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new ClaudeUnavailableException("Claude endpoint unreachable.", e);
            }

            using (resp)
            {
                if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    throw new ClaudeUnavailableException($"Claude key rejected ({(int)resp.StatusCode}).");
                if (((int)resp.StatusCode == 429 || (int)resp.StatusCode >= 500) && attempt <= 3)
                {
                    log.LogWarning("Claude {Status}, retry {Attempt}/3", (int)resp.StatusCode, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct); // linear backoff: 2s, 4s, 6s
                    continue;
                }
                var text = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                    throw new ClaudeUnavailableException($"Claude error {(int)resp.StatusCode}: {Truncate(text)}");
                using var doc = JsonDocument.Parse(text);
                return doc.RootElement.Clone();
            }
        }
    }

    private static string Truncate(string s) => s.Length <= 300 ? s : s[..300];
}
