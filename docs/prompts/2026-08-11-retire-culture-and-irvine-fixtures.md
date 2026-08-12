# Prompt — HarmonIQ repo: retire `Culture`, move demo fixtures to the two Irvine listings

**Repo:** `HarmonIQ` · **Date:** 2026-08-11
**Companion prompt (other repo):** `docs/prompts/2026-08-11-apartments-web-culture-labels-chips-irvine.md`

---

## Paste this into a session in the HarmonIQ repo

You are working in the **HarmonIQ** repo. Two changes, independent of each other.

### Change 1 — retire the culture-of-origin label

Country/culture names are being removed from every renter-facing HarmonIQ surface. The label is
sourced from `ITradition.Culture`, so the field goes away rather than the strings being edited one
at a time.

Do this:

1. Delete `Culture` from `backend/HarmonIQ.Api/Services/Traditions/ITradition.cs` (line 26–27,
   including its doc comment, which currently justifies the field by "the SRP filter's
   `Korea — Pungsu-jiri` labelling" — that labelling is being removed).
2. Delete the `Culture` property from all five implementations: `FengShuiTradition`,
   `VastuTradition`, `PungsuTradition`, `KasoTradition`, `PhongThuyTradition`.
3. Drop the `culture` parameter from `InterpretPromptBuilder.Build`. The opening line becomes a
   tradition-only framing — e.g. `You are HarmonIQ, an expert consultant in {displayName}, a
   tradition of spatial harmony.` — instead of `…, the {culture} tradition of spatial harmony.`
   Update the five `.Build(DisplayName, Culture, Doctrine, …)` call sites.
4. Fix the tests this breaks:
   - `backend/HarmonIQ.Tests/TraditionRegistryTests.cs:37` — `Assert.False(IsNullOrWhiteSpace(t.Culture))`.
   - `backend/HarmonIQ.Tests/TraditionRegistryTests.cs:183` — `Assert.Contains(t.Culture, prompt)`.
     This one actively **asserts the culture name appears in the prompt**, so it must be removed or
     inverted, not adjusted.
   - `TraditionRegistryTests.AllFiveCulturesArePresent_InDisplayOrder` (line 14) — rename; it is
     really asserting five traditions in display order, which still holds.
5. `frontend/src/api.ts:17-22` — drop `culture` from the `TRADITIONS` table and its type.
   `frontend/src/components/RefineDrawer.tsx:126` uses it as the `title` tooltip on the tradition
   selector; fall back to the tradition label.
6. Rebuild the embed bundle. `backend/HarmonIQ.Api/wwwroot/embed/harmoniq-module.js` is a **built
   artifact** with the culture strings baked in — editing the TypeScript alone leaves the old
   strings being served.
7. Update `SPEC.md` and any doc comment that describes the culture label as a product surface.

**Boundary — do not overreach.** Only the *display label* is retiring. Leave alone:

- Doctrine bodies (`PungsuTradition.cs:155`, `KasoTradition.cs:141`, `PhongThuyTradition.cs:131`,
  etc.) that reference related traditions by culture. That is doctrinal content explaining where
  traditions diverge, and the divergence is the point.
- Numerology note strings ("Contains 4 (사), which shares its sound with the hanja 死…",
  "in Chinese numerology…"). These are the tradition's own reading, not a label on the tradition.
  `NumerologyServiceTests` and `TraditionDivergenceTests` assert on them and should stay green
  untouched.

If a change would require editing `NumerologyServiceTests` or `TraditionDivergenceTests`, you have
gone too far — stop and re-read this boundary.

### Change 2 — move the demo fixtures to the two Irvine listings

Replace the invented Arlington fixtures with real data scraped from these two apartments.com pages:

| Fixture | Listing | Key | Role |
|---|---|---|---|
| `Data/sample-multiplan-listing.json` | https://www.apartments.com/enzo-irvine-ca/349246f/ | `349246f` | multi-plan — a grade chip per floor plan |
| `Data/sample-listing.json` | https://www.apartments.com/108-ambiance-irvine-ca/tk93cec/ | `tk93cec` | single listing — the compact score card |

Subjects stay **per floor plan**, as today. "Grade per unit" in the original request meant "a grade
on each row of the floor-plan grid" — do not change the subject model.

Scope of the swap:

- `Data/sample-listing.json` — real title, address, listing URL, unit/floor/street numbers, and real
  photos into `Data/sample-photos/`.
- `Data/sample-multiplan-listing.json` — real plan `rentalKey`, `modelName`, beds/baths/sqft and
  availability rows; real plan images into `Data/sample-plans/`.
- `Data/sample-orientation.json` — rekey to the new property key and the new plan rental keys. It is
  fixture orientation data, so invent plausible facings, but keep at least one plan with **no**
  orientation so the Vastu/Kasō gate still demonstrates `insufficient_evidence`.
- `Services/SampleListingProvider.cs` — the `ListingId` / `MultiplanPropertyKey` constants.
- `wwwroot/mock-ldp.html:95,98` — `listing-id="sample"`.
- `.harmoniq-local/store/` holds reports keyed by the old ids (`sample-multiplan:rk-101`, …). Clear
  and regenerate rather than leaving stale bodies behind.

**Two things that will bite you:**

1. **`PlanScraper` does not parse apartments.com markup.** Its regexes
   (`Services/PlanScraper.cs:22-51`) target the *mock* LDP's markup — `class="plan-card"`,
   `class="unit-row"`, `data-rentalkey`. The live pages do not look like that. Decide up front
   whether you are extending the scraper with real selectors or capturing the page HTML once and
   converting it by hand into the fixture JSON. Either is fine; pick one and say which.
2. **Do not break `PlanScraperTests`.** They run against
   `backend/HarmonIQ.Tests/Fixtures/multiplan-ldp.html`, which is mock markup by design. If you
   extend the scraper, add real-markup cases alongside the existing ones — do not retarget them.

The real `rentalKey` values matter beyond this repo: apartments-web's chips key off the LDP's
`data-rentalkey`, so record the exact values you scrape in your summary.

### Done means

- `dotnet test` green, with the two culture assertions removed rather than weakened.
- The rebuilt embed bundle contains no culture strings (`grep -c Chinese wwwroot/embed/harmoniq-module.js` → 0).
- The mock LDP renders against the Irvine fixtures.
- Your summary lists the scraped `rentalKey` values and which scraping approach you took.

---

## Why this split

`Culture` never crossed the wire — it is absent from `ProjectionRow` and `SetGrade`, and
apartments-web keeps its own copy in `TraditionDisplay.cs`. So the two repos can retire it
independently, in either order, with no contract change and no coordination window.
