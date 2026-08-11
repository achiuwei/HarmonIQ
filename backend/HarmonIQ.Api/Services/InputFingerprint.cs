using System.Security.Cryptography;
using System.Text;
using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services;

/// <summary>
/// SHA-256 over an <see cref="InputSet"/>'s snapshot fields plus a principle set and rules
/// version, stable field ordering, hex lowercase. Two callers:
/// <list type="bullet">
/// <item>Ingestion (<see cref="SubjectService.SnapshotAsync"/>) computes and stores
/// <see cref="InputSet.InputFingerprint"/> with empty principle-set/rules-version placeholders
/// - at snapshot time neither is known yet; this value only detects "did the evidence itself
/// change" (identical snapshots are stable; any changed input differs).</item>
/// <item>Scoring (Task 7's pipeline) calls this again with the real principle set and rules
/// version being derived, and stores the result on <c>Analysis.InputFingerprint</c> - the
/// backfill fingerprint check compares a fresh call against that stored value to decide whether
/// re-derivation is needed for that (subject, principle_set, rules_version).</item>
/// </list>
/// </summary>
public static class InputFingerprint
{
    private const string FieldDelimiter = "";

    public static string Compute(InputSet set, string principleSet, string rulesVersion)
    {
        var text = string.Join(FieldDelimiter, new[]
        {
            set.SubjectId,
            set.EvidencePath,
            set.EvidenceHashesJson,
            set.EnvironmentJson,
            set.OrientationJson ?? "",
            set.NumbersJson ?? "",
            principleSet ?? "",
            rulesVersion ?? "",
        });

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash);
    }
}
