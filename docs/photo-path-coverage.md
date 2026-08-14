# The photo path scored nothing, and the tests could not see it

**Date:** 2026-08-12 · **Status:** prototype fix landed behind new tests, not yet re-exported

Every `photos/*` subject in the `b8fafdc19400` export came back `insufficient_evidence` — including
the property-level rows that feed apartments-web's score card. Every `floorplan/*` subject on the
same building scored normally. This records why, and what changed.

---

## 1. The asymmetry

Both interiors lenses multiply two coverage factors, but the factors are not the same kind of thing.

| Path | Factor 1 | Factor 2 |
|---|---|---|
| **Floor plan** (`PlanLens`) | `RuleEvaluation.Coverage(outcomes)` — **deterministic**: applicable ÷ total over the fixed `FloorPlanRules` catalogue | `plan.Coverage` — model self-reported |
| **Photos** (`RoomLens`, live) | `perceptionCoverage` — model self-reported (stage 1) | `interpretation.Coverage` — model self-reported (stage 3) |

The plan path is anchored: one factor is computed in code against a closed catalogue and cannot
drift. The photo path had **no catalogue denominator at all** — it multiplied two independent
answers to "how much could you evaluate?", a question models answer conservatively. Two conservative
numbers around 0.6 compound to ~0.36, against a `ConfidenceFloor` of 0.50.

The comment directly above the code said coverage "measures how much the photographs showed, which
is a property of the evidence, **not of the tradition reading it**" — and the next line multiplied
it by a tradition-specific number. The code contradicted its own stated intent.

## 2. The evidence

Enzo (`349246f`) carries both paths — same building, same location, same traditions — so the site
term is constant and any gap is interiors:

| Path | Confidence | Implied interiors coverage |
|---|---|---|
| `floorplan/*` (20 plans, 47 of 100 rows `ok`) | 0.525 – 0.595 | ≈ 0.43 |
| `photos/*` (property level, 0 of 5 rows `ok`) | 0.294 – 0.442 | ≈ 0.03 – 0.21 |

`z6cbnfy` behaves identically: five property-level rows, all unscored, Feng Shui at coverage 0.294,
with a full Site lens at 8-of-8 and four specific, plausible suggestions. Vision ran and saw the
home. The reading was discarded by arithmetic, not by absence of evidence.

## 3. Why no test caught it

`TraditionInterpretation` appeared **nowhere in the test suite**. Every photo-path test ran the
fallback branch — the demo/mock path, which uses `perceptionCoverage` alone with no multiplication.

So the mock path produced roughly double the coverage of a live run of the same subject. The photo
path scored in the demo and never scored for real, and 371 green tests were consistent with that.

## 4. The change

`RoomLens`'s live branch now uses `perceptionCoverage` alone, matching the fallback branch and the
comment's stated intent. The plan path is untouched — its product is a different shape, and its
anchored factor is doing real work.

**Blast radius, as of the `b8fafdc19400` export: no existing grade changes.** Since
`interpretation.Coverage ∈ [0,1]`, dropping it can only raise coverage, never lower it — so a
subject that scores cannot stop scoring. And no live photo subject scored at all, so the only
movement is `insufficient_evidence` → a grade.

That is a property of the current export, not a general guarantee: for a photo subject that *does*
score, raising interiors coverage shifts aggregate weight from Site toward Interiors, which can move
the grade in either direction.

## 5. What this does not settle

- **The fix is unverified against real data.** `export-fixture` needs a live analysis (Claude API
  key + network), so the new coverage numbers have not been observed — only the arithmetic and the
  unit tests. Re-run the export before trusting any specific grade.
- **`perceptionCoverage` is still model self-reported.** This removes the double discount; it does
  not anchor the photo path the way the plan path is anchored. Building a photo-side rule catalogue,
  so factor 1 is computed rather than reported, is the more durable fix and is not done here.
- **Whether 0.5 is the right floor for the photo path** is untouched. A photo subject now lands
  around 0.5–0.6, i.e. just over — the same margin the plan path has. That is thin by construction,
  and worth revisiting with real numbers rather than by adjusting the floor.
- **The second `FindingConfidenceFloor = 0.5`** still drops individual findings before coverage is
  computed. Not changed; noted because it compounds with anything else in this area.

## 6. Tests

`HarmonIQ.Tests/PhotoPathCoverageTests.cs` — the first coverage of the live interpretation branch:

- coverage is perception's number, not the product (this is the fix)
- a subject at realistic coverages clears the floor (the consequence, with numbers chosen so
  compounding is what decides it)
- no findings above the finding floor ⇒ zero coverage, not a zero score (guard rail, unchanged)
- perception facts with no interpretation ⇒ zero coverage for that tradition (guard rail, unchanged)
