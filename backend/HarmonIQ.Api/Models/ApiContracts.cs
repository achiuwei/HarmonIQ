namespace HarmonIQ.Api.Models;

/// <summary>
/// The single bulk payload an LDP page fetches (design Q2): every scoreable subject on the
/// property, each with its stored per-set grades. One call per page — never one per plan.
///
/// <see cref="EngineVersion"/> is echoed so a caller can pin it on subsequent requests (the
/// report body, the grades feed) and be certain badge, card and drawer all describe the same
/// engine. <see cref="Mode"/> is <c>"live"</c> or <c>"demo"</c>; demo rows are read-path
/// presentation only and are never published.
/// </summary>
public record SubjectsResponse(
    string PropertyKey,
    string EngineVersion,
    string Mode,
    IReadOnlyList<SubjectGrade> Subjects);

/// <summary>
/// One subject (a floor plan on a multi-plan property, or the property itself on a single
/// listing) and everything the chip row needs.
///
/// <see cref="Sets"/> is <b>empty</b> when the subject carries no analysis at all — the imageless
/// plan of design Q3, or one whose scoring failed. The subject is still returned so the section's
/// footprint is known at first paint; the frontend renders nothing for it. An empty list is the
/// only "unscored" signal: there is never a placeholder grade, never a zero, never an "F".
///
/// <see cref="Units"/> is computed at read time by <c>NumerologyService.EvaluateUnits</c> and is
/// never read from or written to a table (design Q1). It is an annotation, not a competing grade.
/// </summary>
public record SubjectGrade(
    string SubjectId,
    string SubjectType,
    string? PlanKey,
    string? PlanName,
    int? Beds,
    double? Baths,
    IReadOnlyList<SetGrade> Sets,
    IReadOnlyList<UnitNumerologyAnnotation> Units);

/// <summary>
/// One tradition's stored verdict. There is no blended headline: <c>both</c> is a UI union of the
/// two rows in <see cref="SubjectGrade.Sets"/>, never a third score.
///
/// <see cref="Score"/> and <see cref="Grade"/> are null unless <see cref="Status"/> is
/// <c>"ok"</c>; with the API's <c>WhenWritingNull</c> policy they are then absent from the JSON
/// entirely, so a client can never mistake an unscored subject for a badly scored one.
/// <see cref="EvidencePath"/> + <see cref="OrientationPath"/> carry the cohort, so a consumer can
/// rank and filter <b>within cohort</b> (design §2) rather than across incomparable evidence.
/// </summary>
public record SetGrade(
    string PrincipleSet,
    string Status,
    int? Score,
    string? Grade,
    double Confidence,
    string EvidencePath,
    string OrientationPath);

/// <summary>
/// A session-only recompute request. Overrides are applied on top of the subject's stored
/// immutable input set; anything left null keeps the stored value.
/// </summary>
/// <param name="Orientation">
/// A renter-supplied cardinal facing ("north", "southeast", ...). For Vastu on a subject with no
/// resolved orientation this is the only way to get a number at all — and that number is
/// explicitly session-only: never stored, never published, never filterable.
/// </param>
public record RefineRequest(
    string SubjectId,
    string PrincipleSet,
    string? Orientation = null,
    ListingEnvironment? Environment = null,
    ListingNumbers? Numbers = null);

/// <summary>
/// The result of a refine. <see cref="Persisted"/> is <b>always false</b> — the endpoint writes
/// nothing, by construction — and <see cref="Notice"/> says so in the reader's language.
/// </summary>
public record RefineResponse(SetScore Score, bool Persisted, string Notice);
