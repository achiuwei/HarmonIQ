using System;

namespace HarmonIQ.Api.Models;

/// <summary>
/// A scoreable subject: either a whole property (single-listing) or an individual
/// floor plan on a multi-plan property. Id is "{propertyKey}" for property subjects
/// and "{propertyKey}:{rentalKey}" for floor-plan subjects.
/// </summary>
public class Subject
{
    public string Id { get; set; } = string.Empty;
    public string PropertyKey { get; set; } = string.Empty;
    public string SubjectType { get; set; } = string.Empty; // "property" | "floorplan"
    public string? ExternalPlanKey { get; set; } // scraped data-rentalkey
    public string? PlanName { get; set; }
    public int? Beds { get; set; }
    public double? Baths { get; set; }
    public int? SqftMin { get; set; }
    public int? SqftMax { get; set; }
    public string? PlanImageUrl { get; set; }
    public string? PlanImageHash { get; set; }
    public string? ContentSignature { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>
/// Immutable input snapshot for a subject: evidence hashes, environment, orientation,
/// numbers. Written once by ingestion; never mutated. Scoring reads only the snapshot.
/// </summary>
public class InputSet
{
    public string Id { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string EvidencePath { get; set; } = string.Empty; // "photos" | "floorplan"
    public string EvidenceHashesJson { get; set; } = string.Empty;
    public string EnvironmentJson { get; set; } = string.Empty;
    public string? OrientationJson { get; set; }
    public string? NumbersJson { get; set; }
    public string InputFingerprint { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Raw tradition-agnostic model output (findings, element balance, layout reading).
/// Expensive; invalidated only by evidence/prompt/model change. Unique on
/// (SubjectId, EvidenceHash, PromptVersion, ModelId).
/// </summary>
public class Observation
{
    public string Id { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string InputSetId { get; set; } = string.Empty;
    public string EvidenceHash { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty; // "demo" | "live"
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Deterministic per-principle-set derivation from observations + site + numbers.
/// Cheap; an engine bump is batch SQL re-derivation. Unique on
/// (SubjectId, PrincipleSet, RulesVersion).
/// </summary>
public class Analysis
{
    public string Id { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string PrincipleSet { get; set; } = string.Empty; // "fengshui" | "vastu"
    public string RulesVersion { get; set; } = string.Empty; // per principle set
    public string EngineVersion { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "pending" | "ok" | "failed" | "insufficient_evidence"
    public int? Score { get; set; }
    public string? Grade { get; set; }
    public int? InteriorsScore { get; set; }
    public int? SiteScore { get; set; }
    public double? NumerologyAdjustment { get; set; }
    public double? InteriorsCoverage { get; set; }
    public double? SiteCoverage { get; set; }
    public double? Confidence { get; set; }
    public string? CohortEvidencePath { get; set; }
    public string? CohortOrientationPath { get; set; }
    public string? ElementBalanceJson { get; set; } // null for Vastu
    public string? SummaryText { get; set; }
    public string Mode { get; set; } = string.Empty; // "demo" | "live"
    public string? ModelId { get; set; }
    public string? InputFingerprint { get; set; }
    public string? ReportUri { get; set; }
    public string? ReportSha256 { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
}

/// <summary>
/// Resolved orientation for a subject. PK is SubjectId (one row per subject).
/// </summary>
public class SubjectOrientation
{
    public SubjectOrientation()
    {
    }

    public SubjectOrientation(string subjectId, double? facingDegrees, string? cardinal, string source, double? confidence, DateTimeOffset resolvedAt)
    {
        SubjectId = subjectId;
        FacingDegrees = facingDegrees;
        Cardinal = cardinal;
        Source = source;
        Confidence = confidence;
        ResolvedAt = resolvedAt;
    }

    public string SubjectId { get; set; } = string.Empty;
    public double? FacingDegrees { get; set; }
    public string? Cardinal { get; set; }
    public string Source { get; set; } = string.Empty; // "sightmap" | "annotation" | "none"
    public double? Confidence { get; set; }
    public DateTimeOffset ResolvedAt { get; set; }
}

/// <summary>
/// A named engine version snapshot: rules versions per principle set, prompt version,
/// model id, calibration constants. PublishedAt is null until the version flip.
/// </summary>
public class EngineVersion
{
    public string Version { get; set; } = string.Empty;
    public string RulesVersionFengshui { get; set; } = string.Empty;
    public string RulesVersionVastu { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? CalibrationJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>
/// A queued/running/completed scoring attempt for a subject under a given engine version.
/// </summary>
public class ScoringJob
{
    public string Id { get; set; } = string.Empty;
    public string SubjectId { get; set; } = string.Empty;
    public string EngineVersion { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty; // "backfill" | "new_listing" | "evidence_changed" | "engine_upgrade" | "task_zero"
    public string Status { get; set; } = string.Empty; // "queued" | "running" | "ok" | "failed" | "skipped"
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public double? CostUsd { get; set; }
}

/// <summary>
/// Published grade projection consumed by the LDP/SRP surfaces. Written per engine
/// version, published atomically on the version flip. Never mutated mid-rollout.
/// </summary>
public class ProjectionRow
{
    public string Id { get; set; } = string.Empty;
    public string ListingId { get; set; } = string.Empty;
    public string? FloorPlanId { get; set; } // nullable — mirrors the apartments-web child table
    public string PrincipleSet { get; set; } = string.Empty;
    public int? Score { get; set; }
    public string? Grade { get; set; }
    public string? Cohort { get; set; }
    public double? Confidence { get; set; }
    public string EngineVersion { get; set; } = string.Empty;
    public DateTimeOffset ComputedAt { get; set; }
}
