# Prompt — HarmonIQ v2: per-floorplan scoring, orientation gating, culture-specific grades, search & persistence

> Paste the block below into a fresh Claude Code session started in `/Users/achiuwei/Documents/HarmonIQ`.

---

You are working on **HarmonIQ**, in `/Users/achiuwei/Documents/HarmonIQ`. Read `SPEC.md` (v1.6) and
`docs/ARCHITECTURE.md` before you respond — they describe the current build accurately. Relevant current state:

- One score per **listing** (`listingId` = apartments.com `PropertyKey`), rendered as one score card by
  `frontend/src/components/HarmonIQBadge.tsx` / `ReportPanel.tsx`.
- Three lenses merged in `Services/ScoreMath.cs`: rooms (Claude vision, 70%) + site (`SiteAnalysisService`, 30%)
  ± numerology (`NumerologyService`, ±3).
- `orientation` is a free `string` on `AnalyzeRequest` (`Models/AnalysisModels.cs`), defaulted to `"unknown"` and
  hand-set in `RefineDrawer.tsx`. `SiteAnalysisService` already splits rules into orientation-independent
  (sha-chi roads) and orientation-dependent (armchair, bright hall, overshadowing, water placement).
- `systems` is a single tri-state (`both | fengshui | vastu`) producing **one** blended score.
- **No persistence at all** — `IMemoryCache`, 30-minute TTL, nothing durable (SPEC NFR-7).
- The apartments-web integration is two lines on a local-only `harmoniq-demo` branch (SPEC FR-6b, ARCHITECTURE §8.3).

This is the **v2 scope**. It deliberately breaks several v1 constraints (no persistence, single score, local-only
host change) — that is intended; treat SPEC v1.6 as the baseline to amend, not as a rule to obey.

## Scope of this work

Two codebases:

1. **HarmonIQ** (this repo) — scoring engine, API, embed module, persistence, batch scoring.
2. **apartments-web** — search filter UI, search-results badges, and the DB migration for the grade columns.
   Note that SPEC §7 / FR-6b currently forbids merging anything into apartments-web. Part of your job is to tell
   me what that boundary should become for v2 (still local-only? feature-flagged branch? PR-ready?) — do not
   silently change it, and do not push, merge, or PR anything in apartments-web.

## Data sources — four separate things, do not conflate them

**The floor plan and the Engrain SightMap are different data sources with different coverage.** A property having
floor plan images tells you nothing about whether it has SightMap data, and vice versa. Treat availability of each
as an independent fact per property.

| Source | Grain | What it gives HarmonIQ | Coverage |
|---|---|---|---|
| **Floor plan image** | Per plan ("Plan 8", "Plan 19") | Interior **layout**: room adjacency, entrance, kitchen/bath position, bed walls, window placement. Says nothing about which way the unit faces. | Multi-plan properties; **single listings usually have none** |
| **Unit availability table** | Per unit, nested under a plan | **Unit numbers** (6360, 7147, 3235, 2113 …) and per-unit price/sqft/availability. This is the numerology input. | Wherever plans are listed |
| **Engrain SightMap** | Per unit | **Where the unit physically sits** in the building — position, floor, and therefore facing/**orientation**. Nothing about interior layout. | Some properties only |
| **Geo prefill** (`GeoContextService`) | Per property | Surroundings on each side: roads, water, structures, slope. | Wherever geocoding succeeds |

Shape of the floor plans section on the LDP, for reference: each **plan** card shows a floor plan thumbnail, bed/
bath, a sqft figure or range ("1,091 – 1,204 Sq Ft"), price or "Starting at", deposit, and *Floor Plan Details* /
*Tour Floor Plan* links; beneath it an **N Available Units** table listing unit number, base price, sqft,
availability, with a "Show More Units" expander. Note that units under one plan can differ in sqft (1,175 vs
1,133) — a plan is not perfectly uniform, so say what that means for a per-plan score.

## Requirements

### R1 — Score per floor plan, not per property

A property (`BuildingProfile`) usually has several floor plans, each with many units. Scoring the property as a
whole is wrong when its floor plans differ.

- Compute and surface a HarmonIQ score **per floor plan**.
- **The discriminator is the property's count of distinct floor plans — not its unit count.** A plan with one
  available unit on a property that has other plans is still the multi-plan case (in the reference screenshot,
  "Plan 8 · 1 Available Unit" sits alongside "Plan 19 · 10 Available Units" — Plan 8 takes the floor-plan path).
  Define precisely what "distinct floor plan" means against the real data before writing the branch.
- **Multi-floorplan properties:** render the score as a **badge on each floor plan** (in the floor plans /
  availability section) and **suppress the single property-level score card** entirely.
- **Single-floorplan properties** (one plan, or no per-plan breakdown at all — a house, condo, or single-unit
  rental): keep today's behavior exactly — photo-driven room analysis, one score card beneath the "Getting
  Around" scores. Here the property's photos *are* the unit's photos, so the attribution problem below does not
  arise. Single listings **usually have no floor plan at all**, so photos are not merely the better evidence
  there, they are the only evidence.
- Note that the available evidence partitions along the same line as this rule, which is what makes the branch
  robust rather than arbitrary: multi-plan properties have a per-plan floor plan image and only property-level
  photos; single listings have unit-level photos and typically no floor plan. Each path uses the evidence that
  actually exists at its grain.
- **For properties with more than one floor plan, do not use the photos to compute the score — score from the
  floor plan only.** Listing photos on these properties are property-level marketing shots that cannot be
  attributed to a specific plan, so feeding them into a per-plan score would attribute one unit's interior to
  another's grade. The floor plan image is the only evidence that is actually per-plan.
- This replaces the 70% interiors lens for these properties. Design the floor-plan lens: a vision call over the
  floor plan image reading **layout** — room adjacency, entrance placement and what faces it, kitchen/bathroom
  position relative to bedrooms, bed-wall options, corridor and window placement — with its own forced-tool JSON
  Schema alongside `record_room_analysis` in `Services/Prompts.cs`.
- The floor plan gives layout in the plan's own arbitrary frame — **it carries no compass information**. Rotating
  it to true north requires SightMap orientation (R2), which is a separate source with separate coverage. So the
  floor-plan lens must produce useful rotation-independent findings on its own (adjacency, entrance relationships,
  bath-over-kitchen, bed placement options), and treat directional room-placement rules — the heavily weighted
  part of Vastu — as an *additional* layer available only when R2 supplies a facing. Do not gate the whole lens on
  orientation, and do not infer a facing from the drawing.
- Consequences to work through: `ScoreMath`'s `0.7·rooms + 0.3·site` has no `rooms` term on this path — define the
  replacement weighting; `RoomCard`/`ElementBars` in the expanded report are photo-driven, so specify what the
  report body shows for a floor-plan-scored subject; and photo-scored and floor-plan-scored grades must stay
  comparable to each other (same problem as R3, and it compounds — a property can be floor-plan-scored *and*
  orientation-less).

### R2 — Orientation only where Engrain SightMap data exists

- Derive a unit's **orientation from the Engrain SightMap** where the property has it. SightMap is the *only*
  orientation source — floor plan images are not one (R1), and the presence of floor plans must never be read as
  the presence of orientation.
- Orientation is fundamentally **per unit**, not per plan: two units of the same plan on opposite sides of the
  building face opposite directions. Decide what a plan-level badge (R1) can honestly claim when its units'
  orientations disagree — a range, the most common facing, no orientation at all, or a per-unit score in the
  availability table.
- Where there is no SightMap data, **drop orientation entirely** — do not guess, do not default to a direction,
  and do not let the Refine drawer's manual orientation silently become the production path.
- Show a **disclaimer** wherever a score was computed without orientation, stating that orientation was not
  available for this property and is excluded from the calculation. Write the copy; keep it short and factual.
- Investigate what SightMap actually exposes (geometry, unit polygons, building rotation, north reference) and
  what HarmonIQ can legitimately derive from it. If access is a blocker, say so and propose the seam.

### R3 — Two scoring paths: with and without orientation

`SiteAnalysisService` already separates orientation-dependent from orientation-independent rules. v2 needs this to
be an explicit, tested branch:

- **With orientation:** full rule set.
- **Without orientation:** orientation-independent rules only, with the site score **renormalized** so that a
  property is not penalized for missing data — a missing input must not read as a bad property.
- The two paths must produce **comparable, honest** grades. Say explicitly in the model (and in the API response)
  which path produced a given score, and cover both in `HarmonIQ.Tests/SiteAnalysisServiceTests.cs` and
  `ScoreMathTests.cs`.
- This is one of **two** independent path axes — orientation (here) and evidence (photos vs floor plan, R1). Four
  combinations exist; treat them as one matrix rather than two unrelated branches, and test the corners.

### R4 — Users can search for "feng shui" / "vastu shastra"

- A renter typing "feng shui" or "vastu shastra" — and reasonable variants ("fengshui", "vaastu", "vasthu") —
  reaches HarmonIQ-filtered results.
- Specify where this hooks in: query synonym/keyword mapping, the filter's own label and sub-options (R7), and
  whether the term produces a search suggestion, a redirect to a pre-filtered SRP, or both.
- ARCHITECTURE §7 "Cultural framing" stays as-is: verdicts remain attributed to a named tradition ("in Chinese
  numerology…") rather than stated as objective claims about safety, health, or value.

### R5 — Culture-specific scores

- Store and serve a **separate score per principle set** (Feng Shui, Vastu Shastra, and any future set) instead
  of one blended number gated by the `systems` tri-state.
- Define what the property-level/floor-plan-level "HarmonIQ grade" is when multiple sets apply — separate grades
  only, or a headline number plus per-set breakdown? Recommend one.
- Keep the ±3 numerology adjustment tradition-scoped as it already is.

### R6 — Nullable grade columns

- Add **nullable** HarmonIQ grade/score columns so that "not yet scored" is distinguishable from "scored badly".
  Null must never render as `F`, `0`, or an empty card.
- Columns must cover R1 (per floor plan) and R5 (per principle set). Propose the schema — including where it
  lives, keying (shared network listing ID per ARCHITECTURE §8.1), a scoring-engine **version** column, and a
  scored-at timestamp so results can be invalidated when rules change.
- HarmonIQ has no database today. Specify the persistence layer, migration path, and how the API reads through
  cache → store → compute. Appendix A is a starting point, not a settled design — challenge it.

### R7 — Search filter UI

- A general **"HarmonIQ" checkbox** in the search filters, which when checked reveals **sub-selection of the
  actual principles/sets to apply** (per R5).
- Define default behavior when only the parent checkbox is ticked, how the filter interacts with null grades
  (R6 — unscored listings must not be silently dropped or silently included; pick one and say why), and whether
  search results carry a badge.

### R8 — Precompute at scale

- Score **a large batch of existing listings up front**, then score **each new listing as it is added**.
- Design the pipeline: batch backfill job, incremental trigger on new/updated listings, re-score trigger when the
  engine version changes, concurrency and Claude rate/cost limits (today: ≤9 concurrent vision calls per
  analysis, one call per photo), failure/retry semantics, and what happens to a listing whose photos change.
- Give me a cost and wall-clock estimate for the backfill at realistic inventory size, and state the assumptions.

### R9 — Small and minimal on the listing page

**The score and the "Data provided by HarmonIQ" link must be small and minimal on the apartments.com LDP.** This
is a secondary signal on someone else's page, not a feature block. Today's badge is a full vendor-style score
card; per-plan scoring (R1) multiplies it by the number of plans, and a dozen score cards down the floor plans
section would take the page over.

- **Per-plan badge:** a compact grade chip sitting inside the existing plan card — near the bed/bath/sqft stats
  line — with no card chrome, no gauge, no element bars, and no separate heading. Twelve of them down the page
  should read as a quiet column of chips. It must not increase the plan card's height or push the availability
  table down.
- **Attribution once per page, not once per badge.** Twelve repetitions of "Data provided by HarmonIQ" is noise;
  the link belongs in a single small line for the section, following the LDP's existing "Scores provided by
  [Local Logic]" convention. Keep the link — it is the attribution requirement (SPEC FR-3) — just stop repeating it.
- **Single-listing card:** the property-level card stays, but no heavier than the Walk/Transit/Bike cards beside
  it. It should not be the loudest thing in the scores region.
- **Reserve space before the score arrives.** Scores fill in asynchronously; the badge must occupy its final
  footprint from first paint so nothing on the host page reflows when a grade lands. The loading state has to be
  as quiet as the resolved state — no spinner multiplied by twelve.
- Depth lives behind the expansion, not on the surface: the full report keeps its current richness when opened.
- Files: `HarmonIQBadge.tsx`, `ReportPanel.tsx`, `styles/tokens.css`.

## Constraints

- Host-page guarantees from ARCHITECTURE §8.5 still hold: shadow DOM, `defer`, no host breakage, no hotlinking,
  no credentials client-side.
- Deterministic engines stay deterministic — the Refine drawer's predictable re-grade depends on it.
- Never render a null/unscored state as a bad score.
- No layout shift on the host page, ever — a score arriving must not move anything (R9).
- Do not push, merge, or open a PR against apartments-web.
- The hackathon Claude proxy shut down Aug 12, 2026 — assume live mode needs a repointed endpoint and that demo
  mode must keep working.

## Open questions I expect you to raise with me (don't answer them silently)

1. Does the per-floorplan score vary by **unit** at all? Unit number and floor drive numerology (R5/§3.4), so two
   units in the same floor plan can differ. Floor-plan badge + unit-level adjustment, or floor plan only?
2. Where exactly does the floor plan badge live in `BuildingProfile`, and what is the loading state when a page
   shows 12 floor plans at once? (Today one badge = one analysis on intersection.) Related: with the badge
   reduced to a chip (R9), how does a renter open the full report from it — inline under that plan, a drawer, a
   modal — and can more than one be open at a time?
3. **A plan on a multi-plan property that has no floor plan image.** That subject has no interior evidence at
   all — photos are barred by R1 and there is no drawing. Options: score from site + numerology only with the
   R3-style renormalization and a disclosure, or leave it **unscored/null** (R6 exists precisely to express
   this). Whichever we pick has to hold for the search filter too (R7) — an unscored plan is not a bad plan.
   Tell me how common this is in the real data before we choose.
4. What is authoritative for orientation if SightMap and the geocode disagree?
5. When a plan's units have **different** SightMap orientations, what does the plan-level badge claim? (R2 — this
   is the one place where per-plan scoring and per-unit orientation genuinely fight each other.)
6. Which apartments-web table(s) and migration process for R6 — and does v2 lift the local-only boundary?
7. Is search filtering on a *stored* grade (fast, stale-tolerant) or live? R8 implies stored.
8. Does the full report body live in the database or in object storage? (See Appendix A — at network inventory
   scale the JSON dominates everything else the schema holds.)

## What to produce, in order

1. **Brainstorm with me first.** Use `superpowers:brainstorming`. Work through the open questions above one at a
   time — do not write a spec or code until we have resolved them. Push back where a requirement is
   underspecified or where two requirements conflict (R3's with/without-orientation comparability, R5's
   headline-vs-breakdown grade, R7's handling of null grades in a filtered result set).
2. **Update `SPEC.md` to v2** — amend the affected FRs and NFRs (notably NFR-7 "no persistence" and §8 out-of-scope
   list, which currently exclude search filters, persistence, and floor-plan/orientation inference), and record
   what changed and why.
3. **Write a phased implementation plan** in `docs/superpowers/plans/`, matching the format of
   `2026-08-10-harmoniq-ldp-module.md`: numbered tasks, exact file paths, per-task verification, and an explicit
   split between HarmonIQ-repo tasks and apartments-web tasks.

Do not write implementation code until I approve the plan.

---

## Appendix A — What the HarmonIQ database holds (starting point)

Two stores, different jobs. Getting this split right is most of R6.

| Store | Role |
|---|---|
| **HarmonIQ DB** | System of record. Everything needed to render a report and to decide whether a stored score is still valid. |
| **apartments-web columns** | Read-model projection. Nullable scalars only, denormalized for search filtering and SRP badges. Never the source of truth; rebuildable from HarmonIQ. |

### Subject model

Scores attach to a **subject**, which is a floor plan when the property has several and the property itself when
it doesn't (R1). Numerology attaches to a **unit** (R5, open question 1). Keep one polymorphic
`(subject_type, subject_id)` rather than three parallel score tables.

### Tables

**Identity and inputs** — what was analyzed:

- `properties` — `property_key` (PK, the network-shared listing ID from ARCHITECTURE §8.1), address, lat/lon,
  geocode status, source URL, `last_ingested_at`. Brand-agnostic: one row serves every brand's LDP.
- `floor_plans` — `property_key`, external plan ID, plan name ("Plan 19"), beds/baths, sqft **range**, floor plan
  image URL and its **content hash**. On multi-floorplan properties this image is the sole interior evidence (R1),
  so its hash is the invalidation key that photo hashes are for single-floorplan properties. No SightMap fields
  belong on this table.
- `units` — `floor_plan_id`, unit number, floor, sqft, availability. Sourced from the plan's availability table.
  Unit number is the numerology input (R5).
- `unit_placements` — the **SightMap** projection, keyed by unit and kept deliberately separate from
  `floor_plans`: SightMap unit ID, building/level, position geometry, derived facing degrees. A unit having a row
  here is independent of its plan having a floor plan image; either can exist without the other, and the schema
  should make an absent row the natural representation of "no SightMap for this property".
- `photos` — `property_key`, nullable `floor_plan_id`, source URL, **content hash**, `is_interior`, room type,
  selected. The hash is what tells you a listing's photos changed (R8). Stored for every property, but only fed
  into scoring on the single-floorplan path (R1).
- `property_environment` — the four sides of road/water/structures/slope that `GeoContextService` derives, plus
  per-side `unknown` flags, source, resolved-at. Cacheable for far longer than 30 minutes; it changes with the
  neighborhood, not the listing.
- `subject_orientation` — the *resolved* facing per scoring subject, materialized from `unit_placements` by
  whatever rule R2 settles on for a plan whose units disagree: facing degrees, cardinal, `source`
  (`sightmap` | `none`), agreement/confidence, resolved-at. This is what selects the R3 scoring path, so store it
  explicitly rather than recomputing it at read time — and keep it distinct from the raw SightMap rows it derives
  from, so a change in the resolution rule is a re-materialization rather than a re-ingest.

**Outputs** — what was computed:

- `analyses` — one row per `(subject, principle_set, engine_version)`: `overall_score`, `grade`,
  `orientation_path` (`with` | `without`), `evidence_path` (`photos` | `floorplan`, per R1), component scores
  (`interiors_score`, `site_score`, `numerology_adj`), element balance, summary text, `mode` (`live` | `demo`),
  `model_id`, `computed_at`, `status` (`pending` | `ok` | `failed`), and **`input_fingerprint`**.
- `analysis_reports` — the report body: room cards, findings, violations, suggestions. Render-only, never queried
  by predicate, so a single JSON document per analysis beats normalizing findings into rows. At network inventory
  scale this is the one large thing in the schema (see open question 6).

**Control plane** — what makes it re-runnable:

- `engine_versions` — version, rules hash, prompt hash, model ID, activated-at. Bumping a row is the re-score
  trigger for R8.
- `scoring_jobs` — subject, `reason` (`backfill` | `new_listing` | `photos_changed` | `engine_upgrade`), status,
  attempts, last error, timings, tokens/cost. This is also where the backfill cost estimate gets measured against
  the estimate.

### The load-bearing column

`input_fingerprint` = hash of (the evidence hashes for this subject's path — floor plan image on the floor-plan
path, selected photo hashes on the photo path — plus environment, orientation, numbers, `engine_version` and
`principle_set`). Recompute the fingerprint on ingest; if it matches the stored one, skip the analysis. Note the
practical win: on the floor-plan path a property's marketing photos can churn constantly without invalidating a
single score, because they were never inputs. That single column answers "what happens when photos change", "how do we avoid re-scoring 100k listings
for nothing", and "is this grade stale" — without it, R8 has no cheap idempotency check.

### Nullability

The HarmonIQ DB expresses "not scored" as **no `analyses` row** (or `status = 'pending'`). The nullable columns
of R6 live on the apartments-web side, where a NULL means exactly "HarmonIQ has nothing for this subject yet" —
distinct from a stored low score. Propose whether the projection is one child table
(`listing_id, floor_plan_id, principle_set, score, grade, orientation_path, engine_version, computed_at`) plus a
denormalized headline column for coarse filtering, or a fixed set of per-set columns; the child table scales to
new principle sets without a migration, the columns index better.

### Explicitly not in the database

- **Photo bytes.** SPEC NFR-5 keeps photos in memory, downscaled, never on disk. v2 needs hashes and URLs, not
  pixels — if serving thumbnails from a store becomes necessary, that is an object-storage decision, not a DB one.
- **Claude credentials**, which stay server-side environment config.
- **Renter identity of any kind.** There are no accounts, and a filter for culturally-minded renters is not a
  reason to start recording who used it.
