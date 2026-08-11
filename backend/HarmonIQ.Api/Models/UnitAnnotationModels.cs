namespace HarmonIQ.Api.Models;

/// <summary>
/// A per-unit numerology reading, computed at read time from a unit's number and
/// (optionally) its floor. It is deterministic and microsecond-cheap, so it is never
/// persisted — no <c>units</c> table, no per-unit rows, no per-unit subject.
///
/// This is an annotation, not a grade (design Q1 / SPEC v2 FR-20): it renders inline
/// in the availability table (or a Numbers card on the photo path), never as a
/// competing score. <see cref="Adjustment"/> is carried only for internal ordering —
/// callers must never render it as a number, letter, or colour that reads as a grade.
/// The plan-level badge remains the only grade on the page.
/// </summary>
/// <param name="UnitNumber">The unit number as scraped (e.g. "444").</param>
/// <param name="Floor">The unit's floor, when known.</param>
/// <param name="PrincipleSet">
/// The tradition this reading is scoped to: <see cref="PrincipleSets.FengShui"/> or
/// <see cref="PrincipleSets.Vastu"/>. Annotations are computed independently per set —
/// a Vastu reading never changes the Feng Shui reading of the same unit, and vice versa.
/// </param>
/// <param name="Adjustment">Clamped to [-3, 3]. Never surfaced as a score.</param>
/// <param name="Verdict">One of "lucky", "neutral", "unlucky".</param>
/// <param name="Note">
/// Tradition-framed prose (e.g. "in Chinese numerology, ..."). Never states an
/// objective claim and never uses a negative superlative (NFR-8).
/// </param>
public record UnitNumerologyAnnotation(
    string UnitNumber,
    int? Floor,
    string PrincipleSet,
    int Adjustment,
    string Verdict,
    string Note);
