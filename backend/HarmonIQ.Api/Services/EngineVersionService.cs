using System.Security.Cryptography;
using System.Text;
using HarmonIQ.Api.Models;
using HarmonIQ.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Mints and looks up <see cref="EngineVersion"/> rows. The current engine identity is the
/// 4-tuple (RulesVersionFengshui, RulesVersionVastu, PromptVersion, ModelId); a change in any
/// of the four hashes to a different short version string, so an engine bump is "a new row
/// appears", never "an old row changes underneath a reader".
/// </summary>
public interface IEngineVersionService
{
    /// <summary>
    /// Returns the <see cref="EngineVersion"/> matching the currently configured engine
    /// identity, creating it (unpublished) if this is the first time this exact identity has
    /// been seen. Never mutates an existing row.
    /// </summary>
    Task<EngineVersion> GetOrCreateCurrentAsync(CancellationToken ct);

    /// <summary>The most recently published version, or null if nothing has ever been published.</summary>
    Task<EngineVersion?> GetPublishedAsync(CancellationToken ct);

    /// <summary>Looks up a specific version by its version string, published or not.</summary>
    Task<EngineVersion?> GetAsync(string version, CancellationToken ct);
}

public class EngineVersionService(HarmonIQDbContext db, IConfiguration config) : IEngineVersionService
{
    public async Task<EngineVersion> GetOrCreateCurrentAsync(CancellationToken ct)
    {
        var (rulesFengshui, rulesVastu, promptVersion, modelId) = CurrentIdentity();
        var version = ComputeVersion(rulesFengshui, rulesVastu, promptVersion, modelId);

        var existing = await db.EngineVersions.FindAsync([version], ct);
        if (existing is not null)
        {
            return existing;
        }

        var engineVersion = new EngineVersion
        {
            Version = version,
            RulesVersionFengshui = rulesFengshui,
            RulesVersionVastu = rulesVastu,
            PromptVersion = promptVersion,
            ModelId = modelId,
            CreatedAt = DateTimeOffset.UtcNow,
            PublishedAt = null,
        };

        db.EngineVersions.Add(engineVersion);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Lost a race with a concurrent creator of the same identity; the row now exists.
            db.Entry(engineVersion).State = EntityState.Detached;
            var raced = await db.EngineVersions.FindAsync([version], ct);
            if (raced is not null)
            {
                return raced;
            }
            throw;
        }

        return engineVersion;
    }

    public async Task<EngineVersion?> GetPublishedAsync(CancellationToken ct)
    {
        // SQLite cannot ORDER BY a DateTimeOffset server-side, and the published set is tiny
        // (one row per engine version), so pick the latest in memory.
        var published = await db.EngineVersions
            .Where(e => e.PublishedAt != null)
            .ToListAsync(ct);
        return published.MaxBy(e => e.PublishedAt);
    }

    public Task<EngineVersion?> GetAsync(string version, CancellationToken ct) =>
        db.EngineVersions.FirstOrDefaultAsync(e => e.Version == version, ct);

    private (string RulesFengshui, string RulesVastu, string PromptVersion, string ModelId) CurrentIdentity()
    {
        var modelId = config["Claude:Model"] is { Length: > 0 } configured ? configured : "claude-sonnet-5";
        return (SiteAnalysisService.RulesVersionFengShui, SiteAnalysisService.RulesVersionVastu, Prompts.PromptVersion, modelId);
    }

    /// <summary>SHA-256 over the 4-tuple, stable ordering, hex lowercase, truncated for readability.</summary>
    public static string ComputeVersion(string rulesFengshui, string rulesVastu, string promptVersion, string modelId)
    {
        var input = string.Join('|', rulesFengshui, rulesVastu, promptVersion, modelId);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }
}
