using HarmonIQ.Api.Models;

namespace HarmonIQ.Api.Services.Traditions;

/// <summary>
/// The single place that knows which traditions exist.
///
/// Everything downstream — scoring, gating, numerology, prompts, search synonyms, rules versions —
/// resolves through here rather than switching on a principle-set string. Adding a sixth tradition
/// is one new <see cref="ITradition"/> implementation plus one line in <see cref="Ordered"/>.
///
/// Static because the traditions are pure and stateless, and because read-time paths
/// (<see cref="NumerologyService"/>, report rendering) must be callable with no DI scope.
/// </summary>
public static class TraditionRegistry
{
    /// <summary>Every tradition, in display order. Never sorted by score — that would rank traditions.</summary>
    public static IReadOnlyList<ITradition> Ordered { get; } =
    [
        new FengShuiTradition(),
        new VastuTradition(),
        new PungsuTradition(),
        new KasoTradition(),
        new PhongThuyTradition(),
    ];

    private static readonly Dictionary<string, ITradition> ById =
        Ordered.ToDictionary(t => t.Id, StringComparer.Ordinal);

    /// <summary>Wire ids in display order — the canonical <see cref="PrincipleSets.All"/> source.</summary>
    public static IReadOnlyList<string> Ids { get; } = Ordered.Select(t => t.Id).ToList();

    /// <summary>The traditions that need a resolved facing to produce a stored grade.</summary>
    public static IReadOnlyList<string> OrientationGatedIds { get; } =
        Ordered.Where(t => t.RequiresOrientation).Select(t => t.Id).ToList();

    public static bool IsKnown(string? id) => id is not null && ById.ContainsKey(id);

    /// <summary>The tradition, or null when the id is unrecognized.</summary>
    public static ITradition? Find(string? id) =>
        id is not null && ById.TryGetValue(id, out var t) ? t : null;

    /// <summary>
    /// The tradition, or a throw. Use on paths where an unknown id is a programming error rather
    /// than untrusted input — callers handling user input should use <see cref="Find"/>.
    /// </summary>
    public static ITradition Require(string id) =>
        Find(id) ?? throw new ArgumentException(
            $"Unknown principle set '{id}'. Known sets: {string.Join(", ", Ids)}.", nameof(id));

    /// <summary>Query normalization lives in <see cref="SynonymMap"/>, which builds its table from here.</summary>
}
