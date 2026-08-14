# Can an OSM footprint pin true north? — measured at Enzo

**Date:** 2026-08-12 · **Status:** inconclusive by design, and the reason is the finding ·
**Extends** [orientation-data-sources.md §5](orientation-data-sources.md)

§5 proposed recovering true north by aligning Engrain map-space geometry to OSM building footprints,
and reported simulation numbers (43% coverage at margin ≥2.0, 0 errors in 102 samples) measured on
**randomly placed synthetic building sets**. This note replaces those numbers with a measurement on
the real footprints at the one real multi-plan demo subject.

Two things changed on contact with the data.

---

## 1. The method in §5 does not apply to Enzo

§5's algorithm matches the *relative arrangement of several buildings*. Enzo has no such
arrangement. Overpass returns 31 buildings within 300m of `33.67253, -117.85841`, and they are the
Irvine Business Complex — offices on Dupont and Von Karman, the Langson IMCA museum, the Hilton
Irvine, four parking structures. The only `building=apartments` in range sits at **18880 MacArthur
Boulevard, 230m away**, a different property.

Enzo is a **single-building site** as far as OSM is concerned. Constellation matching has nothing to
match, so §5's coverage figure does not transfer — not as a tuning problem, but because the
algorithm's input does not exist here.

What remains available is matching a **single outline** to a single footprint. A complex asymmetric
outline has one good rotational fit, so this is not automatically hopeless. It is a different
algorithm with a different failure mode, and the rest of this note measures it.

## 2. Subject identity cannot be settled from the data

No OSM building within 300m carries Enzo's address (`3100 Martin`). The nearest candidates are:

| Distance | Area | Vertices | Label |
|---|---|---|---|
| 42m | 10,932 m² | 31 | *(unnamed `building=yes`)* |
| 74m | 5,770 m² | **0** | 2253 Martin |
| 95m | 414 m² | 24 | 2263 Martin |
| 121m | 1,462 m² | 12 | 3202 Martin |
| 128m | 7,146 m² | **0** | 2248 Martin |

The unnamed building at 42m is the leading candidate on proximity and size, but that is inference,
not identification. Picking wrong is a **silent** total failure: an aligner would recover a
neighbouring office block's rotation and apply it to Enzo's units, producing confidently backwards
Vastu grades rather than the honest absence §6 prefers.

Note also the asymmetry with `ARCHITECTURE.md §8`, which forbids deriving a *bearing* from the
geocode. Selecting a footprint by proximity is weaker than that, but it still inherits geocode
error — and apartments.com geocodes routinely err by more than the 42–128m spread separating these
candidates.

**The probe therefore scores every candidate rather than one chosen subject.** If no candidate can
pin north, identity is moot and the site fails regardless — which is what makes the result below
usable despite the unresolved identity.

## 3. What was measured

`OutlineIdentifiability.Measure` rotates a footprint against itself through 360° and records the
mean symmetric mismatch, in metres, at each angle. The true rotation scores exactly 0m by
construction; any *other* low-scoring rotation is a hypothesis no aligner could rule out even with a
perfect input. Two numbers come out:

- **Precision** — how tightly the rotation is pinned. Must stay well inside 45°, since
  `OrientationResolution` buckets bearings into 90°-wide cardinal sectors.
- **Ambiguity** — whether a *distant* rotation fits about as well. A rival within 20° of 180° is the
  dangerous one: it inverts the entire directional scheme.

A second, independent metric (turning-function residual — pure shape, scale- and position-free)
cross-checks which rotation is confusable. Agreement between the two makes a finding a property of
the shape rather than of one scoring choice.

**There is deliberately no peak-versus-rival ratio.** §5's ≥2.0 gate presumed a *noisy* true match;
here the truth scores exactly zero, so any ratio against it is degenerate. The substitute is
`DiscriminationRatio`: the best rival's mismatch divided by an explicitly supplied estimate of how
far a real Unit Map floor outline departs from an OSM roof trace. That estimate is a **stated
assumption, not a measurement** — it is a parameter so that it stays visible.

Both remaining biases run the same direction, so every number here is an **upper bound**:
translation is fixed by centroid coincidence rather than re-optimized per angle (a sliding aligner
could only find a *better* rival), and the scan compares OSM to itself, whereas a real floor outline
adds mismatch at the true rotation only.

## 4. Result

The verdict is decided by the one quantity nobody has measured:

| Assumed floor-vs-roof disagreement | Candidates clearing 2× | Leading Enzo candidate |
|---|---|---|
| 0.5m | 15 of 26 (58%) | **7.53 — clears** |
| 1.0m | 11 of 26 (42%) | **3.76 — clears** |
| 2.0m | 6 of 26 (23%) | 1.88 — fails |
| 3.0m | 3 of 26 (12%) | 1.25 — fails |

The leading Enzo candidate's most confusable hypothesis is a **180° flip at 3.76m mismatch** — the
grade-inverting failure, not a benign one. Whether that flip is rejectable depends entirely on
whether real outline disagreement is nearer 1m or nearer 3m. Roof overhangs, balconies, and
podium-versus-tower differences all plausibly land in that range, and **the crossover sits inside
it**.

Three further observations from the run:

- **Precision is not the bottleneck.** Every candidate pins its rotation to 1–28° at these
  tolerances, comfortably inside a 45° sector. The approach's whole risk is discrete flips.
- **Rectangular footprints score exactly 0.00m at 180°.** Five buildings here do
  (`2442-2450 Dupont`, `18821-18829 Bardeen`, `2452-2468 Dupont`, `way/1351246084`, and a parking
  structure at 0.02m). A plain bar is *perfectly* unidentifiable, and plain bars are the commonest
  apartment building form there is.
- **Only 2 of 26 buildings have no flip rival at all** (`Hampton`, `2212 Dupont Drive`). For most of
  the rest, the best rival *is* the flip.

## 5. Known incompleteness

The committed fixture was fetched with `out geom tags`, and Overpass's `tags` modifier suppresses
member lists — so `geom` had nothing to attach coordinates to for relations and emitted only a
bounding box. **Five relations therefore have no outline, and three of them are the
Martin-addressed buildings** — the best identity candidates. Refetch with plain `out geom;`:

```
[out:json][timeout:60];
(nwr(around:300,33.67253,-117.85841)[building];);
out geom;
```

`OverpassOutlineReader` reports these as `SkippedNoGeometry` rather than dropping them quietly,
because a silently missing building would make the probe look exactly as confident over a smaller
set.

Also unmeasured: n=1. This is one site, chosen because it is the only real multi-plan demo subject.
It supports no coverage estimate whatsoever, and the 26-building table is 26 *neighbouring* buildings
at one location — not a sample of apartment complexes.

## 6. What would settle it

In priority order:

1. **The `geojson_url` question in §4.** Still the only path that dissolves the problem instead of
   bounding it. One call with a Unit Map key.
2. **One real Unit Map floor outline** for any licensed asset, compared against its OSM footprint.
   That measures the disagreement parameter directly and collapses the table in §4 to a single row.
3. **Enzo's identity**, confirmed against aerial imagery — worth doing only if 1 and 2 come back
   favourable.

## 7. Status of each claim

| Claim | Basis |
|---|---|
| Enzo is a single-building site in OSM | Overpass `nwr(around:300)[building]`, 31 elements |
| No OSM building carries `3100 Martin` | Same fetch, address tags inspected |
| Rectangular footprints are exactly unidentifiable | Measured, 0.00m at 180°; matches the control tests |
| Precision stays inside one cardinal sector | Measured across all 26 candidates |
| Leading candidate's rival is a 180° flip at 3.76m | Measured |
| Verdict flips between 1m and 2m assumed disagreement | Measured sweep, §4 |
| Real floor-vs-roof disagreement is 1–3m | **Guess — nobody has seen a real unit map** |
| §5's 43% coverage figure | Synthetic multi-building sites; does not apply here |

## 8. Reproducing

```
dotnet run --project backend/HarmonIQ.Api -- outline-probe [--disagreement <metres>]
```

Reads `Data/enzo-osm-buildings.json`, needs no network, and is deterministic. The scorer's controls
live in `HarmonIQ.Tests/OutlineIdentifiabilityTests.cs` and pin it against shapes whose answer
follows from symmetry alone — a scorer that called everything unidentifiable would otherwise be
indistinguishable from the result we half-expected to find here.
