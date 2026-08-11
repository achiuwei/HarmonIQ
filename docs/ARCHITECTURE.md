# HarmonIQ — System Architecture (v2)

**Status:** v2 in build · **Spec:** [SPEC.md](../SPEC.md) v2.0 · **Design:** [v2 approved design](superpowers/specs/2026-08-11-harmoniq-v2-design.md) · **Plan:** [v2 implementation plan](superpowers/plans/2026-08-11-harmoniq-v2.md)

> **This document describes the v2 target architecture.** §11 states honestly what is built today versus
> what is still designed-only. Where this document and the design doc disagree, **the design doc wins**.
>
> **v2 grades are not comparable to v1 grades.** The score arithmetic, the subject, and the aggregation rules
> all changed. A v1 "B+" and a v2 "B+" are different measurements.

HarmonIQ grades apartments against **Feng Shui** and **Vastu Shastra** and surfaces the result on the host
listing page. v1 was a stateless, one-score-per-listing, analyze-on-view demo. v2 is a **precomputed, persisted,
per-floor-plan, per-tradition scoring system** with an orientation seam, a floor-plan vision lens, a versioned
grades feed, and search integration.

---

## 1. What changed from v1, and why

| v1 | v2 | Reason |
|---|---|---|
| One score per listing | One score per **subject** — a floor plan on multi-plan properties, the property on single listings | A 342-unit property with 6 plans has 6 different layouts; one grade for all of them is a fiction |
| One blended headline number (`0.7·rooms + 0.3·site ± numerology`) | **Two stored per-tradition scores, never blended.** `both` is a UI union of two rows | The v1 shared-rule-list engine makes a blended number non-decomposable — you cannot say what it measured |
| `70 + 5·adhering − penalties` | **Normalized rule scoring**: severity-weighted fraction of *applicable* rules satisfied | The v1 form made missing evidence flattering (all-unknown = a clean B−) and gave evidence-rich listings higher variance |
| Missing evidence silently absorbed | **Coverage-weighted aggregation + confidence floor (0.5)** → `insufficient_evidence`, no grade | Missing evidence must reduce a lens's *weight*, never its *score* |
| Analyze on view, per renter | **Precompute, store, publish by version flip** | A vision call cannot sit in an SRP request path, and 100k properties cannot be scored per-view |
| Orientation guessed / renter-supplied | **Engrain SightMap is the only orientation source**; Vastu requires it | Geocoding never yields orientation; Vastu without a facing is "Vastu with Vastu removed" |
| Numerology folded into the score | **Per-unit annotation computed at read time, never persisted, never a grade** | Units are not subjects; a unit number must not compete with the plan's grade |
| Interiors from photos only | Two mutually exclusive **evidence paths**: `floorplan` (multi-plan) or `photos` (single listing) | Marketing photos on a multi-plan property belong to no particular plan |
| No storage | EF Core + SQLite (Postgres-shaped), object store for report bodies | Everything above depends on persistence |

---

## 2. System context

```mermaid
graph LR
  R([Renter]) -->|SRP search / LDP view| HOST[Host surface<br/>mock LDP · mock SRP ·<br/>apartments-web LDP local]
  HOST -->|3 custom elements| EMB[harmoniq-chip<br/>harmoniq-section<br/>harmoniq-module]
  EMB <-->|bulk /api/property/{key}/subjects<br/>/api/subject/../report<br/>/api/refine| API[HarmonIQ API<br/>ASP.NET Core :5080]
  API --> DB[(SQLite / EF Core<br/>subjects · observations ·<br/>analyses · projection rows)]
  API --> OS[(Object store<br/>gzipped report bodies)]
  API -->|IOrientationProvider| SM[(Engrain SightMap<br/>stubbed · fixture locally)]
  API -->|listing + geo| SRC[(Listing source<br/>scrape now · internal API at scale)]
  API -->|Messages API, forced tools| CLAUDE[(Claude<br/>interactive · Batch at scale)]
  AW[apartments-web] -->|GET /api/feed/grades?engineVersion=| API
```

Three properties still drive every decision:

| Property | Consequence |
|---|---|
| **Zero input from anyone** | The host page supplies a property key. Everything else is derived. |
| **Guest on someone else's page** | Shadow DOM both directions, `defer`, **no layout shift by construction**, fail-inert. |
| **Never dead-ends, never lies** | Demo mode always renders; an absent grade renders as an *explained absence*, never as `F`, `0`, or empty bars. |

---

## 3. The subject model

Scoring attaches to a **subject**, not a listing.

```
property has ≥2 distinct floor plans  →  one `floorplan` subject per plan, and NO property subject
property has 0 or 1 floor plan        →  one `property` subject
units                                 →  never subjects, never rows
```

- **Discriminator is the plan count, not the unit count.** A plan with one available unit alongside four other
  plans is still the multi-plan case.
- **Identity is the scraped `data-rentalkey`** (present on 11/11 surveyed multi-plan LDPs). Visible plan names
  are *not* unique — one surveyed property repeats "1 Bed 1 Bath" ~10×. Fallback identity is a
  **perceptual** image hash + beds/baths; an **ambiguous match writes no row** (a wrong grade is worse than a null).
- **A plan with no floor-plan image is not scored at all** — no observation, no analysis row, no chip, no
  placeholder. Site+numbers alone would clone one grade across every plan on the property. Such a subject is still
  returned by the API (with an empty `sets` list) so the page's footprint is known at first paint.

---

## 4. Perception / judgment split

This is the load-bearing structure of v2. Model output and scores are separate tables with separate lifetimes.

```mermaid
graph TD
  IN[InputSet — immutable snapshot<br/>evidence hashes · environment · orientation · numbers] --> P
  P[Perception: ONE tradition-agnostic vision call per evidence item<br/>findings tagged by tradition] --> OBS[(observations<br/>key: subject, evidence_hash, prompt_version, model_id)]
  OBS --> J[Judgment: deterministic per principle set<br/>filter by tradition → RuleOutcomes → site rules →<br/>VastuGate → coverage/confidence → calibrate → band]
  IN --> J
  J --> AN[(analyses<br/>unique: subject, principle_set, rules_version)]
  AN --> RB[report body → object store<br/>reports/{engine}/{subject}/{set}.json.gz]
  AN --> PR[(projection rows<br/>published by version flip)]
```

| Layer | Cost | Invalidated by | Consequence |
|---|---|---|---|
| `observations` | Expensive (Claude) | evidence hash, prompt version, model id | Re-scoring after a rule change costs ~nothing |
| `analyses` | Cheap (pure C#) | `rules_version` (**per principle set**) | An engine bump is batch SQL re-derivation |

Two decisions fall out of this and are worth stating plainly:

- **One tradition-agnostic call, not one call per tradition.** The model records *all* findings tagged
  `fengshui | vastu | both`; tradition filtering moves from prompt time to score time. This halves the LLM bill.
- **`rules_version` is per set.** A Vastu rule change must not invalidate Feng Shui analyses.

**Immutable input snapshots.** Ingest writes an `InputSet` — evidence hashes, environment snapshot, resolved
orientation, unit numbers — and scoring reads *only* the snapshot. The fingerprint derives from the snapshot.
This kills the ingest/scoring race and makes "did anything actually change?" a hash comparison.

---

## 5. Scoring arithmetic

### 5.1 Normalized rules

```
Score01(lens, set) = Σ(severityᵢ · satisfiedᵢ) / Σ(severityᵢ)     over APPLICABLE outcomes only
Coverage(lens,set) = applicable rules / evaluable rules            ∈ [0,1]
```

A 3-rule evaluation and a 12-rule evaluation with the same satisfied fraction land on the same scale.
Unknown environment values produce `Applicable = false` — never a violation.

### 5.2 Coverage-weighted aggregation

```
score(set)      = Σ(wᵢ·cᵢ·sᵢ) / Σ(wᵢ·cᵢ)        w: interiors .70, site .30
confidence(set) = Σ(wᵢ·cᵢ)
confidence < 0.5 → status = insufficient_evidence, score = null, grade = null
```

Missing evidence reduces a lens's **weight**, never its score. The floor makes the unscored case natural and
keeps surviving grades honest. `NotDeterminable` from the floor-plan lens → `Coverage = 0` → insufficient
evidence, **not** a low score.

### 5.3 The Vastu gate

`VastuGate.CanScore` returns false for Vastu when orientation is absent, and this **overrides renormalization**:

- Stored, filterable **Vastu grades exist only where SightMap orientation resolved.**
- Elsewhere the UI shows a short factual "orientation data isn't available for this property" state.
- The Refine drawer may compute a **session-only** Vastu score from renter-supplied orientation — displayed,
  never persisted, never filterable.
- **Feng Shui degrades gracefully** without orientation: sha-chi and interior rules survive; armchair/bright-hall
  gate off through coverage.

### 5.4 Cohorts, not disclaimers

Every score carries `(evidencePath: photos|floorplan, orientationPath: with|without)` — e.g. `"floorplan/without"`.
**Ranking, filtering, and thresholds apply within cohort**, using per-cohort calibration constants stored on the
engine version and derived offline by task zero. Calibration is **never computed at read time**; absent
constants are identity `(0, 1)`.

### 5.5 Numbers

Subject-level numerology remains a clamped ±3 adjustment. **Per-unit** numerology is computed at read time from
the availability table, is never persisted, and renders as an annotation — no letter, no 0–100, no score-colored
chrome. The plan chip is the only grade on the page.

---

## 6. Lenses

| Lens | Engine | Weight | Notes |
|---|---|---|---|
| **Interiors — photos path** | Claude vision, one call per photo, forced `record_room_observation` | .70 | Single listings only |
| **Interiors — floor-plan path** | Claude vision, **one call over the plan image**, forced `record_floorplan_observation` | .70 | Multi-plan properties only |
| **Site** | Deterministic rules over map data, per principle set | .30 | Orientation-dependent rules become non-applicable without a facing |
| **Numbers** | Pure rules | ±3 clamped (subject) / annotation (unit) | Never model-improvised |

### The floor-plan lens is deliberately narrow

**In scope (adjacency only):** bath adjacent to / over kitchen; bathroom door onto kitchen or dining;
entry-to-rear straight line; toilet sharing the bed-head wall; center-of-unit obstruction (only when the
boundary is fully drawn); kitchen at entry; bed-wall options.

**Out of scope, as schema-level prohibitions:** furniture and staging, mirrors, beams, clutter, natural-light
quality, five-element balance, anything dimensional, door swings, and **anything chirality-dependent
(left/right)** — plans are mirrored for opposite building stacks, so a left/right claim is wrong half the time.
Within-plan sqft varies ("385–431 Sq Ft"), so nothing dimensional may be scored either.

**The lens never infers north from a drawing.** Directional Vastu placement is a separate layer applied only
when orientation exists.

**A forced tool call must be able to decline:** `minItems: 0` on findings and suggestions, an explicit
`notDeterminable` marker, per-finding `confidence`, a closed `ruleId` enum, and a model-stated `coverage`.
Findings outside the enum are dropped and logged; `center_obstruction` is dropped when the boundary isn't drawn.

`ElementBalance` is **Feng Shui only** and nullable end-to-end — the report *omits* the section rather than
rendering five zero bars.

---

## 7. Orientation seam

```
IOrientationProvider.ResolveAsync(propertyKey, subjectId) → SubjectOrientation?
```

`SubjectOrientation(FacingDegrees, Cardinal, Source: sightmap|annotation|none, Confidence, ResolvedAt)`.

**Resolution rule (per plan):** bucket placed units into four cardinal sectors; if **≥80%** fall in one sector,
that is the plan's facing and the concentration ratio is the confidence. Otherwise `Source = "none"` → the
without-orientation path. Units with no facing are excluded from the denominator; zero placements → `null`.

| Path | Status |
|---|---|
| `SightMapOrientationProvider` + `ISightMapClient` | **Stubbed.** The API (`api.sightmap.com/v1`, API-key auth, unit↔floor↔building↔plan linkage) is real and confirmed; whether unit polygons are exposed as true-north geo-referenced vectors at the relevant tier is **unverified** and is a CoStar↔Engrain partner data request. Never a network call in tests. |
| Annotation fallback | If vectors aren't true-north: a one-time per-property rotation annotation from satellite imagery, `Source = "annotation"`. |
| `FixtureOrientationProvider` | The local path. Covers all three shapes — resolves, splits to `none`, and absent. |

Downstream is agnostic to which path filled the row. Orientation is **never** inferred from a floor-plan image or
a geocode; a >45° disagreement with a footprint bearing is a data-quality **log line only**, never a score input.

---

## 8. Backend structure

### 8.1 Persistence

| Table | Key facts |
|---|---|
| `subjects` | Materialized (referential integrity + backfill enumeration source). PK `"{propertyKey}"` or `"{propertyKey}:{rentalKey}"` |
| `input_sets` | Immutable snapshot; fingerprint source |
| `observations` | Unique `(SubjectId, EvidenceHash, PromptVersion, ModelId)` |
| `analyses` | Unique `(SubjectId, PrincipleSet, RulesVersion)`; status `pending\|ok\|failed\|insufficient_evidence` |
| `subject_orientations` | One per subject |
| `engine_versions` | `(rulesFengShui, rulesVastu, promptVersion, modelId)` → version; `CalibrationJson`; `PublishedAt` **null until the flip** |
| `scoring_jobs` | Queue + attempt/error/token/cost record |
| `projection_rows` | What the feed serves; mirrors apartments-web's child-table shape |

No `units` table. No photo bytes. Report bodies live in `IObjectStore` at
`reports/{engineVersion}/{subjectId}/{principleSet}.json.gz` (gzipped UTF-8 JSON, CDN-cacheable on drawer-open);
the analysis row carries `ReportUri` + `ReportSha256`.

Local stand-ins at the designed seams, without changing the seams: **SQLite** for the database,
**filesystem** for the object store, both behind the interfaces the design already required.

### 8.2 DI — the module seam

`Program.cs` is edited exactly once in the entire v2 plan. Every area registers itself via
`Infrastructure/<Area>Module.cs : IServiceModule`, discovered by
`builder.Services.AddHarmonIQModules(configuration)` (assembly scan, stable type-name order). This is what lets
a dozen tasks land in parallel without contending for one file, and it is why adding a service later never
touches host wiring.

`IHarmonIQCommand` + `CommandRunner` are the same trick for the CLI: `dotnet run … -- backfill …` dispatches by
name from DI before the web host starts; an unrecognized first arg falls through to normal server startup.

### 8.3 Services

| Area | Members |
|---|---|
| Ingestion | `PlanScraper` (rentalkey/modelname/attachmentid, plan image, availability rows behind "Show More Units"), `SubjectService` (materialize + immutable snapshot), `InputFingerprint`, `PerceptualHash`, `ListingService`, `SampleListingProvider` |
| Scoring core | `RuleEvaluation` (normalized), `ScoreMath` (aggregate/band/elements), `SiteAnalysisService.EvaluateSet` + `VastuGate` |
| Analysis | `AnalysisPipeline` (perception→judgment), `FloorPlanLensService`, `ClaudeAnalysisService`, `MockAnalysisService`, `ReportBodyWriter` |
| Orientation | `IOrientationProvider`, `OrientationResolution`, fixture + SightMap providers, `ISightMapClient` |
| Numbers | `NumerologyService` (per set; subject + read-time unit annotations) |
| Publishing | `EngineVersionService`, `PublicationService` |
| Batch/commands | `IBatchScoringClient` (+ stub), `InteractiveScoringDriver`, `BackfillCommand`, `TaskZeroCommand` |
| Search | `SynonymMap`, `SearchService` |

---

## 9. Pipeline and publishing

**Task zero — a decision gate, not a warm-up.** A 1,000-property sampling job measures (a) plan-image coverage,
(b) **within-property score variance across plans** — if the floor-plan lens creates no real variance, per-plan
grades are cosmetic and the fallback is a property grade plus per-plan layout notes, (c) per-cohort calibration
constants from a dual-scored subsample, (d) real cost per property. Locally it runs at N≈20 over fixtures and
says so in its own output rather than implying network-scale confidence.

**Backfill.** enumerate `subjects` → fingerprint check (match → `skipped`, zero model calls) → `scoring_jobs` →
drive. Interactive locally; the **Claude Batch API** path is stubbed behind `SCORING_MODE=batch` +
`BATCH_API_ENABLED`. No LLM summary in backfill — deterministic `LocalSummary`, with narrative generated lazily
on first report open. At stated assumptions (100k properties, 60% multi-plan × ~6 plans, 40% single × ~5 photos,
Sonnet batch pricing) the run is **≈$7.3k batch / ≈$14.6k interactive**.

**Backfill has a hard prerequisite, not an optimization:** internal listing and geo data. Public
Nominatim/Overpass and page scraping at backfill scale violate provider policies — they are dev/demo only.
`LISTING_SOURCE=api` is the seam. Without internal sources the backfill does not run.

**Incremental.** listing created/updated → fingerprint mismatch → interactive scoring in minutes.

**Publish by version flip.** Projection rows are written per engine version and published atomically when that
version's run completes; **rows are never mutated mid-rollout**, and rollback is republishing the previous
version, never deleting rows. Readers pass an explicit `engineVersion` so an SRP badge and an LDP chip fetched
seconds apart agree. Eligibility is `Mode == "live" && Status == "ok"`:

- **Demo output is never persisted into the publish path.** Demo is a read-path presentation.
- `insufficient_evidence` and `failed` produce **no** projection row. NULL is the correct representation.
- Retries 3× with backoff → `failed` → projection stays NULL. **A failure is never a grade.**

---

## 10. Surfaces

### 10.1 Frontend — three custom elements

```
<harmoniq-chip property-key subject-key brand>     one per plan card, in the stats line
<harmoniq-section property-key brand>              one per page — the single attribution line
                                                   and the SINGLE report drawer instance
<harmoniq-module listing-id brand>                 the single-listing card, slimmed
```

- **One bulk fetch per page.** `subjectsStore` performs exactly one `GET /api/property/{key}/subjects`
  regardless of how many chips mount. **Chips never fetch.**
- **No layout shift by construction.** Chips reserve their final footprint at `connectedCallback`. The cold
  state is an identical-footprint muted box — **no spinner**, and never twelve spinners. Because grades are
  precomputed, the bulk fetch resolves at first paint, so this is the normal path, not a transition.
- **A subject with no grades renders nothing at all**, from the first paint — not an empty box.
- **One attribution line per section** ("Scores provided by HarmonIQ" → `/harmoniq`), following the Local Logic
  convention — never one per chip.
- **One drawer, one open at a time**, `position: fixed` inside the shadow root, `aria-expanded` on the invoking
  chip, focus trap, Esc closes and returns focus. Full report richness lives behind the expansion; the surface
  stays a ~22px pill so twelve of them read as a quiet column.
- Per-set presentation is a **UI union** — Feng Shui and Vastu side by side. There is no combined number in the DOM.
- Null-safety is structural: `ElementBars` returns `null` on absent balance, `ScoreGauge` on a null score,
  and an `insufficient_evidence` set renders a short factual explanation of *what was missing* — no numerals.

### 10.2 Search

- **Synonyms:** `feng shui | fengshui | feng-shui → fengshui`; `vastu | vaastu | vasthu | vastu shastra → vastu`.
  Typeahead yields a suggestion chip → a pre-filtered SRP with the HarmonIQ filter open and that set selected.
  Near-misses ("vast open floor plan") must not match.
- **Filter:** parent "HarmonIQ" checkbox → per-set sub-selection. Parent-only default = either set at **B− or
  better within its cohort**, confidence floor applied. Filtering reads **stored projection rows** — a vision
  call cannot sit in an SRP request path.
- **Null grades are excluded from filtered results, and the exclusion is visible**: "N of M properties in this
  area have HarmonIQ scores." A filter is an affirmative request for a signal we have; hiding the omission is not
  acceptable. Never rank across cohorts. No renter identity is recorded.

### 10.3 API contract

| Endpoint | Purpose |
|---|---|
| `GET /api/property/{key}/subjects?engineVersion=&sets=` | **The bulk call.** All subjects, their per-set grades, and read-time unit annotations. Unscored subjects appear with an empty `sets`. |
| `GET /api/subject/{id}/report/{set}?engineVersion=` | Streams the gzipped report body from the object store; 404 when no analysis row |
| `GET /api/feed/grades?engineVersion=&cursor=&limit=` | The versioned feed apartments-web consumes; 409 on an unpublished version unless `includeUnpublished=true` |
| `POST /api/refine` | **Session-only** deterministic recompute from the stored input set with caller overrides. `persisted: false`, always. Writes nothing. |
| `GET /api/search/suggest?q=` · `GET /api/search?sets=&min=` | Typeahead chip and the cohort-aware filter |
| `GET /api/listing/{id}` · `…/photos/{pid}?w=` | Listing context and the cached, downscaled photo passthrough (no hotlinking) |
| `GET /api/health` | `{ ok, live, engineVersion, publishedVersion }` |

No endpoint ever emits a grade into server-rendered HTML, `<title>`, or `<meta>`. Null is `status` + a null
score, never `0`. C# records in `Models/` are mirrored 1:1 in `frontend/src/api.ts`.

### 10.4 Demo hosts

| Host | Role |
|---|---|
| `wwwroot/mock-ldp.html` | Floor-plans section with per-plan chips, availability tables with unit annotations, brand switcher, plus the intact single-listing region |
| `wwwroot/mock-srp.html` | Synonym typeahead, filter rail, the same chip component on results, the count caveat |
| `wwwroot/harmoniq.html` | The attribution target — describes per-tradition grades, cohorts, what a missing grade means, and that v2 grades are not comparable to v1's |
| apartments-web LDP, run locally | Proves the elements drop into the real page; **local branch only** |

---

## 11. Repo boundary

**HarmonIQ publishes; apartments-web consumes.** HarmonIQ exposes a versioned grades feed and **never writes
into apartments-web's database**. apartments-web owns its consumer, its additive-nullable migration
(`harmoniq_grade` child table + two nullable headline scalars on the search-index row), and its filter UI —
behind a feature flag defaulting **off**, authored on the local `harmoniq-demo` branch.

**The standing rule holds: no push, no merge, no PR** until the apartments-web owners lift it. That repo has an
unresolved Razor build anomaly on this machine; work there may land as *prepared-but-unverifiable artifacts*,
which is an acceptable, explicitly-reported outcome.

---

## 12. Cultural, legal, and safety guardrails

These are code-level constraints, not documentation:

- **Cultural framing everywhere** — verdicts attributed to a named tradition ("in Chinese numerology…",
  "in form-school terms…"), never as objective claims about safety, health, or value. Asserted by tests over
  emitted rule and finding text.
- **No negative superlatives** in any rule text, finding, summary, or UI copy. Asserted by tests.
- **The confidence floor is non-negotiable** — no low grade may ride thin evidence.
- **Grades are client-rendered inside the shadow root only** — never in `<title>`/`<meta>`, not indexable.
- **Session-only means session-only** — a renter-supplied Vastu orientation yields a displayed score that is
  never stored and never filterable, and the UI says so at the point of display.
- Renter-side tradition filtering is a preference and defensible; **publishing negative luck grades about named
  listings is the exposure.** Production publish requires a landlord-visible view and a dispute path (recorded
  now, built later), and the filter is to be reviewed as a potential steering/audience-segment issue before any
  rollout.
- The staging-advisor upsell must never compose into "pay us to fix the grade we published" — flagged, deferred.

---

## 13. Risk register

| # | Risk | Mitigation / gate |
|---|---|---|
| 1 | The floor-plan lens creates no real per-plan variance → per-plan grades are cosmetic | Task-zero variance measurement; fallback is a property grade + per-plan layout notes |
| 2 | SightMap does not expose usable true-north geometry | `IOrientationProvider` seam + satellite rotation-annotation fallback |
| 3 | Vision cannot actually read LDP plan images at served resolution | Task-zero dual-scoring |
| 4 | `data-rentalkey` is unstable across ingests | Perceptual content-signature fallback; ambiguous → no row |
| 5 | Internal listing/geo access does not materialize | Backfill is hard-gated on it; public endpoints stay dev/demo only |

---

## 14. Build state — as-built vs as-designed

Honest snapshot, as of the current working tree (v2 Tier 1).

| Area | State |
|---|---|
| Persistence, object store, `IServiceModule` seam (Task 1) | **Built and committed.** `Models/Entities.cs`, `HarmonIQDbContext` + migration, `FileSystemObjectStore`, `PersistenceTests` |
| Scoring core — normalized rules, coverage/confidence/cohorts, `VastuGate` (Task 2) | **In the working tree, uncommitted.** `RuleEvaluation.cs`, `ScoringModels.cs`, rewritten `ScoreMath`/`SiteAnalysisService`, `RuleEvaluationTests` + `CohortMatrixTests` |
| Prompts — tradition-agnostic room tool + floor-plan lens tool (Task 3) | **Built and committed.** `Prompts.cs`, `LensModels.cs`, `PromptSchemaTests` |
| Orientation seam (Task 4) | **Built and committed.** Provider interface, resolution rule, fixture provider, SightMap stub, `sample-orientation.json` |
| Multi-plan fixture (Task 5) | **Built and committed.** `sample-multiplan-listing.json`, five generated plan PNGs (one plan deliberately imageless) |
| Ingestion, analysis pipeline, numerology v2, publishing, host wiring (Tier 2) | **Not started.** `Program.cs` is still v1; no module scan, no migrate-on-start, no command runner |
| API surface, backfill, task zero (Tier 3) | **Not started.** The v1 `POST /api/analyze` contract is still what the server exposes |
| Frontend (Tier 4) | **Still v1.** One `<harmoniq-module>` element, `useHarmonIQ`'s single-score state machine, inline report panel. Chips, section element, store, and per-set state do not exist yet |
| Search (Task 17) | **Not started** |
| Mock LDP / mock SRP (Tier 5) | **Not built.** `wwwroot/` still contains only `embed/` |
| apartments-web (Tier 6) | v1 two-line embed applied on the local `harmoniq-demo` branch; v2 feed consumer and filter not started |
| Live Claude mode | Config-gated and unused locally. No key on this machine; the hackathon proxy sunsets **Aug 12, 2026**. **Demo mode is the development and verification path** — every task must be verifiable with no key set |

### Running it

```bash
npm run build --prefix frontend && dotnet run --project backend/HarmonIQ.Api
```

Once Tier 2–3 land, the demo sequence becomes:

```bash
dotnet run --project backend/HarmonIQ.Api -- backfill --property sample-multiplan --demo
dotnet run --project backend/HarmonIQ.Api -- task-zero --n 5 --dual-score
dotnet run --project backend/HarmonIQ.Api
```

Local state (SQLite DB, object store, task-zero report) lives under `.harmoniq-local/`, gitignored.
