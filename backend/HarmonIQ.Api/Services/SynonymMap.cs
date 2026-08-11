using HarmonIQ.Api.Services.Traditions;

namespace HarmonIQ.Api.Services;

/// <summary>
/// Normalizes a renter's free-text search query to a known principle set (design §8). The variant
/// table is built from each tradition's own <see cref="ITradition.SearchSynonyms"/>, so adding a
/// tradition adds its spellings automatically and there is no second list to keep in step.
///
/// <b>Matching is whole-query, not substring.</b> The query is stripped of everything but letters
/// and digits and lowercased, then compared for <i>exact</i> equality against a known variant —
/// never "contains". That is what keeps "vast open floor plan" (normalizes to
/// <c>"vastopenfloorplan"</c>) from incidentally matching "vastu" while still treating
/// "feng-shui", "Feng Shui" and "FENGSHUI" as the same query (all normalize to
/// <c>"fengshui"</c>). A typeahead box is expected to hold just the term the renter typed, not a
/// full sentence containing it.
///
/// Stripping non-alphanumerics keeps native-script spellings working (家相, 풍수지리, वास्तु all
/// survive) while collapsing romanization diacritics' surrounding punctuation.
/// </summary>
public static class SynonymMap
{
    private static readonly IReadOnlyDictionary<string, string> Variants = BuildVariants();

    /// <summary>
    /// Throws at startup if two traditions claim the same spelling. A silent last-one-wins here
    /// would route a renter to the wrong tradition, which is worse than failing loudly — this is
    /// what caught 風水 being claimed by both Feng Shui and Kasō.
    /// </summary>
    private static Dictionary<string, string> BuildVariants()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tradition in TraditionRegistry.Ordered)
        {
            foreach (var synonym in tradition.SearchSynonyms)
            {
                var key = Key(synonym);
                if (key.Length == 0) continue;
                if (map.TryGetValue(key, out var owner) && owner != tradition.Id)
                {
                    throw new InvalidOperationException(
                        $"Search synonym '{synonym}' is claimed by both '{owner}' and '{tradition.Id}'. " +
                        "One tradition must give it up — an ambiguous query cannot resolve to a set.");
                }
                map[key] = tradition.Id;
            }
        }
        return map;
    }

    private static string Key(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Returns the matched principle set, or <c>null</c> for anything that isn't an exact,
    /// punctuation/case/whitespace-normalized match — including <c>null</c>, empty, whitespace-only
    /// and unrelated queries.
    /// </summary>
    public static string? Normalize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var key = Key(query);
        return key.Length > 0 && Variants.TryGetValue(key, out var set) ? set : null;
    }
}
