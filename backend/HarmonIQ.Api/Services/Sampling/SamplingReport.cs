using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services.Sampling;

/// <summary>
/// Within-property score variance across a property's floor-plan subjects — the risk gate
/// (design §11 #1 / plan Task 13): if the floor-plan lens creates no real variance, per-plan
/// grades are cosmetic and the design's stated fallback is a property grade plus per-plan layout
/// notes instead of twelve identical chips.
/// </summary>
/// <param name="MeanStdDev">Mean, across multi-plan properties, of the standard deviation of
/// <c>Score</c> among that property's <c>Ok</c>-status plans (one principle set at a time,
/// averaged across sets present). Zero for a property scored on a single plan — that property
/// contributes no variance signal and is counted in <see cref="PropertiesWithZeroVariance"/>.</param>
/// <param name="MedianRange">Median, across the same properties, of <c>max(Score) − min(Score)</c>
/// among that property's scored plans — a range is easier to eyeball than a standard deviation
/// and is reported alongside it rather than instead of it.</param>
/// <param name="PropertiesWithZeroVariance">Count of multi-plan properties whose scored plans are
/// all within the property/set group but produced identical (or a single) score — the direct
/// count behind the "cosmetic" call, independent of how the mean/median read.</param>
public record WithinPropertyVariance(double MeanStdDev, double MedianRange, int PropertiesWithZeroVariance);

/// <summary>
/// Real cost per property. No Claude key exists on this machine (demo mode is the runnable path),
/// so every figure here is <b>modelled from the design's own per-call unit-economics assumptions
/// (§6), never billed</b> — <see cref="Estimated"/> is always true locally and every consumer of
/// this record must render that plainly rather than implying a live invoice.
/// </summary>
public record CostSummary(
    int PerceptionCallsMade,
    double AssumedCostPerCallUsd,
    double TotalCostUsd,
    double CostPerPropertyUsd,
    bool Estimated);

/// <summary>
/// The task-zero decision-gate artifact (design §6, §11 #1/#3; plan Task 13). Four measurements —
/// plan-image coverage, within-property variance, per-cohort calibration, and cost — plus a
/// verdict and the mandatory illustrative-scale caveat. Written under <c>.harmoniq-local/</c>
/// (gitignored); never committed.
/// </summary>
public record SamplingReport(
    DateTimeOffset GeneratedAt,
    int RequestedN,
    int AvailableSubjects,
    int SampledProperties,
    int SampledSubjects,
    int FloorPlanSubjectsTotal,
    int FloorPlanSubjectsWithImage,
    double PlanImageCoverage,
    bool DualScoreRequested,
    int DualScoredSubjects,
    WithinPropertyVariance Variance,
    IReadOnlyDictionary<string, CalibrationConstants> Calibration,
    CostSummary Cost,
    IReadOnlyList<string> Notes,
    string Verdict,
    string Caveat);
