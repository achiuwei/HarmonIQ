# Design — retire `ITradition.Culture`; move demo fixtures to the two Irvine listings

Date: 2026-08-11

Two independent changes. No shared files, no ordering constraint between them.

---

## Change 1 — retire the culture-of-origin label

### Why it is a field deletion, not a string edit

The renter-facing label is sourced from `ITradition.Culture`. Editing the five strings would leave
the surface able to come back. Deleting the field makes the label unrepresentable.

`Culture` never crossed the wire — it is absent from `ProjectionRow` and `SetGrade`, and
apartments-web keeps its own copy in `TraditionDisplay.cs`. So this repo can retire it with no
contract change and no coordination window.

### Edits

| File | Change |
|---|---|
| `Services/Traditions/ITradition.cs:26-27` | Delete `Culture` and its doc comment (which justifies the field by the SRP filter's "Korea — Pungsu-jiri" labelling — the labelling being removed) |
| `FengShuiTradition.cs:17`, `VastuTradition.cs:21`, `PungsuTradition.cs:22`, `KasoTradition.cs:23`, `PhongThuyTradition.cs:19` | Delete the `Culture` property |
| `InterpretPromptBuilder.cs` | Drop the `culture` parameter and its `<param>` doc line |
| Five `.Build(DisplayName, Culture, Doctrine, …)` call sites | Drop the argument |
| `frontend/src/api.ts:17-22` | Drop `culture` from the `TRADITIONS` table and its inline type |
| `frontend/src/components/RefineDrawer.tsx:126` | Tooltip falls back to the tradition label |
| `wwwroot/embed/harmoniq-module.js` | Rebuild from source (`npm run build`) |
| `SPEC.md`, `docs/ARCHITECTURE.md:82,100` | Drop the Culture column and the `Culture` interface-member row |

The prompt's opening line becomes tradition-only framing:

> You are HarmonIQ, an expert consultant in {displayName}, a tradition of spatial harmony.

replacing `…, the {culture} tradition of spatial harmony.`

### Tests

- `TraditionRegistryTests.cs:37` — `Assert.False(IsNullOrWhiteSpace(t.Culture))` is **removed**, not weakened.
- `TraditionRegistryTests.cs:183` — `Assert.Contains(t.Culture, prompt)` actively asserts the culture
  name appears in the prompt. **Removed**, not adjusted.
- `TraditionRegistryTests.AllFiveCulturesArePresent_InDisplayOrder:14` — renamed. It asserts five
  traditions in display order, which still holds.

### Boundary — explicitly out of scope

Only the display label retires. Left alone:

- **Doctrine bodies** (`PungsuTradition.cs:155`, `KasoTradition.cs:141`, `PhongThuyTradition.cs:131`)
  that reference related traditions by culture. That is doctrinal content explaining where
  traditions diverge, and the divergence is the point.
- **Numerology note strings** ("Contains 4 (사)…", "in Chinese numerology…"). These are the
  tradition's own reading of a number, not a label on the tradition.
- `NumerologyServiceTests` and `TraditionDivergenceTests` must stay green **untouched**. If a change
  requires editing either, the boundary has been crossed — stop and re-read.
- The badge tagline "Cultural harmony across five traditions" contains no country or culture name
  and is not a culture-of-origin label. Retained.

### Done check

`grep -c Chinese backend/HarmonIQ.Api/wwwroot/embed/harmoniq-module.js` → `0`.

Note `wwwroot/embed/` is gitignored — the bundle is a local build artifact. Rebuilding is still
required, or the dev server serves the old strings.

---

## Change 2 — move the demo fixtures to the two Irvine listings

| Fixture | Listing | Key | Role |
|---|---|---|---|
| `Data/sample-multiplan-listing.json` | Enzo, Irvine CA | `349246f` | multi-plan — a grade chip per floor plan |
| `Data/sample-listing.json` | 108 Ambiance, Irvine CA | `tk93cec` | single listing — the compact score card |

Subjects stay per floor plan. "Grade per unit" means a grade on each row of the floor-plan grid;
the subject model does not change.

### Scraping approach — extend `PlanScraper` with real selectors

`PlanScraper` today has **no production caller**. It is registered in DI
(`IngestionModule.cs:18`) and invoked only by its own tests; `SubjectService.cs:13` describes the
backfill path that would use it in the future tense. `SampleListingProvider` reads the fixture JSON
directly and never touches the scraper.

So extending it does not produce the fixtures — the fixtures are static JSON either way. It is a
separate, deliberate improvement: the seam has only ever been proven against markup we wrote
ourselves, and real markup is in hand now.

Real markup differs from the mock in three ways that matter:

1. **Plan container** is `div.priceGridModelWrapper[data-rentalkey]`, not `div.plan-card`.
   Model name and attachment id live on a nested `.floorplanButton[data-modelname][data-attachmentid]`.
2. **Unit rows do not nest inside the plan.** `li.unitContainer[data-unit]` sits in a separate
   container and joins back via `data-modelkey` → the plan's `data-rentalkey`. The mock nests
   `tr.unit-row` inside `div.plan-card`. This is the largest structural difference.
3. **Plans are rendered twice** — once in the "All" tab, once in a bedroom-filtered tab. 40 wrappers,
   20 unique `rentalKey`s. Dedupe by `data-rentalkey` is mandatory or every plan doubles.

Also: plan images come from `data-background-image` on `.floorPlanButtonImage` (not an `img src`),
and `data-attachmentid="-1"` is a sentinel for "none" that maps to `null`, not the string `"-1"`.

`PlanScraperTests` runs against `Fixtures/multiplan-ldp.html`, which is mock markup **by design**.
Those cases are not retargeted. Real-markup cases are added **alongside** them, against a captured
Enzo LDP saved as a new fixture.

### `ScrapedUnit.Floor` is null

apartments.com does not publish a floor on unit rows. The mock's `class="unit-floor"` has no real
counterpart, and Enzo's unit numbers all share a leading `3` that tracks the street address
(*3100* Martin), not a floor.

Nothing computes on it: `NumerologyService.EvaluateUnit` derives verdict, adjustment, and note from
`unitNumber` alone and passes `floor` straight through to `UnitNumerologyAnnotation` as a display
field (`UnitAnnotationTests.EvaluateUnits_CarriesFloorFromScrapedUnit` pins exactly this). Deriving
a floor would be inference presented as scraped data, changing no output. So: `null`.

### `sample-listing.json` — 108 Ambiance is a house

It is a single-family rental: 4bd/4ba/3,698 sqft, no floor-plan grid, no `data-rentalkey` anywhere,
no unit number and no floor. Numbers read real-with-nulls:

```json
"numbers": { "unitNumber": null, "floor": null, "streetNumber": "108" }
```

`ListingNumbers` is all-nullable and `EvaluateSubject` skips blanks, so this is safe. Consequence,
accepted deliberately: the Numbers card renders one street-number check instead of three, because
`EvaluateSubject` already excludes the unit number by design.

45 photos exist; only 5 exteriors are in the initial DOM. Interiors are pulled from the gallery so
the room-by-room cards still have bedroom/kitchen/bathroom material.

### Fixture coverage that changes

The current fixture is hand-built to exercise edge paths. Two shift:

- **Imageless plan.** `rk-105` has `attachmentId: null, planImageUrl: null` deliberately
  (`TaskZeroCommand.cs:64` documents "rk-101..104 imaged, rk-105 deliberately imageless"). All 20
  real Enzo plans have images. Rather than distort a fixture that is supposed to be real, this
  coverage moves to a unit test over the imageless path, and the stale doc comment is corrected.
- **Unoriented plan.** Preserved naturally — `sample-orientation.json` is invented fixture data, so
  most plans stay unoriented and at least one keeps no orientation at all, so the Vastu/Kasō gate
  still demonstrates `insufficient_evidence`.

All 20 unique plans are taken, including the four "… Affordable M" variants. They are distinct
`rentalKey`s on the real LDP, and apartments-web chips key off `rentalKey`, so dropping them would
misrepresent the page.

### Id constants and their blast radius

`SampleListingProvider.ListingId` `"sample"` → `"tk93cec"`;
`MultiplanPropertyKey` `"sample-multiplan"` → `"349246f"`.

Call sites that follow the constants automatically: `ListingService`, `SearchService`,
`TaskZeroCommand`, `BackfillCommand`, `ApiContractTests:543,556`.

Call sites with the id **hardcoded**, which must be updated by hand:

- `wwwroot/mock-ldp.html:95,98` — `listing-id="sample"`
- `frontend/index.html:5,7` — `listing-id="sample"`
- `ApiContractTests.cs:30` — `private const string MultiPlan = "sample-multiplan"`
- `AnalysisPipelineTests.cs:125,126,199,203,363,364,382`
- `OrientationResolutionTests.cs:110,114,125,137` — plus plan keys `rk-101`, `rk-102`, `rk-105`

Self-contained and **staying green untouched** (they build inline arrays and never read the fixture
JSON): `DemoKeyingTests`, `UnitAnnotationTests`. Their doc comments reference fixture unit numbers
and become stale; comments are corrected, assertions are not.

`.harmoniq-local/store/` does not currently exist and is gitignored, so there are no stale report
bodies to clear. It regenerates on next run.

### Files touched

- `Data/sample-listing.json`, `Data/sample-multiplan-listing.json`, `Data/sample-orientation.json`
- `Data/sample-photos/*`, `Data/sample-plans/*` — real assets replace the invented ones
- `Services/SampleListingProvider.cs` — the two constants and the stale class doc comment
- `Services/PlanScraper.cs` — real selectors alongside mock ones
- `HarmonIQ.Tests/Fixtures/` — captured Enzo LDP as a new real-markup fixture
- `HarmonIQ.Tests/PlanScraperTests.cs` — real-markup cases added, mock cases untouched
- `wwwroot/mock-ldp.html`, `frontend/index.html`, and the hardcoded-id tests above

---

## Done means

- `dotnet test` green, with the two culture assertions **removed** rather than weakened.
- `grep -c Chinese wwwroot/embed/harmoniq-module.js` → `0`.
- The mock LDP renders against the Irvine fixtures.
- The summary records the scraped `rentalKey` values and the scraping approach taken.

---

## What the implementation changed about this plan

Four things turned out differently once the code was in hand.

**The `Culture` assertion at `TraditionRegistryTests.cs:183` had to be removed, not inverted.**
Inverting it — asserting the culture name is *absent* from the prompt — fails, because doctrine
bodies legitimately name related traditions by culture where they explain a divergence. That is the
content the boundary protects, so the assertion is gone rather than flipped.

**SPEC.md needed no edit.** It predates the five-tradition expansion, names only Feng Shui and
Vastu, and every "Chinese"/"Korean" mention in it is a numerology reading or NFR-8 cultural framing
— all explicitly out of bounds. `docs/ARCHITECTURE.md` was the only doc carrying the label as a
product surface.

**The imageless-plan coverage was already safe.** `AnalysisPipelineTests` seeds synthetic subjects
via `PlanSubject(rentalKey, imaged: false)` and never reads the fixture, so the null-plan-image path
keeps its coverage for free. Only `ApiContractTests` genuinely depended on a fixture plan being
imageless, and `SubjectService` re-reads `PlanImageUrl` from the plan source on every
materialization — so the condition cannot be arranged in the database. It is now created by
`ImagelessPlanSource`, a test-owned `IPlanSource` decorator that strips one plan's image, leaving
the fixture an honest capture.

**`ListingEnvironmentIngestionTests` collided with the new property key.** That in-flight test
targeted `349246f` as a *scraped* (non-fixture) property. Making Enzo the fixture meant
`GetPropertyEnvironmentAsync` short-circuited to the fixture branch and never reached the geo
lookup. The test now names a deliberately non-fixture key; the alternative — teaching the fixture
property to geocode — would have broken the "fixtures need no network" property of demo mode.

### Known, pre-existing, and untouched

The embed module posts to `/api/analyze`, which v2 replaced with `/api/refine`
(`AnalysisController.cs:35`). Both files are unmodified at `HEAD`, so the expanded report on the
mock LDP has been showing "Score unavailable" since before this work, for any listing id. The
fixture swap neither caused nor fixes it.
