# HarmonIQ v2 — Approved Design

**Date:** 2026-08-11 · **Status:** approved by user (skip-questions mode; recommended calls adopted)
**Inputs:** `docs/prompts/2026-08-11-v2-per-unit-orientation-search.md` (R1–R9, open questions, Appendix A), SPEC.md v1.6, docs/ARCHITECTURE.md, four research/design agent reports (SightMap capabilities, live-LDP data survey, constructive design pass, adversarial design review).

## 0. Empirical findings the design rests on

- **Plan identity:** every surveyed multi-plan LDP (11/11) exposes `data-rentalkey` + `data-modelname` + `data-attachmentid` on plan cards. Visible plan names are NOT unique (one property repeats "1 Bed 1 Bath" ~10×). Plan count is weakly correlated with unit count (342 units/6 plans vs 198 units/46 plans); one converted building has plan≈unit granularity.
- **Plan images:** 0/11 surveyed properties had a plan card without a real schematic image → the "plan without image" case is rare; treat as null-scored, confirm at scale in task zero.
- **Within-plan sqft varies** (e.g. "385–431 Sq Ft") — a plan is not perfectly uniform; nothing dimensional may be scored from a plan.
- **SightMap prevalence:** ~55% of surveyed multi-plan properties (6/11); ~0% of single listings. Correlates with newer/luxury product, not size.
- **Single listings:** no floor plans, real interior photos (1–35), unit numbers present → R1's evidence partition holds.
- **SightMap API:** real (`api.sightmap.com/v1`, API-key auth), unit↔floor↔building↔floor-plan linkage confirmed, `unit_number` joinable. **Unverified:** whether unit polygons are exposed as true-north geo-referenced vectors at the relevant product tier. Engrain demonstrably geo-references site plans (satellite overlay, pathfinding). Resolution is a CoStar↔Engrain partner data request, not reverse engineering.

## 1. Open questions — resolved

| # | Question | Call |
|---|---|---|
| 1 | Per-unit variation | Plan-level badge. Per-unit numerology (±3) **computed at read time, never persisted**; rendered as an annotation in the availability table, never a competing grade. Units leave the subject model. |
| 2 | Badge placement / report access | One bulk call per page: `GET /api/property/{key}/subjects` returns all plans' stored grades. Chip = button in the plan card stats line. Single fixed-position drawer inside the shadow root; one open at a time; Esc closes; focus returns to chip. |
| 3 | Plan without a floor-plan image | **Unscored/null — no chip, no placeholder.** Site+numbers-only would clone one grade across all plans. Rare per survey; task zero confirms. In search, an unscored plan never counts for or against its property. |
| 4 | SightMap vs geocode disagreement | Dissolved: **SightMap is the only orientation source.** Geocode never yields orientation. >45° footprint-bearing disagreement → data-quality log only. |
| 5 | Plan whose units face different ways | ≥80% of placed units in one cardinal sector → that is the plan's facing (confidence = concentration). Otherwise plan orientation = none → without-orientation path. Per-unit oriented detail in the drawer. |
| 6 | apartments-web tables | Child table `harmoniq_grade(listing_id, floor_plan_id NULL, principle_set, score, grade, cohort, confidence, engine_version, computed_at)` + two nullable headline scalars on the search-index row (`harmoniq_fengshui_best`, `harmoniq_vastu_best`). |
| 7 | Stored or live filtering | Stored. A vision call cannot sit in an SRP request path. |
| 8 | Report bodies | Object storage (`reports/{engine_version}/{subject}/{set}.json.gz`), `report_uri` + `report_sha256` on the analysis row. CDN-cacheable on drawer-open. |

## 2. Scoring model

### Per-tradition scores only
- No blended headline number, ever. `both` is a **UI union of two stored per-set rows**, never a stored score (the v1 shared-list site engine makes `both` non-decomposable).
- One **tradition-agnostic vision call** per subject records all findings tagged by tradition; filtering moves from prompt time to score time (halves the LLM bill vs per-set calls).
- `ElementBalance` is Feng Shui-only → nullable per set; the report omits the section when absent (never five zero bars).
- **v2 grades are not comparable to v1 grades.** Documented, accepted.

### Site engine arithmetic (replaces `70 + 5·adhering − penalties`)
The v1 form makes missing evidence flattering (all-unknown = clean B−) and gives evidence-rich paths higher variance. Replacement: per-set **normalized rule scoring** — severity-weighted fraction of *applicable* rules satisfied, applicability recorded per rule. A 3-rule and a 12-rule evaluation land on the same scale.

### Coverage-weighted aggregation with a confidence floor
```
score(set)      = Σ(wᵢ·cᵢ·sᵢ) / Σ(wᵢ·cᵢ)      wᵢ: interior .70, site .30
confidence(set) = Σ(wᵢ·cᵢ)                     cᵢ: lens rule-coverage ∈ [0,1]
confidence < 0.5 → status = insufficient_evidence, no grade
```
Missing evidence reduces a lens's weight, never its score. The floor makes Q3's null natural and keeps surviving grades honest.

### Vastu requires orientation (overrides renormalization for Vastu)
Without a facing, directional room placement / sleep orientation — the heavily weighted core of Vastu — cannot run; the leftovers are "Vastu with Vastu removed." Therefore:
- **Stored/filterable Vastu grades exist only where SightMap orientation resolved.**
- Elsewhere: "needs unit-placement data" state. The Refine drawer may compute a **session-only** Vastu score from renter-supplied orientation — displayed, never stored, never filterable.
- Feng Shui degrades gracefully without orientation (sha-chi + interior rules survive; armchair/bright-hall gate off through coverage).

### Cohorts, not disclaimers
Every score carries cohort `(evidencePath: photos|floorplan, orientationPath: with|without)`. **Ranking and filtering happen within cohort**, using per-cohort calibration constants stored on the engine version (derived from task zero's dual-scored sample; never computed live).

## 3. Floor-plan lens (new forced tool)

Schema: **`minItems: 0`** on findings/suggestions, explicit `not_determinable` marker, per-finding confidence. A forced call must be able to decline.

**In scope:** bath adjacent-to/over kitchen; bathroom door onto kitchen/dining; entry-to-rear straight line (chi rush); toilet sharing bed-head wall; center-of-unit obstruction (Brahmasthan, when boundary fully drawn); kitchen-at-entry; bed-wall options.
**Out of scope:** anything furniture-based (staging art), mirrors, beams, clutter, natural-light quality, five-element balance, anything dimensional, door swings, chirality-dependent (left/right) claims — **plans are mirrored for opposite building stacks**; findings must be adjacency-only.
Directional Vastu placement rules are an additional layer applied only when R2 supplies a facing; the lens never infers north from a drawing.

## 4. Orientation — SightMap seam

`IOrientationProvider` materializes `subject_orientation(facing_degrees, cardinal, source: sightmap|annotation|none, confidence, resolved_at)` per plan via the Q5 rule. Partner data request: per-unit polygon/exterior-wall assignment + building rotation-to-true-north. Fallback if vectors aren't true-north: one-time per-property rotation annotation from satellite imagery (`source: annotation`). Downstream is agnostic to which path filled it.

## 5. Data model (Appendix A, amended)

**Perception/judgment split (load-bearing):**
- `observations` — raw model output (findings, element balance, layout reading), keyed `(subject, evidence_hash, prompt_version, model_id)`. Expensive; invalidated only by evidence/prompt/model change.
- `analyses` — deterministic derivation from observations + site + numbers, keyed `(subject, principle_set, rules_version)`, **unique-constrained**. Cheap; an engine bump is batch SQL re-derivation, near-zero Claude cost.
- `rules_version` is **per principle set** (a Vastu change must not invalidate Feng Shui).
- Columns added to `analyses`: `interiors_coverage`, `site_coverage`, `confidence`, `cohort`; `status` gains `insufficient_evidence` (permanent, non-retryable, distinct from `failed`).

**Immutable input snapshots:** ingest writes an immutable `input_set` (evidence hashes, environment snapshot, orientation, numbers); fingerprint derives from the snapshot; scoring reads only the snapshot. Kills the ingest/scoring race.

**Other amendments:**
- Materialized `subjects` table (referential integrity; backfill enumeration source).
- Plan identity: primary key on scraped **`data-rentalkey`**; fallback content-signature = perceptual (not byte) image hash + beds/baths; **ambiguous match writes no row** (a wrong grade is worse than a null).
- Environment snapshots pinned with an explicit re-resolve cadence (grades must not drift with OSM edits under a frozen engine).
- **Demo output is never persisted:** projection writes require `mode='live' AND status='ok'`; demo is a read-path presentation.
- Units: no rows, no subject role (Q1). Numerology on read.
- Report bodies in object storage (Q8).

## 6. Pipeline

**Task zero: 1,000-property sampling job — a decision gate.** Measures: (a) plan-image coverage (validates Q3), (b) **within-property score variance across plans** — if the floor-plan lens creates no real variance, per-plan grades are cosmetic → fallback: property grade + per-plan layout notes, (c) per-cohort calibration constants (dual-scored subsample), (d) real cost per property.

**Backfill:** enumerate `subjects` → fingerprint check → `scoring_jobs` → **Claude Batch API**. No LLM summary in backfill (deterministic `LocalSummary`; narrative lazily on first report open). Unit economics at stated assumptions (100k properties; 60% multi-plan × ~6 plans × 1 floor-plan call; 40% single × ~5 photo calls; Sonnet batch pricing): **≈$7.3k batch / ≈$14.6k interactive**; task zero refines. **Prerequisite, not optimization:** internal listing + geo data (apartments-web already holds lat/lon and structured plan/unit data; `LISTING_SOURCE=api` is the seam). Public Nominatim/Overpass/scraping at backfill scale violates provider policies — dev/demo only. With internal sources <24h wall-clock; without them the backfill does not run.

**Incremental:** listing created/updated → fingerprint mismatch → interactive-path scoring in minutes.

**Publish by version flip:** projection rows written per engine version, published atomically when the version's run completes; SRP carries the version into the LDP request so badge and card agree. Projection rows are never mutated mid-rollout. Retries: 3 with backoff → `failed` → projection stays NULL (a failure is never a grade).

## 7. LDP surface (R9)

- Per-plan chip: ~22px inline-flex pill in the plan card stats line — grade only, brand accent, no chrome/gauge/bars/heading. Twelve read as a quiet column.
- **No layout shift by construction:** precomputed scores mean the bulk fetch resolves at first paint; the cold path renders an identical-footprint muted box (no spinner); known-unscored subjects render nothing from the start.
- Attribution **once per section**: "Scores provided by HarmonIQ" at the floor-plans section foot (Local Logic convention), link to `/harmoniq`.
- Single-listing card stays, slimmed to the visual weight of the Walk/Transit/Bike cards.
- Report drawer: single instance, `position: fixed` in shadow root, full v1 report richness, `aria-expanded`, focus trap, Esc.

## 8. Search (R4 + R7)

- Synonym map: `feng shui|fengshui|feng-shui → fengshui`; `vastu|vaastu|vasthu|vastu shastra → vastu`. Typeahead produces a suggestion chip → pre-filtered SRP with the HarmonIQ filter open and that set selected.
- Filter UI: parent "HarmonIQ" checkbox → per-set sub-selection. Parent-only default: any selected/either set at **B− or better within its cohort**, confidence floor applied.
- **Null grades are excluded from filtered results** (a filter is an affirmative request for a signal we have), with a visible count caveat ("N properties have HarmonIQ scores in this area") so unscored inventory is visibly absent, not silently dropped.
- SRP badges reuse the chip component.

## 9. Repo boundary (amends SPEC §7 / FR-6b)

**HarmonIQ publishes; apartments-web consumes.** HarmonIQ exposes a versioned grades feed/API; apartments-web owns its consumer, its additive-nullable migration, and the filter UI — behind a feature flag defaulting **off**, authored on a PR-ready branch. HarmonIQ never writes into apartments-web's database. Standing rule remains: **no push, no merge, no PR** until the apartments-web owners lift it; the boundary's new shape is recorded rather than drifting.

## 10. Cultural & legal guardrails

- Renter-side tradition filter = preference, defensible; publishing negative luck grades about named listings = the exposure.
- Confidence floor ⇒ no low grade rides thin evidence. No negative superlatives anywhere. NFR-8 framing everywhere.
- Grades stay client-rendered inside shadow DOM — never in title/meta, not indexable.
- Production publish requires a landlord-visible view and dispute path (recorded now, built later).
- The staging-advisor upsell must never compose into "pay us to fix the grade we published" — flagged, decision deferred.
- Filter to be reviewed as a potential steering/audience-segment issue before any production rollout.

## 11. Risk gates carried into the plan

1. Floor-plan lens creates real per-plan variance (task zero; fallback: property grade + layout notes).
2. SightMap exposes usable true-north geometry (seam + annotation fallback).
3. Vision genuinely reads LDP plan images at served resolution (task zero dual-scoring).
4. `data-rentalkey` stability across ingests (content-signature fallback; ambiguous → no row).
5. Internal listing/geo access materializes (backfill hard-gated on it).

## 12. Local implementation scoping (this machine, demo-grade)

The v2 build on this machine substitutes local stand-ins at the designed seams, without changing the seams:
- Persistence: SQLite (EF Core); object store: local filesystem directory. Both behind interfaces the design already requires.
- Orientation: fixture `IOrientationProvider` (sample data) + the SightMap client stub; no partner key exists.
- Ingestion: LDP scrape reads `data-rentalkey`/`data-modelname`/plan images/availability tables; `sample` fixture gains a multi-plan variant.
- Claude: demo mode remains fully functional (no key on machine; proxy sunsets Aug 12); live path preserved behind config.
- Search demo: mock SRP page served by the HarmonIQ backend (synonyms, filter, badges) — the apartments-web SRP work ships as PR-ready artifacts on the local `harmoniq-demo` branch only where its dev environment permits verification (a known Razor build anomaly is unresolved there); the mock host is the primary demo surface, mirroring v1.
