using System.Globalization;
using System.Text;
using HarmonIQ.Api.Services.Orientation;

namespace HarmonIQ.Api.Commands;

/// <summary>
/// Runs the outline identifiability probe (see <see cref="OutlineIdentifiability"/>) over a
/// committed Overpass fixture and prints one row per building.
///
/// <para><b>Why it scores every candidate rather than one chosen subject.</b> No OSM building near
/// Enzo carries its street address, so which footprint <i>is</i> the subject cannot be settled from
/// the data — and picking the wrong one would silently apply a neighbouring office block's rotation
/// to Enzo's units. Scoring all of them sidesteps that: if every plausible candidate is
/// unidentifiable, identity does not matter and the approach fails at this site regardless. Identity
/// only becomes worth resolving if some candidate clears the bar.</para>
///
/// <code>
/// dotnet run --project backend/HarmonIQ.Api -- outline-probe
///   [--fixture &lt;path&gt;] [--lat &lt;deg&gt;] [--lon &lt;deg&gt;]
///   [--disagreement &lt;metres&gt;] [--report &lt;path&gt;]
/// </code>
/// </summary>
public class OutlineProbeCommand(IWebHostEnvironment env) : IHarmonIQCommand
{
    /// <summary>Enzo (<c>349246f</c>), 3100 Martin, Irvine — the only multi-plan demo subject, and
    /// the anchor the committed fixture was fetched around.</summary>
    private const double DefaultAnchorLat = 33.67253;
    private const double DefaultAnchorLon = -117.85841;

    /// <summary>
    /// Assumed disagreement between an OSM roof trace and a real Unit Map floor outline, in metres.
    /// <b>A stated guess, not a measurement</b> — OSM footprints are digitised from imagery and
    /// follow roofs, so overhangs, balconies and podium-versus-tower differences all move the
    /// boundary. It is a parameter precisely so it stays visible; every verdict here is conditional
    /// on it, and nobody has yet seen a real unit map to pin it down.
    /// </summary>
    private const double DefaultDisagreementMetres = 2.0;

    public string Name => "outline-probe";

    public string Description =>
        "Measure the rotational identifiability ceiling of OSM building footprints (orientation research).";

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var fixturePath = ArgValue(args, "--fixture")
            ?? Path.Combine(env.ContentRootPath, "Data", "enzo-osm-buildings.json");
        var anchorLat = ArgDouble(args, "--lat") ?? DefaultAnchorLat;
        var anchorLon = ArgDouble(args, "--lon") ?? DefaultAnchorLon;
        var disagreement = ArgDouble(args, "--disagreement") ?? DefaultDisagreementMetres;
        var reportPath = ArgValue(args, "--report");

        if (!File.Exists(fixturePath))
        {
            Console.Error.WriteLine($"outline-probe: fixture not found at {fixturePath}");
            return 1;
        }

        var json = await File.ReadAllTextAsync(fixturePath, ct);
        var read = OverpassOutlineReader.Read(json, anchorLat, anchorLon);

        var lines = new StringBuilder();
        void Emit(string line)
        {
            Console.WriteLine(line);
            lines.AppendLine(line);
        }

        Emit($"fixture: {fixturePath}");
        Emit($"anchor: {anchorLat.ToString(CultureInfo.InvariantCulture)}, " +
             $"{anchorLon.ToString(CultureInfo.InvariantCulture)}");
        Emit($"assumed outline disagreement: {disagreement:F2}m " +
             "(a stated assumption — every verdict below is conditional on it)");
        Emit($"usable outlines: {read.Outlines.Count}");

        if (read.SkippedNoGeometry > 0 || read.SkippedDegenerate > 0)
        {
            Emit($"SKIPPED: {read.SkippedNoGeometry} without geometry, " +
                 $"{read.SkippedDegenerate} degenerate — these are NOT covered below");
            foreach (var label in read.SkippedLabels) Emit($"  - {label}");
            if (read.SkippedNoGeometry > 0)
            {
                Emit("  (a fixture fetched with `out geom tags` loses every relation's geometry; " +
                     "refetch with plain `out geom;`)");
            }
        }

        if (read.Outlines.Count == 0)
        {
            Emit("No usable outlines: nothing to align to. This ends the probe for this site.");
            await WriteReportAsync(reportPath, lines, ct);
            return 0;
        }

        Emit("");
        Emit($"{"dist",6} {"area",7} {"tag",-11} {"vtx",4} {"radius",7} {"basin",6} " +
             $"{"best rival",16} {"flip rival",16} {"prec",5} {"ratio",6} {"tf resid",8}  label");

        foreach (var outline in read.Outlines.OrderBy(o => o.DistanceFromAnchorMetres))
        {
            var result = OutlineIdentifiability.Measure(outline);
            Emit(string.Format(
                CultureInfo.InvariantCulture,
                "{0,5:F0}m {1,7:F0} {2,-11} {3,4} {4,6:F1}m {5,5:F0}° {6,16} {7,16} {8,4:F0}° {9,6:F2} {10,7:F1}°  {11}",
                outline.DistanceFromAnchorMetres,
                outline.AreaSquareMetres,
                outline.Tag("building") ?? "-",
                outline.Ring.Count,
                result.CharacteristicRadiusMetres,
                result.CentralBasinHalfWidthDegrees,
                Describe(result.BestRival),
                Describe(result.FlipRival),
                result.PrecisionHalfWidthDegrees(disagreement),
                result.DiscriminationRatio(disagreement),
                result.TurningRivalResidualDegrees,
                outline.Label));
        }

        Emit("");
        Emit("ratio = best rival's mismatch / assumed disagreement. Below ~1 the rival is " +
             "indistinguishable from noise and the footprint cannot pin north.");
        Emit("flip rival = a rival within 20° of 180°: the failure that inverts every direction " +
             "rather than merely leaving the facing unresolved.");

        var scored = read.Outlines
            .Select(o => (Outline: o, Result: OutlineIdentifiability.Measure(o)))
            .ToList();
        var clearing = scored.Where(s => s.Result.DiscriminationRatio(disagreement) >= 2.0).ToList();

        Emit("");
        Emit($"candidates clearing a 2x discrimination ratio: {clearing.Count} of {scored.Count}");
        foreach (var (outline, result) in clearing)
        {
            Emit($"  {outline.Label} ({outline.OsmType}/{outline.OsmId}) at " +
                 $"{outline.DistanceFromAnchorMetres:F0}m, ratio " +
                 $"{result.DiscriminationRatio(disagreement):F2}");
        }
        if (clearing.Count == 0)
        {
            Emit("  none — subject identity is moot here: no candidate footprint could pin north");
            Emit("  even given a perfect floor outline, so the approach fails at this site.");
        }

        await WriteReportAsync(reportPath, lines, ct);
        return 0;
    }

    private static string Describe(RivalRotation? rival) =>
        rival is null
            ? "none"
            : string.Format(CultureInfo.InvariantCulture, "{0,3:F0}° @ {1,6:F2}m",
                rival.AngleDegrees, rival.MismatchMetres);

    private static async Task WriteReportAsync(string? path, StringBuilder lines, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(path)) return;
        await File.WriteAllTextAsync(path, lines.ToString(), ct);
        Console.WriteLine($"wrote {path}");
    }

    private static string? ArgValue(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static double? ArgDouble(string[] args, string flag) =>
        double.TryParse(ArgValue(args, flag), CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
