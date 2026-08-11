namespace HarmonIQ.Api.Models;

/// <summary>
/// The typeahead suggestion chip for a synonym query (design §8): a renter who types any spelling
/// of "feng shui" or "vastu" is offered a one-click path to the SRP, pre-filtered to that set with
/// the HarmonIQ filter panel already open. <see cref="Url"/>'s querystring contract is
/// <c>?harmoniqFilter=open&amp;sets={principleSet}</c> — the mock SRP (Task 19) binds to exactly
/// this shape.
/// </summary>
public record SuggestResponse(string? PrincipleSet, string Label, string Url);

/// <summary>
/// One property in a filtered search result. <see cref="BestSubjectId"/> names the single subject
/// (a floor plan, or the property itself on a single listing) whose grades are shown in
/// <see cref="Sets"/> — the best-qualifying subject when a multi-plan property has more than one
/// scored plan, never a cross-plan blend. <see cref="Sets"/> carries every stored set for that one
/// subject (not only the set(s) the query filtered on), so a renter who matched on Vastu still
/// sees the Feng Shui badge if one exists — the SRP badge reuses the LDP chip component.
/// </summary>
public record SearchHit(string PropertyKey, string Title, string? BestSubjectId, IReadOnlyList<SetGrade> Sets);

/// <summary>
/// The result of a filtered HarmonIQ search (design §8, R4/R7). Filtering reads only stored,
/// published projection rows — never a live vision call, which cannot sit in a search request
/// path — so an unscored property is never in <see cref="Hits"/> and is never mistaken for a badly
/// scored one.
///
/// <see cref="TotalScoredInArea"/> counts properties carrying at least one published grade for the
/// requested set(s), independent of whether that grade cleared the filter threshold;
/// <see cref="TotalInArea"/> counts every property known locally. Together they make the omission
/// of unscored inventory legible rather than silent (design §10); <see cref="Caveat"/> renders that
/// same arithmetic in words.
/// </summary>
public record SearchResponse(
    IReadOnlyList<SearchHit> Hits, int TotalScoredInArea, int TotalInArea, string Caveat);
