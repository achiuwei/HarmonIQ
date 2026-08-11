using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services.Traditions;

/// <summary>
/// One culture's spatial-harmony tradition, as a single self-contained unit.
///
/// Before this abstraction each tradition was smeared across four files — its prompt in
/// <c>Prompts</c>, its site rules in <c>SiteAnalysisService</c>, its numerology in
/// <c>NumerologyService</c>, and its orientation gating in <c>VastuGate</c> — every one of them a
/// binary <c>principleSet == Vastu ? … : …</c>. Five traditions would have meant five-way switches
/// in five places. Here, a prompt, its site catalogue, and its numerology live together, so someone
/// who knows the tradition can review all of it at once.
///
/// Implementations must be pure and stateless: registered as singletons and called from read-time
/// paths with no database in play.
/// </summary>
public interface ITradition
{
    /// <summary>Stable wire id — the <c>principleSet</c> string in the API, feed, and DB.</summary>
    string Id { get; }

    /// <summary>Renter-facing name, e.g. "Vastu Shastra".</summary>
    string DisplayName { get; }

    /// <summary>Culture of origin, for the SRP filter's "Korea — Pungsu-jiri" labelling.</summary>
    string Culture { get; }

    /// <summary>Display order across all surfaces. Never "highest score first" — that would rank traditions.</summary>
    int Order { get; }

    /// <summary>
    /// Scoped per tradition so one tradition's rules change never invalidates another's analyses
    /// (FR-41). Bump when this tradition's site catalogue or interpretation prompt changes.
    /// </summary>
    string RulesVersion { get; }

    /// <summary>
    /// True when a resolved facing is required to produce a <b>stored</b> grade. The gated
    /// traditions' core doctrine is absolute-directional, so the remainder would be
    /// "the tradition with the tradition removed" — they return <c>insufficient_evidence</c>
    /// rather than a renormalized score. Ungated traditions degrade through coverage instead.
    /// </summary>
    bool RequiresOrientation { get; }

    /// <summary>
    /// True for the Sinitic traditions, which share wǔxíng (五行 wood/fire/earth/metal/water).
    /// False for Vastu, whose pancha bhuta are a different five (earth/water/fire/air/space) and
    /// so cannot be carried in the same <see cref="ElementBalance"/> shape — its section is
    /// omitted rather than zeroed.
    /// </summary>
    bool UsesWuxing { get; }

    /// <summary>Framing clause for generated prose, e.g. "in Vastu Shastra terms".</summary>
    string TraditionPhrase { get; }

    /// <summary>Query spellings the SRP typeahead recognizes, lower-cased. Includes native script.</summary>
    IReadOnlyList<string> SearchSynonyms { get; }

    /// <summary>
    /// This tradition's own reading of the site. Deterministic — no model call. Rules whose
    /// evidence is unknown must be emitted <b>not applicable</b>, never violated, so missing data
    /// lowers coverage rather than the score.
    /// </summary>
    /// <param name="cardinal">Resolved facing, or null when unresolved.</param>
    IReadOnlyList<RuleOutcome> SiteCatalogue(ListingEnvironment env, string? cardinal);

    /// <summary>This tradition's reading of one number. Deterministic; a rules engine, not the LLM (FR-18).</summary>
    NumerologyCheck Numerology(string subject, string value);

    /// <summary>Short human title for one of this tradition's rule ids, for report rendering.</summary>
    string? RuleTitle(string ruleId);

    /// <summary>Renter-feasible remedy for one of this tradition's rule ids, or null.</summary>
    Suggestion? RuleRemedy(string ruleId);

    /// <summary>
    /// The explanatory absence shown instead of a grade when this tradition is gated off for want
    /// of a facing. Only meaningful when <see cref="RequiresOrientation"/> is true.
    /// </summary>
    string OrientationGateExplanation { get; }

    /// <summary>
    /// This tradition's interpretation prompt (stage 3). Reads the shared, tradition-agnostic
    /// fact sheet produced by the single vision pass and applies only this tradition's doctrine.
    /// Every tradition sees identical evidence, so a score difference is attributable to the
    /// tradition rather than to what one call happened to notice.
    /// </summary>
    string InterpretPrompt(string factSheet, string? orientationHint);
}
