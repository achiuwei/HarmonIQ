# HarmonIQ — Project Specification

**Version:** 2.0 · **Date:** August 11, 2026 · **Event:** Apartments.com Hackathon (Aug 10–11, 2026)

> **v1.1:** Photo upload removed; photos pull from the Apartments.com listing.
> **v1.2:** Added surrounding-environment analysis (roads, water, structures, slopes per side) and numerology checks on the listing's numbers.
> **v1.3:** Added LDP surfacing and multi-brand integration across the CoStar rentals network.
> **v1.4:** Standalone app and URL-pasting removed. HarmonIQ is **an LDP module only**: it appears on the listing page, gets its listing context from the host page, and runs automatically.
> **v1.5:** Badge placement finalized to match the real apartments-web LDP (`BuildingProfile`): the compact score renders as a native score card directly beneath the listing's existing scores — the Transportation section's "Getting Around" score-card grid, which follows the Schools/Education section — with a **"Data provided by HarmonIQ"** attribution link to the HarmonIQ page, following the LDP's Local Logic / GreatSchools attribution convention.
> **v1.6:** Second demo host added: the module is also embedded in the **real apartments-web LDP running locally** (a local-only demo branch — never merged or deployed). The module gains cross-origin support (API base derived from the embed script's origin, overridable via `api-base`) and the API enables CORS.
> **v2.0:** Per-floorplan subjects replace the single per-listing score; scores are now stored **per principle set** (Feng Shui / Vastu) with **no blended headline number**, gated by cohort (evidence path × orientation path) and a confidence floor. Orientation is sourced exclusively from Engrain SightMap, and Vastu requires it to produce a stored, filterable grade (a session-only Vastu score is still available via Refine when SightMap has no data for the unit). Numerology moves per-unit and is computed at read time as an annotation on the availability table, never a grade adjustment. The engine gains real persistence (observations/analyses split + object-storage report bodies) and a precompute pipeline (task-zero sampling gate, Claude Batch API backfill, incremental re-score, version-flip publish). The LDP surface shrinks to a per-plan grade chip plus a single per-section attribution line, with the single-listing card slimmed to match the Walk/Transit/Bike cards; a mock SRP demonstrates the "feng shui" / "vastu shastra" search filter. The apartments-web boundary becomes publish/consume: HarmonIQ publishes a versioned grades feed, apartments-web's own consumer work ships PR-ready and feature-flagged off — still no push, merge, or PR.

---

## 1. Overview

HarmonIQ grades apartments against **Feng Shui** and **Vastu Shastra** principles — as two separate, stored scores; there is no blended headline number — and surfaces the result **directly on the Listing Detail Page (LDP)**. Scoring attaches to a **subject**: a floor plan, on properties with more than one distinct floor plan, or the property itself, on single-floor-plan and single-unit listings. There is no separate app and nothing to paste: when a renter views a multi-plan listing, each floor plan carries a small grade chip in its stats line; a single-listing property keeps a compact score card sized to match the "Getting Around" Walk/Transit/Bike cards. Both surfaces read a **precomputed** grade rather than triggering a live analysis. Opening a chip or card reveals a full report built from data the listing already has — its floor plan image or its photos (never both for the same subject), its address and surroundings, its unit numbers, and, where Engrain SightMap resolves it, its orientation — across per-tradition scores backed by supporting lenses: **floor-plan layout** or **interiors** (whichever the subject's evidence path supplies), **site** (what surrounds the building on each side), and **numbers** (per-unit numerology, rendered as an annotation, never folded into a grade), plus a five-element balance profile (Feng Shui only) and concrete renter-friendly suggestions.

### 1.1 Problem

For millions of renters — particularly households that practice Vastu or Feng Shui — spatial harmony is a top-of-funnel filter when choosing a home, yet no listing platform surfaces it. Renters today tour units with a consultant, or guess from photos. Landlords and agents have no way to communicate (or improve) this quality of their inventory.

### 1.2 Vision within the CoStar rentals network

Because the analysis runs on data the platform already hosts, every listing carries HarmonIQ scores with no seller effort and no renter effort:
- An **LDP HarmonIQ module**: a grade chip per floor plan (multi-plan properties) or a slim score card (single listings), expandable full report (rooms or floor-plan layout, site, numbers) inline on the page the renter is already reading.
- A **search filter/badge** for culturally-minded renters — now demonstrated end to end via a mock SRP (§3.9) — differentiating the network in a way competitors don't offer.
- **One engine, every brand**: the same analysis service powers the whole network — Apartments.com, ApartmentFinder, ForRent.com, ApartmentHomeLiving, and sibling sites — since they share listing inventory; a subject analyzed once carries its score(s) to every brand's LDP.
- A **staging advisor upsell** for landlords: the suggestions engine tells them exactly what to move before the photographer arrives — flagged as a guardrail concern until it can be positioned without implying a grade is for sale (§3.11, FR-55).
- With **Matterport scans** (already common on listings), analysis could run automatically across every room with true compass orientation — a candidate second `IOrientationProvider` alongside SightMap (§10).

### 1.3 Scope

The v2 build substitutes local stand-ins at every seam the design specifies, without changing the seams: **SQLite** (via EF Core) for persistence, a **local filesystem directory** standing in for object storage, a **fixture `IOrientationProvider`** plus a SightMap client stub (no partner key exists on this machine) for orientation, and a **mock SRP** page served by the HarmonIQ backend for the search-filter demo. Two LDP hosts remain, as in v1: (1) the **mock LDP** — now carrying a multi-plan floor-plan/availability section with per-plan chips, backed by a multi-plan fixture, alongside the unchanged single-listing badge state; and (2) the **real apartments-web LDP running locally** — unchanged in mechanism (still local-only, never merged or deployed), but the *consumer-side* work it would need for v2 (filter UI, badges, DB migration) ships as **PR-ready, feature-flagged-off artifacts** on the local `harmoniq-demo` branch rather than as a live local integration, since a known Razor build anomaly in that dev environment limits what can be verified there; the mock host remains the primary, always-verifiable demo surface, mirroring v1. Live Claude vision + site + numerology analysis behind it where the interactive path runs; demo mode remains fully functional with no key (the hackathon proxy sunsets Aug 12). Single machine; persistence is now in scope by design (SQLite + a local object store); still no accounts, no auth, no deployment target.

---

## 2. Users & core flow

| User | Need |
|---|---|
| **Renter** (primary) | "Does this apartment have good energy? What would I need to fix?" — answered on the listing page they're already viewing, per floor plan where the property has several. |
| **Listing agent / landlord** | "How do I stage this unit to score better?" |
| **Judge / stakeholder** (demo) | See the concept work live in under 2 minutes on a realistic LDP, including a multi-plan property and a search filter. |

**Core flow:** Renter opens a listing page → for a multi-plan property, each plan card in the floor-plans section shows a small grade chip per principle set that cleared the confidence floor (or nothing, if that plan is unscored); a click opens the single shared report drawer for that plan. For a single-listing property, the compact score card beneath "Getting Around" behaves as in v1. Grades are precomputed and read from storage via one bulk call — the first paint already carries them, with a muted, identical-footprint box standing in for anything still resolving, never a spinner. Opening a report shows per-tradition scores/grades (Feng Shui and/or Vastu, never blended), the floor-plan-layout or per-room findings depending on evidence path, a Site & Surroundings card, and numerology rendered as an annotation on the unit rather than a score adjustment. A **Refine** drawer lets the renter reselect photos/tags (single-listing subjects), edit surroundings and numbers, choose which principle set(s) to view, and — only where SightMap didn't already resolve an orientation — supply one to compute a **session-only** Vastu score that is shown but never stored. Separately, a renter can type "feng shui" or "vastu shastra" (or variants) into search to reach a filtered results page carrying HarmonIQ badges.

No URL entry, no upload, no separate destination: the module's only required input is the `listing-id` (and, on multi-plan properties, the plan/subject id) provided by the host LDP.

---

## 3. Functional requirements

### 3.1 LDP module & listing context
- **FR-1** HarmonIQ ships **only** as an embeddable module (self-contained web component / embed bundle: `<harmoniq-module listing-id brand [api-base]>`) rendered inside a host LDP. There is no standalone app and no user-entered listing URL. The module works cross-origin: it derives its API base from the origin the embed script was loaded from (overridable via the `api-base` attribute), and the HarmonIQ API serves `/api/*` with CORS enabled — so a host page on any local origin (e.g., the real apartments-web LDP) can embed it with a single script tag.
- **FR-2** The module reads its **listing identity from the host page** (`listing-id` attribute; production hosts would inject it server-side). Listing IDs are the network's **shared identity**, so any brand's LDP (apartments.com, apartmentfinder.com, forrent.com, apartmenthomeliving.com, …) resolves to the same listing and, on multi-plan properties, the same set of floor-plan subjects and their grades.
- **FR-3** The module surfaces in two forms depending on subject count. **Multi-plan properties:** each floor plan card in the floor-plans/availability section carries a **grade chip** — a ~22px inline-flex pill in the plan's stats line, grade only, no card chrome, no gauge, no heading — for each scored principle set (Feng Shui, Vastu, or both, whichever cleared the confidence floor); an unscored plan renders nothing, not a placeholder. The property-level score card is suppressed entirely on these properties. **Single-floor-plan / single-unit properties:** keep the v1 badge — compact score card + expanded panel — but slimmed to the visual weight of the host's Walk/Transit/Bike cards, beneath the Transportation section's "Getting Around" score-card grid, which itself follows Schools/Education. Whichever form, attribution is a single **"Scores provided by HarmonIQ"** line at the foot of the floor-plans section (multi-plan) or the existing per-badge attribution line (single-listing) — never repeated once per chip. Neither form triggers a live analysis on render: both read a precomputed grade (§3.8) that resolves at first paint via one bulk fetch (FR-42).
- **FR-4** **Brand-agnostic theming:** all colors/typography derive from a design-token set overridable per brand; ship presets for Apartments.com, ApartmentFinder, and ForRent so the module visually belongs on each brand's LDP. The `brand` attribute (or host CSS tokens) selects the theme.
- **FR-5** The analysis API is **brand-aware** (`brand` parameter for attribution/theming) while results remain keyed by the shared listing ID and, on multi-plan properties, the plan's subject id nested under it — analyze once, render on every brand.
- **FR-6** Hackathon demo hosts, two of them:
  - **(a) Mock LDP** — a static replica of the apartments-web `BuildingProfile` layout (including a Schools/Education section with GreatSchools ratings and a Transportation section ending in the "Getting Around" score-card grid, so the single-listing badge renders in its real slot) embedding the module in both states with a brand switcher, **now also including a multi-plan floor-plan/availability section** (plan cards with per-plan chips, an availability table with inline numerology annotations) driven by a multi-plan sample fixture — both evidence paths demoable from one host.
  - **(b) Real LDP, local only** — unchanged in mechanism: the module injected into the actual apartments-web `BuildingProfile` page (`Modules/BuildingProfile/Views/Index.cshtml`, immediately after the `_TransportationSection` partial), with `listing-id` bound to the page's `PropertyKey` and the embed script loaded from the HarmonIQ origin (`http://localhost:5080`). This lives on a **local-only demo branch of apartments-web that is never merged, pushed, or deployed.** For v2, the *filter/badge/migration* work apartments-web's own SRP and LDP consumer would need ships as **PR-ready, feature-flagged-off artifacts** on that branch (§7) rather than as a live local integration. A bundled **sample listing** fixture (metadata + illustrated room photos with deliberate violations) and a bundled **multi-plan sample fixture** back the mock LDP so the demo works offline.

### 3.2 Automatic listing data ingestion
- **FR-7** Given a listing ID, the backend fetches the listing and extracts: title/address, photo URLs, photo captions/labels, floor-plan identity (`data-rentalkey`/`data-modelname`/image) and availability tables where present, and the unit/floor/street numbers.
- **FR-8** Photos are classified as **interior** or **non-interior** (exterior, floor plan, amenity, pool, map). On single-listing subjects, interior photos are **auto-selected up to a cap of 6** (by listing photo order) for analysis. On multi-plan properties, property-level photos are classified but **never fed into per-plan scoring** (FR-35) — only the plan's floor-plan image is.
- **FR-9** Room-type tags are **pre-filled from listing captions** when available, otherwise "Auto-detect" (the model identifies the room from the image). Applies to the single-listing/photos path only.
- **FR-10** The backend downscales fetched photos server-side to ≤1568 px on the long edge before sending to the model.
- **FR-11** Defaults are refinable, not required: the expanded module includes a **Refine drawer** where the renter can deselect/reselect photos and correct room tags (single-listing subjects), edit surroundings and numbers, and choose which principle set(s) to view (`both` default | `fengshui` | `vastu` — each renders its own stored score, never a blended one). **Orientation control:** when SightMap resolved an orientation for the subject, it is shown read-only; when it didn't, the renter may supply one to compute a **session-only** Vastu score (FR-40) that is displayed but never stored, never affects the stored/filterable grade, and does not survive a reload. Re-grading applies in place on the single-listing/interactive path, as in v1; a floor-plan subject's stored grade is precomputed and is not re-triggered by Refine.
- **FR-12** Listing fetch failures (unknown ID, no photos, blocked request) render the badge (single-listing) or the chip column (multi-plan) in an unobtrusive error state ("HarmonIQ Score unavailable"); the module never breaks the host page.

### 3.3 Surrounding environment (site analysis)
- **FR-13** For each of the four sides of the building (N/E/S/W, resolved from the subject's orientation when one exists), the app captures what lies immediately outside: **road** (and type: quiet street / busy road / T-junction pointing at the building / highway), **water** (river, lake, pond, pool), **other structures** (taller building, similar buildings, open land), and **slope** (ground rises / falls / level). Orientation itself comes **only from Engrain SightMap** (`IOrientationProvider`, §3.8) — geocoding never yields orientation, and floor-plan images carry no compass information. Where SightMap resolves no facing for the subject, site analysis runs the **orientation-independent rule subset only**, renormalized so a missing input never reads as a bad property, and the report/API mark the result `orientationPath: without` with a short factual disclaimer ("Orientation wasn't available for this property; direction-dependent checks are excluded.").
- **FR-14** Environment data (road/water/structures/slope) is **derived automatically from the listing address** — geocode, then query public map data (e.g., OpenStreetMap) for nearby roads, water bodies, and buildings, and an elevation service for slope direction. Values are editable in the Refine drawer; anything not derivable defaults to "unknown / not sure". This is independent of orientation resolution (FR-13), which is SightMap-only.
- **FR-15** Site findings are graded against form-school Feng Shui and Vastu site rules, including at minimum: *Feng Shui* — armchair position (higher support behind, open "bright hall" in front), T-junction or straight road aimed at the entrance (sha chi), water placement relative to the facing direction, being overshadowed by a much taller adjacent structure. *Vastu* — water bodies auspicious in N/NE, ground sloping down toward N/E auspicious and toward S/W inauspicious, heavier/taller masses auspicious in S/W, road-facing direction effects. Where the subject has no resolved orientation, Feng Shui site rules run on the orientation-independent subset (sha-chi, overshadowing) — enough to keep a Feng Shui grade viable; Vastu's site rules are predominantly directional and are excluded wholesale without a facing, same as Vastu's interior/floor-plan rules (§3.8, FR-39).
- **FR-16** Site analysis produces the same finding shape as rooms (adhering / violations with severity / suggestions) and feeds `score(set)` as the 30%-weighted lens (FR-25). Site suggestions must be renter-realistic: mitigation (curtains, plants, mirrors, entrance screening) rather than "move the river". Unknown environment values simply produce no findings — never guessed ones.

### 3.4 Numerology
- **FR-17** Numerology is computed **per unit, at read time, never persisted** (design §5, Q1) — plan-level and property-level subjects carry no numerology of their own. For each unit in a scored subject's availability table, the unit number, floor number, and street address number are extracted (correctable in the Refine drawer for the demo's single sample unit) and checked live when the row renders.
- **FR-18** Each unit's numbers (FR-17) are checked against numerology traditions consistent with the selected tradition filter: *Chinese/Feng Shui* — tetraphobia (4 and 4-containing numbers inauspicious, 8 wealth, 9 longevity, combinations like 14/24); *Vastu/Indian numerology* — digit-sum reading of the unit and street numbers; *Western* — 13, plus 666 flagged as culturally sensitive. The rules are **deterministic** (a rules engine, not the LLM).
- **FR-19** Numerology results render as an **annotation, never a grade**: on single-listing subjects (evidence path = photos), a dedicated "Numbers" card as in v1 — each number with a lucky / neutral / unlucky verdict, tradition and reasoning, and renter-feasible remedies. On multi-plan subjects (evidence path = floor plan), the equivalent verdict renders **inline in the unit availability table**, next to each unit's number/floor, computed on read per FR-17 — never as a fifth score contending with the plan's stored grade(s).
- **FR-20** *(v2: superseded)* Numerology **no longer adjusts any stored score**. It is cultural annotation only (FR-19); v1's ±3 score-adjustment mechanism is removed, and `numerology.scoreAdjustment` is dropped from the API contract (§4).

### 3.5 Interior / floor-plan analysis
- **FR-21** Analyze each selected photo with Claude vision against the selected tradition(s). Findings must reference **only what is visible** in the image. (Single-listing/photos evidence path.)
- **FR-22** Principles checked (non-exhaustive): *Feng Shui* — commanding position (bed/desk/stove), chi flow and clutter, five-element balance, mirror placement, bed under window/beam, pairs and symmetry, natural light, poison arrows. *Vastu* — directional alignment of rooms, Brahmasthan (open center), heavy furniture in S/W, water in N/NE, sleep orientation, direction-appropriate colors. Vastu principles that depend on a facing (directional alignment, S/W mass placement, N/NE water, sleep orientation, direction-appropriate colors) apply only when the subject has a resolved orientation (§3.8, FR-39); without one, Vastu interior findings are limited to non-directional checks (Brahmasthan, general clutter/flow) — often too thin alone to clear the confidence floor, in which case Vastu is `insufficient_evidence` for that subject (FR-25).
- **FR-23** Per room, return: room type, score 0–100, five-element balance (wood/fire/earth/metal/water, each 0–100), 2–4 **adhering** findings, 0–4 **violations** (each with severity `minor|moderate|major`), 2–4 **suggestions** (each with `effort` and `impact` rated `low|medium|high`). Every finding is tagged with its tradition (`fengshui|vastu|both`).
- **FR-24** Suggestions must be **renter-feasible**: furniture rearrangement, decor, plants, mirrors, textiles, lighting — never structural renovation.
- **FR-25** Aggregate **per principle set, never blended**: for each of Feng Shui and Vastu independently, `score(set) = Σ(wᵢ·cᵢ·sᵢ) / Σ(wᵢ·cᵢ)`, where `wᵢ` is the lens weight (interiors or floor-plan-layout .70, site .30) and `cᵢ` is that lens's rule-coverage confidence ∈ [0,1]; `confidence(set) = Σ(wᵢ·cᵢ)`. Missing evidence lowers a lens's weight, never its score. `confidence(set) < 0.5` → `status = insufficient_evidence`, no grade for that set — never a flattering all-unknown default, and never a fabricated F. Each set that clears the floor gets its own letter grade (A+ ≥95 … F <40) and its own element-balance profile (Feng Shui only — Vastu's is omitted, not zeroed, per FR-27). A `both` view in the UI is a **union of the two stored per-set rows**, never a third stored score. Numerology never adjusts a score (FR-20). Every grade carries a `cohort` — `(evidencePath: photos|floorplan, orientationPath: with|without)` — and ranking/filtering happen **within cohort** using stored per-cohort calibration constants (task zero, §3.10), never a live-computed adjustment. The natural-language summary is scoped to one open set at a time.

### 3.6 Report (expanded module)
- **FR-26** The expanded panel header shows the animated circular gauge with letter grade and score, once per opened principle set (Feng Shui and/or Vastu render as separate gauges when both apply — never averaged into one). Gauge color follows score (green ≥75, amber ≥55, red below). The compact single-listing badge is a host-style score card showing grade + score; the per-plan chip (FR-3) carries only the grade letter/score, with the "Scores provided by HarmonIQ" attribution living at the section foot, not on the chip.
- **FR-27** Five-element balance rendered as labeled, color-coded bars — **Feng Shui only**; the report omits the section entirely when the open set is Vastu, or when the element-balance lens's coverage didn't clear the confidence floor (never five zero bars).
- **FR-28** For single-listing/photos subjects: one card per analyzed photo — thumbnail, room score chip, two-column findings (green "Working in your favor" / red "Breaking the principles" with severity badges), and suggestion cards with impact/effort tags. For multi-plan/floor-plan subjects: one **layout card** per floor-plan finding (adjacency, entrance relationship, bed-wall options — §3.8's floor-plan lens) in the same two-column adhering/violation shape, keyed to the plan image rather than a photo thumbnail; no per-room score chip, since the lens reads the whole plan, not a room.
- **FR-29** A **Site & Surroundings card** in the same two-column finding format, headed by a compass diagram summarizing what's on each side when the subject has a resolved orientation; without one, the card states that only orientation-independent sides/rules were evaluated (FR-13).
- **FR-30** A **Numbers card** (single-listing subjects) or in-table numerology annotations (multi-plan subjects), per FR-19.
- **FR-31** A mode pill inside the expanded panel, scoped to the currently open principle set: `Live · <model>` or `Demo mode`; demo mode also shows an explanatory banner.

### 3.7 Resilience / demo safety
- **FR-32** If the Claude endpoint is unreachable, or the key is missing or rejected (401/403), the API returns a realistic **built-in demo analysis** (template-based, respecting the tradition filter and room-type hints, and — for floor-plan subjects — a template floor-plan-layout finding set) flagged `mode: "demo"` — the demo must never dead-end.
- **FR-33** Transient upstream failures (429, 5xx) — from the Claude proxy, the listing fetch, or map/elevation services — are retried with linear backoff (up to 3 retries) before failing. Map/elevation failures degrade to an empty (editable) surroundings section, never a blocked module.
- **FR-34** Invalid input (unknown listing ID, zero photos selected in Refine, >6 photos) returns HTTP 400 with a human-readable error; the module surfaces it inside its own bounds without affecting the host page. A request for a subject with no stored analysis (unscored per FR-37, or simply not yet backfilled) is **not an error**: the API returns a `pending` / `insufficient_evidence` status rather than 400/404, so the frontend renders "no chip" instead of an error state.

### 3.8 Subjects, scoring model & persistence (new in v2)
- **FR-35** A **subject** is a floor plan, on any property with more than one distinct floor plan (the discriminator is the property's **count of distinct floor plans**, not its unit count — a plan with a single available unit on a property that has other plans is still floor-plan-scored), or the property itself, on single-floor-plan / single-unit properties. Evidence path follows subject type: floor-plan subjects score from the **floor-plan image only**, never from property-level marketing photos (which cannot be attributed to one plan); single-listing subjects score from **photos only**, as in v1. A property is exactly one of the two paths; never both.
- **FR-36** A new forced-tool Claude vision call (`record_floorplan_analysis`, alongside `record_room_analysis` in `Services/Prompts.cs`) reads a floor-plan image for **rotation-independent layout**: room adjacency, entrance placement and what it opens onto, kitchen/bathroom position relative to bedrooms and to each other, bed-wall options, corridor/window placement, and center-of-unit obstruction (Brahmasthan, when the boundary is fully drawn). Findings/suggestions schemas use `minItems: 0` with an explicit `not_determinable` marker and per-finding confidence — the call may decline entirely. Out of scope for this lens: anything furniture-based, mirrors, beams, clutter, light quality, five-element balance, anything dimensional, door swings, and any chirality-dependent (left/right) claim, since plans are mirrored for opposite building stacks — findings are adjacency-only. Directional Vastu placement is an additional layer applied only when the subject has a resolved orientation (FR-38/FR-39); the lens never infers a facing from the drawing. This lens supplies the 70%-weighted lens in `score(set)` (FR-25) for floor-plan subjects; site keeps its 30%.
- **FR-37** A plan with no usable floor-plan image, and thus no interior evidence at all (property-level photos are barred by FR-35), is **unscored**: no chip, no placeholder row, no `analyses` row (or a `pending` one). It never counts for or against its property in search (§3.9).
- **FR-38** Orientation is sourced **exclusively from Engrain SightMap** (`IOrientationProvider`, a seam over `unit_placements`), materializing `subject_orientation(facing_degrees, cardinal, source: sightmap|annotation|none, confidence, resolved_at)` per subject. Per-unit facings roll up to a plan via: ≥80% of placed units in one cardinal sector → that sector is the plan's facing (confidence = concentration); otherwise the plan has no orientation. Geocoding never yields orientation; a footprint-bearing disagreement >45° between SightMap and any other geometry source is logged as a data-quality signal only, never surfaced as a user-facing conflict. Where vectors aren't true-north-referenced, a one-time per-property rotation annotation from satellite imagery fills the gap (`source: annotation`) — downstream code is agnostic to which path resolved it.
- **FR-39** A subject's Vastu score is **stored and filterable only where orientation resolved** (FR-38). Without one, Vastu's directional rules (interior and site alike) are excluded; the leftover non-directional Vastu checks alone are rarely enough to clear the confidence floor (FR-25), so Vastu is typically **absent** — not degraded — on unoriented subjects. Feng Shui degrades gracefully instead: its orientation-independent rules (sha-chi, interior clutter/flow, five-element balance) keep it viable without a facing.
- **FR-40** The Refine drawer may still accept a renter-supplied entrance orientation and compute a **session-only** Vastu score from it — displayed immediately, **never written to storage, never used for search filtering or ranking, and never surfacing on any other renter's view of the same subject**. The drawer visibly labels this score as a personal, unsaved estimate.
- **FR-41** HarmonIQ persists across three layers: `observations` (raw model output, keyed by subject/evidence-hash/prompt-version/model-id — expensive, invalidated only by evidence/prompt/model change), `analyses` (deterministic derivation from observations + site + numbers, keyed by subject/principle-set/rules-version, unique-constrained — cheap, re-derivable in bulk on an engine bump), and report bodies in **object storage** (`reports/{engine_version}/{subject}/{set}.json.gz`, referenced from `analyses` by `report_uri` + `report_sha256`, CDN-cacheable). `rules_version` is scoped **per principle set** so a Vastu rules change never invalidates Feng Shui. Ingest writes an immutable `input_set` snapshot (evidence hashes, environment snapshot, orientation, numbers) that scoring reads exclusively, eliminating an ingest/scoring race. Plan identity keys on scraped `data-rentalkey`, with a perceptual-image-hash + beds/baths fallback; an ambiguous match writes **no row** (a wrong grade is worse than a null). Units carry no subject role and no persisted rows beyond what backs the availability table (FR-17). **Demo output is never persisted:** projection writes require `mode='live' AND status='ok'`; the demo host is a read-path presentation over fixture data, never a source of durable grades.
- **FR-42** `GET /api/property/{propertyKey}/subjects` returns every scored (and pending/unscored, for the frontend's benefit) plan's stored grades in **one call**, so a page with a dozen floor plans resolves all of its chips from a single fetch at first paint rather than one request per plan.

### 3.9 Search (new in v2)
- **FR-43** Search recognizes `feng shui|fengshui|feng-shui → fengshui` and `vastu|vaastu|vasthu|vastu shastra → vastu` (case-insensitive). Typeahead on a recognized term produces a suggestion chip that opens a pre-filtered SRP with the HarmonIQ filter open and that principle set pre-selected.
- **FR-44** The search filter panel gets a parent **HarmonIQ** checkbox; checking it reveals per-set sub-selection (Feng Shui / Vastu). Parent-only (no sub-selection) defaults to: any selected-or-either set at **B− or better within its cohort** (FR-25's cohort, confidence floor applied). Results are filtered against **stored** grades only — never a live vision call in the SRP request path.
- **FR-45** A subject/property with no qualifying stored grade is **excluded** from filtered results (a filter is an affirmative request for a signal HarmonIQ doesn't have for it) — never silently included as if it passed, and never rendered with a placeholder grade. The SRP shows a visible count caveat ("N properties have HarmonIQ scores in this area") so unscored inventory is visibly absent rather than silently dropped.
- **FR-46** SRP result cards reuse the LDP chip component (FR-3) to badge properties/plans that pass the active filter.
- **FR-47** A **mock search-results page**, served by the HarmonIQ backend alongside the mock LDP, demonstrates FR-43–FR-46 offline over the fixture data: synonym typeahead, the HarmonIQ filter checkbox and sub-selection, badge chips, and the null-count caveat.

### 3.10 Precompute pipeline (new in v2)
- **FR-48** Before backfill runs at scale, a **1,000-property sampling job (task zero)** measures: plan-image coverage (validates FR-37's frequency assumption), within-property score variance across plans (if the floor-plan lens produces no real per-plan variance, the fallback is a property-level grade + per-plan layout notes instead of per-plan grades), per-cohort calibration constants from a dual-scored subsample (feeds FR-25's cohort ranking), and real cost per property. Backfill at scale does not proceed until this gate reports.
- **FR-49** Backfill: enumeration (a materialized `subjects` table) → fingerprint check against the stored `input_fingerprint` → `scoring_jobs` → **Claude Batch API**, with no LLM summary written at backfill time (a deterministic `LocalSummary`; narrative text generates lazily on first report open). Backfill is **gated on internal listing/geo data access** (`LISTING_SOURCE=api`) — public Nominatim/Overpass/scraping at backfill scale violates provider policy and is dev/demo-only (NFR-4 amended). At stated assumptions (100k properties; 60% multi-plan × ~6 plans × 1 floor-plan call; 40% single × ~5 photo calls; Sonnet batch pricing): **≈$7.3k batch / ≈$14.6k interactive**, refined by task zero.
- **FR-50** A listing created or updated triggers a fingerprint check; a mismatch enqueues an **interactive-path** scoring job that completes in minutes, independent of the batch backfill cadence.
- **FR-51** Projection rows (the apartments-web-facing nullable grade columns, FR-6b/§7) are written per engine version and published **atomically** once that version's run completes; the SRP carries the engine version into the LDP request so a badge and its underlying card always agree, even mid-rollout. Projection rows are never mutated in place. A job that exhausts 3 retries with backoff marks `failed`; the projection stays NULL — a failure is never rendered as a grade.

### 3.11 Cultural & legal guardrails (new in v2)
- **FR-52** Because a stored Vastu/Feng Shui score can describe a specific, named listing rather than a renter's personal preference, the confidence floor (FR-25) is also a publication safeguard: no low grade is ever published on evidence too thin to trust, and no finding uses a negative superlative ("cursed", "terrible energy") anywhere in generated text — NFR-8's cultural framing applies to every surface, including the search filter and SRP badge, not only the report body.
- **FR-53** Grades and findings render **client-side inside the module's shadow DOM only** — never emitted into page `<title>`/`<meta>` or any server-rendered markup a search engine could index.
- **FR-54** Production publish requires a **landlord-visible view** of their listing's grade and a dispute path before launch; this is out of scope for the local demo (§8) but recorded here so it isn't silently dropped.
- **FR-55** The staging-advisor upsell (§1.2) must never be positioned as "pay us to change the grade we published" — flagged as an open decision, deferred, not resolved by this spec.

---

## 4. API contract

Base URL: the HarmonIQ origin (default `http://localhost:5080`). The module calls these endpoints itself; the host page supplies only `listing-id` (and, on multi-plan properties, the plan/subject id from the bulk fetch below). Same-origin hosts (the mock LDP) use relative paths; cross-origin hosts (the local real LDP) work because the module prefixes its API base (derived from the embed script's origin, or `api-base`) and the API sends permissive CORS headers. Relative URLs the API returns (photo thumbnails) are resolved against the same base.

### 4.1 `GET /api/listing/{listingId}` — listing context for the module

(`listingId` is the network's shared listing identity; `sample` returns the single-listing fixture, `sample-multiplan` returns the multi-plan fixture. Optional `?brand=` for attribution.)

Response `200` (single-listing subject shown; multi-plan properties additionally carry a `floorPlans` array of `{ planId, planName, beds, baths, sqftRange, imageUrl, imageHash, units: [{unitNumber, floor, sqft, available}] }`, and omit `photos`/`environment` prefill for scoring purposes — those fields still exist for the property's marketing gallery but are not sent to the floor-plan lens per FR-35):
```json
{
  "listingId": "xyz123",
  "title": "The Elm — 2BR/2BA",
  "address": "123 Main St, Arlington, VA",
  "url": "https://www.apartments.com/…/xyz123/",
  "photos": [
    {
      "photoId": "p1",
      "thumbnailUrl": "/api/listing/xyz123/photos/p1?w=300",
      "caption": "Master Bedroom",
      "interior": true,
      "selected": true,
      "suggestedRoomType": "Bedroom"
    }
  ],
  "numbers": { "unitNumber": "414", "floor": 4, "streetNumber": "123" },
  "environment": {
    "north": { "road": "busy", "water": "none", "structures": "taller-building", "slope": "level" },
    "east":  { "road": "none", "water": "pond", "structures": "open", "slope": "falls" },
    "south": { "road": "quiet", "water": "none", "structures": "similar", "slope": "level" },
    "west":  { "road": "unknown", "water": "unknown", "structures": "unknown", "slope": "unknown" }
  }
}
```
`numbers` and `environment` are best-effort prefills (address parsing + map/elevation lookups); any field may be `"unknown"`. `selected` marks the auto-chosen analysis set (interior, capped at 6) on the single-listing path. All of it is editable via the Refine drawer.

Errors: `404` when the listing or its photos can't be found; `502` when the listing source can't be reached.

### 4.2 `GET /api/property/{propertyKey}/subjects` — bulk per-plan grades (new in v2, FR-42)

One call per page load on multi-plan properties, resolved before first paint.

Response `200`:
```json
{
  "propertyKey": "prop789",
  "subjects": [
    {
      "planId": "plan-19",
      "planName": "Plan 19",
      "scores": [
        { "set": "fengshui", "status": "ok", "score": 78, "grade": "B+", "confidence": 0.74, "cohort": { "evidencePath": "floorplan", "orientationPath": "without" } },
        { "set": "vastu", "status": "insufficient_evidence", "score": null, "grade": null, "confidence": 0.31, "cohort": { "evidencePath": "floorplan", "orientationPath": "without" } }
      ]
    },
    {
      "planId": "plan-08",
      "planName": "Plan 8",
      "scores": []
    }
  ]
}
```
`scores: []` (no entries at all, per FR-37) means the plan is unscored — the frontend renders no chip. `status: "pending"` marks a subject queued for scoring but not yet complete; `status: "insufficient_evidence"` marks one that was evaluated and fell below the confidence floor (FR-25) — both render no chip, and are distinguishable only in logs/admin tooling, never in the renter-facing UI.

### 4.3 `POST /api/analyze`

Called automatically by the module with the defaults from `/api/listing` on the single-listing/interactive path, and again with edited values when the renter refines. Floor-plan subjects do **not** call this endpoint on render — their grade comes from FR-42's bulk fetch — but the same endpoint is used by the backfill/incremental pipeline (§3.10) and by the Refine drawer's session-only Vastu recompute (FR-40).

Request:
```json
{
  "listingId": "xyz123",
  "photos": [ { "photoId": "p1", "roomType": "Bedroom" } ],
  "systems": "both | fengshui | vastu",
  "orientation": "unknown | north | northeast | east | southeast | south | southwest | west | northwest",
  "environment": { "north": { "road": "…", "water": "…", "structures": "…", "slope": "…" }, "east": { }, "south": { }, "west": { } },
  "numbers": { "unitNumber": "414", "floor": 4, "streetNumber": "123" },
  "brand": "apartments"
}
```
(`brand` is optional attribution; results are keyed by the shared `listingId`, so an analysis performed via one brand serves all of them. `orientation` supplied here on a subject whose orientation is `source: sightmap` is honored only as a **session-only** override per FR-40 — it never overwrites the stored `subject_orientation`.)

Response `200` — **per-set scores, no blended headline number** (v2; supersedes v1's single `overallScore`/`grade`):
```json
{
  "mode": "live | demo",
  "modelId": "claude-sonnet-5 (live only)",
  "notice": "explanation (demo only)",
  "listing": { "listingId": "xyz123", "title": "…", "address": "…", "url": "…" },
  "analysis": {
    "subject": { "type": "property | floorplan", "evidencePath": "photos | floorplan" },
    "orientation": { "path": "with | without", "cardinal": "north | … | null", "source": "sightmap | annotation | none | session" },
    "scores": [
      {
        "set": "fengshui",
        "status": "ok",
        "score": 67,
        "grade": "C+",
        "confidence": 0.81,
        "cohort": { "evidencePath": "photos", "orientationPath": "without" },
        "summary": "2-3 sentence assessment for this set…",
        "elementBalance": { "wood": 38, "fire": 12, "earth": 42, "metal": 8, "water": 5 }
      },
      {
        "set": "vastu",
        "status": "insufficient_evidence",
        "score": null,
        "grade": null,
        "confidence": 0.31,
        "cohort": { "evidencePath": "photos", "orientationPath": "without" },
        "summary": null,
        "elementBalance": null
      }
    ],
    "rooms": [
      {
        "photoId": "p1",
        "roomType": "Bedroom",
        "score": 62,
        "adhering":   [ { "principle": "…", "observation": "…", "system": "fengshui|vastu|both" } ],
        "violations": [ { "principle": "…", "observation": "…", "severity": "minor|moderate|major", "system": "…" } ],
        "suggestions": [ { "title": "…", "detail": "…", "effort": "low|medium|high", "impact": "low|medium|high" } ]
      }
    ],
    "floorplan": {
      "findings": [
        { "principle": "Bath Adjacent to Kitchen", "observation": "…", "confidence": 0.9, "system": "fengshui|vastu|both" }
      ]
    },
    "site": {
      "adhering":   [ { "principle": "Armchair Position", "observation": "…", "system": "fengshui" } ],
      "violations": [ { "principle": "T-Junction Facing the Entrance", "observation": "…", "severity": "major", "system": "fengshui" } ],
      "suggestions": [ { "title": "Screen the entrance line", "detail": "…", "effort": "low", "impact": "high" } ]
    },
    "numerology": {
      "annotations": [
        {
          "subject": "unitNumber",
          "value": "414",
          "verdict": "unlucky",
          "tradition": "fengshui",
          "reason": "Contains 4 twice; in Chinese numerology 4 (sì) is a homophone of death (sǐ).",
          "remedy": "Add a small interior plaque so the number read at the door sums to an auspicious digit, or a red accent at the threshold."
        },
        { "subject": "floor", "value": "4", "verdict": "unlucky", "tradition": "fengshui", "reason": "…", "remedy": "…" },
        { "subject": "streetNumber", "value": "123", "verdict": "lucky", "tradition": "vastu", "reason": "Digit sum 6 — harmony and domestic wellbeing.", "remedy": null }
      ]
    }
  }
}
```
`rooms` is present only when `subject.evidencePath = "photos"`; `floorplan` only when it is `"floorplan"` — never both. `numerology.annotations` is informational only and never contributes to any entry in `scores` (FR-20). `rooms[i]` corresponds to `photos[i]` in request order when present.

Errors: `400 { "error": "…" }` for invalid input (unknown `photoId`, zero/too many photos); `502 { "error": "…" }` for non-fallback upstream failures.

### 4.4 `GET /api/health`

`200 { "ok": true, "live": <bool: Claude key configured> }`

---

## 5. Architecture

```
┌─ Host LDP (mock + real, local) ────────┐      ┌─ ASP.NET Core API (:5080) ─────────────────────┐
│ mock-ldp.html (brand switcher)         │      │ ListingController → ListingService ────────────┼──▶ Listing source
│  <harmoniq-module listing-id brand> ───┼──────┼→   (resolve shared ID, fetch, extract +        │    (network listing page or
│   single-listing badge OR per-plan     │      │     classify photos, cache, thumbnails)        │     internal listing API)
│   chip column → bulk GET /subjects     │      │   → GeoContextService (environment prefill) ───┼──▶ Geocoder / OSM Overpass /
│   → chip click → shared report drawer  │      │   → OrientationProvider (SightMap seam) ────────┼──▶ SightMap API (fixture/stub
│  mock-srp.html (synonyms, filter,      │      │→ AnalysisController                            │    on this machine)
│   badges) — search demo                │      │  → ClaudeAnalysisService ──┬─ per photo ──┐    │
│  (shadow DOM, brand theme tokens)      │      │    (fan-out, per-lens aggregate — never    │    │
└────────────────────────────────────────┘      │     a cross-tradition blend)               ▼    │
                                                │  → FloorPlanAnalysisService (floor-plan lens) │    │
                                                │  → SiteAnalysisService                     │    │
                                                │  → NumerologyService (read-time annotation) │    │
                                                │  MockAnalysisService (fallback)   ClaudeClient ┼──▶ Hackathon proxy
                                                │  Prompts (system prompt + tool schemas)         │    (Anthropic Messages API,
                                                │  PersistenceService (EF Core / SQLite) +        │     claude-sonnet-5)
                                                │  ObjectStore (local filesystem, report bodies)  │
                                                │  ScoringJobQueue / BackfillJob (Batch API)       │
                                                │  GradesFeedController (publish endpoint, §7)    │
                                                └────────────────────────────────────────────────┘
```

### 5.1 Backend — `backend/HarmonIQ.Api` (ASP.NET Core, .NET 10)

| Component | Responsibility |
|---|---|
| `ListingController` | `GET /api/listing/{id}`, `GET /api/property/{key}/subjects` (FR-42), thumbnail passthrough endpoint |
| `ListingService` | Resolve the shared listing ID → fetch listing (page scrape or internal listing API) → extract title/address/photo URLs/captions/numbers/floor-plan identity (`data-rentalkey`) → classify interior vs. non-interior → auto-select up to 6 on the photos path → suggest room types from captions → download + downscale photos into a short-lived in-memory cache (keyed `listingId/photoId`, TTL ~30 min) |
| `SampleListingProvider` | Serves the single-listing and multi-plan bundled offline fixtures |
| `GeoContextService` | Geocode the listing address; query public map data (OpenStreetMap Overpass) for roads/water/buildings and an elevation service for slope on each side of the building; produce the best-effort `environment` prefill (unknowns allowed); short-lived cache per listing |
| `OrientationProvider` (`IOrientationProvider`) | SightMap seam: resolves `subject_orientation` per FR-38. On this machine, a fixture implementation plus a SightMap client stub (no partner key present); the interface is what a real SightMap integration would implement without changing any downstream code |
| `FloorPlanAnalysisService` | Runs the `record_floorplan_analysis` forced-tool call (FR-36) over a plan image; layout findings only, orientation-independent |
| `NumerologyService` | Deterministic rules engine for Chinese/Feng Shui, Vastu digit-sum, and Western checks; emits verdicts, reasons, and remedies as **annotations** (FR-19/FR-20) — no score adjustment |
| `SiteAnalysisService` | Grade the confirmed environment against form-school Feng Shui and Vastu site rules, branching on `orientationPath` (FR-13/FR-15); deterministic rules for the clear-cut cases, one small Claude text call to phrase observations/suggestions, with template fallback |
| `AnalysisController` | Validation, live/demo routing, error mapping; resolves `photoId`s against the listing cache; computes per-set `score(set)`/`confidence(set)` (FR-25) — **never a cross-set blend** |
| `ClaudeClient` (`IClaudeClient`) | Typed `HttpClient` for `POST /v1/messages`; retries 429/5xx with linear backoff; maps 401/403 and network failures to `ClaudeUnavailableException` |
| `ClaudeAnalysisService` (`IAnalysisService`) | One request per photo fanned out via `Task.WhenAll` (photos path) or one request per plan image (floor-plan path); forced tool call (JSON Schema); aggregates scores/elements within a lens; one small text call for the per-set summary with local fallback |
| `MockAnalysisService` | Demo fallback from `Data/mock-analysis.json` templates keyed by room type, plus floor-plan-lens templates |
| `Prompts` | System prompts (tradition + orientation aware) and the `record_room_analysis` / `record_floorplan_analysis` tool schemas |
| `PersistenceService` (EF Core / SQLite) | `observations` / `analyses` tables per FR-41; `input_fingerprint` computation and skip-on-match check |
| `ObjectStore` | Local filesystem stand-in for `reports/{engine_version}/{subject}/{set}.json.gz`; interface matches the production object-store seam |
| `ScoringJobQueue` / `BackfillJob` | Task-zero sampling job (FR-48), batch backfill enumeration (FR-49), incremental trigger (FR-50), version-flip publish (FR-51) |
| `GradesFeedController` | The versioned grades feed apartments-web would consume (§7) — read-only, additive-nullable shape, never writes into apartments-web's own database |
| `Models/` | Record DTOs (camelCase JSON; `Tradition` serialized as `system`) |
| `Program.cs` | DI, root `.env` loader (env vars take precedence), serves the demo hosts (mock LDP, mock SRP) + embed bundle |

**Photo classification:** caption keywords first (cheap, reliable when present); photos without informative captions fall back to a single batched low-cost Claude call ("classify these thumbnails: interior room / exterior / floor plan / amenity") or, offline, to a permissive default (include, tagged Auto-detect).

### 5.2 Frontend — `frontend/` (React 18, TypeScript strict, Vite)

The build has one product target: the **embed bundle**, registering `<harmoniq-module listing-id="…" brand="…" state="badge|expanded">` as a web component (shadow DOM for style isolation). Internally it's a React app with a state machine (`idle → fetching-listing → analyzing → report`, with `refining` re-entering `analyzing`; multi-plan properties add `idle → fetching-subjects → chips` and skip `analyzing` entirely on render). `src/api.ts` mirrors backend DTOs, including the per-set `scores[]` shape (§4) — there is no `overallScore` type.

Components: `HarmonIQBadge` (single-listing compact score card, slimmed to Walk/Transit/Bike weight), `HarmonIQChip` (new: the ~22px per-plan pill, FR-3), `ReportPanel` (expanded view; renders per-set gauges, never a blended one), `ScoreGauge`, `ElementBars` (Feng Shui only), `RoomCard`, `FloorPlanCard` (new: layout findings), `SiteCard` (compass diagram + findings), `NumbersCard` / inline table annotation, `RefineDrawer` (photo selection + room tags, surroundings quadrant editor, numbers editor, principle-set view switch, session-only orientation override), `ModePill`.

**Theming:** all styling flows through CSS custom-property **design tokens** (`--hiq-primary`, `--hiq-font-display`, …) with per-brand presets (`themes/apartments.css`, `themes/apartmentfinder.css`, `themes/forrent.css`); the `brand` attribute selects a preset, and host pages may override tokens directly.

**Demo hosts:** (1) a static `mock-ldp.html` replicating the apartments-web `BuildingProfile` layout, now with a multi-plan floor-plan/availability section (chip column + numerology-annotated table) alongside the single-listing badge state, plus a brand switcher — served by the backend, which also serves the **HarmonIQ page** (`/harmoniq`) and the new **`mock-srp.html`** (search demo, FR-47); no separate app remains. (2) the **real apartments-web LDP run locally**, embedding the same bundle from `http://localhost:5080/embed/harmoniq-module.js` on a local-only demo branch (see FR-6b) — unchanged in mechanism; its consumer-side v2 work ships PR-ready/flagged-off (§7) rather than live. Dev mode: Vite on :5173 proxies `/api` to :5080.

### 5.3 LLM integration design decisions

| Decision | Rationale |
|---|---|
| **One request per photo or per plan image, in parallel** | The proxy sits behind API Gateway (~29 s hard timeout); a single multi-image request with large structured output times out (observed 503). Per-room / per-plan calls finish in ~10–15 s and stay far under the shared 50 req/s room limit. |
| **Forced tool call with JSON Schema** | `tool_choice: {type:"tool"}` guarantees parseable, schema-shaped output — no prose parsing, deterministic UI rendering. Applies to both `record_room_analysis` and `record_floorplan_analysis`. |
| **Separate small summary call, per principle set** | Each set's narrative needs cross-finding context within that set only; a 300-token text call on the set's findings digest is fast, with a deterministic local fallback. |
| **`claude-sonnet-5`** | Event guidance: Sonnet for iteration/volume; vision + tool use are sufficient for this task. Opus 5 reserved via `CLAUDE_MODEL` override. Backfill uses the same model via the Batch API. |
| **No streaming, no `temperature`** | Proxy returns 400 for `stream: true`; proxy rejects `temperature` as deprecated for Sonnet 5. |

### 5.4 Configuration

Root `.env` (gitignored) → mapped into .NET config; real environment variables override. `CLAUDE_API_KEY` (required for live mode; shared event key), `CLAUDE_BASE_URL`, `CLAUDE_MODEL`, plus `LISTING_SOURCE` (`scrape` | `api`), optional internal listing-API settings, `GEO_PROVIDER` settings (geocoder, Overpass, and elevation endpoints — public defaults, keyless), `SIGHTMAP_MODE` (`fixture` | `live`, no live key present on this machine) and `SIGHTMAP_BASE_URL`/`SIGHTMAP_API_KEY` (unused placeholders for the seam), `HARMONIQ_DB` (SQLite connection string), and `HARMONIQ_OBJECT_STORE_PATH` (local filesystem root for report bodies). Non-secret defaults live in `appsettings.json`.

---

## 6. Non-functional requirements

- **NFR-1 Latency:** *(v2: amended)* Chips and the single-listing badge render a **precomputed** grade — the bulk fetch (FR-42) resolves within the LDP's normal data-load budget, not a live analysis window; the interactive single-listing path (still used for new/incremental listings, Refine, and demo) keeps the v1 SLA: grade ≤ 25 s after the module loads (listing fetch + prefills ≤ 7 s, then parallel analysis; site and numerology run concurrently with room analysis). Refine re-grades ≤ 25 s on that same interactive path.
- **NFR-2 Demo resilience:** the happy path must complete with no network and no key (sample listing fixture + demo mode, including fixture environment and numbers) for **both** the single-listing and multi-plan fixtures.
- **NFR-3 Secrets:** the event key never appears in source or client code; server-side only, gitignored.
- **NFR-4 Rate-limit & sourcing citizenship:** *(v2: amended)* Interactive path: ≤ 9 concurrent Claude requests per analysis (6 rooms + classification + site phrasing + summary, or fewer on the single floor-plan-lens call); backoff on 429 per event ground rules. Listing fetches are polite: one page request per analysis, honest User-Agent, no crawling. Map/geocoding/elevation calls respect the public providers' usage policies (throttled, cached per listing). **Batch path:** backfill runs exclusively through the **Claude Batch API**, never the interactive endpoint, preserving the interactive room's 50 req/s budget for live traffic. **Internal listing + geo data access (`LISTING_SOURCE=api`) is a backfill prerequisite, not an optimization** — without it, batch backfill does not run (FR-49). Public Nominatim/Overpass/page-scraping remain acceptable for **dev/demo only**; they violate provider usage policy at backfill scale and must never run against production inventory.
- **NFR-5 Photo handling:** fetched photos are cached in memory only (TTL ~30 min), downscaled server-side, never persisted to disk.
- **NFR-6 Type safety:** API contract expressed as typed DTOs on both sides (C# records ↔ TS interfaces), including the per-set `scores[]` shape (§4).
- **NFR-7 Persistence:** *(v2: reversed from v1's "no persistence")* HarmonIQ persists durably via the observations/analyses/report-body split (§3.8/FR-41); this machine's stand-in is SQLite (EF Core) + a local filesystem object store, behind the same interfaces the production design specifies. Photo bytes and Claude credentials are still never persisted (unchanged from NFR-5/NFR-3). **Demo-mode output is never persisted:** any run with `mode != 'live'` or `status != 'ok'` is barred from writing to the projection — the demo host stays a read path over fixture/local data, never a source of durable grades.
- **NFR-8 Cultural framing:** numerology and site/interior/floor-plan verdicts are presented as cultural tradition ("in Chinese numerology…"), never as objective claims about safety or value — on every surface, including the search filter and SRP badge (FR-52).
- **NFR-9 Host isolation:** the module must not leak styles into or inherit breaking styles from the host page (shadow DOM), must not block host rendering (async load), must reserve its final footprint before data arrives (no layout shift, for both the single-listing badge and the per-plan chip column), and must fail closed to a hidden/unobtrusive state on error.

## 7. Constraints

- Hackathon Claude proxy only (Anthropic Messages API shape); **endpoint shuts down Wed Aug 12** — after that the module runs in demo mode unless repointed via `CLAUDE_BASE_URL`/`CLAUDE_API_KEY`.
- Shared event key and 50 req/s room-wide rate limit; `max_tokens` ≤ 16,384; no streaming. Batch backfill (§3.10) is separate infrastructure and not subject to the same room-wide interactive limit.
- Listing photo/floor-plan access depends on either public listing-page markup (scrape fragility accepted for the hackathon) or an internal CoStar listing API if credentials are available at the event.
- Environment prefill depends on public, keyless geo services (OSM/Overpass, open elevation data) for road/water/structures/slope; accuracy is best-effort and always user-correctable. Orientation is a separate concern (FR-13/FR-38) and comes only from SightMap. Satellite/street-view **imagery** analysis remains out of scope for environment prefill.
- Runs on a single dev machine; no deployment target in scope.
- The real-LDP host (FR-6b) requires a working local apartments-web dev environment; the integration lives on a local demo branch of that repo and is **never merged, pushed, or deployed**. HarmonIQ's own repo carries only the snippet/instructions, not apartments-web code.
- **Publish/consume boundary (v2):** HarmonIQ **publishes**; apartments-web **consumes**. HarmonIQ exposes a versioned grades feed/API (`GradesFeedController`, FR-42); apartments-web owns its own consumer, its additive-nullable migration, and its filter UI, authored on a **PR-ready branch behind a feature flag defaulting off**. HarmonIQ never writes into apartments-web's database. The standing rule is unchanged in substance, only in shape: still **no push, no merge, no PR** until apartments-web's owners lift it — this spec records the new boundary rather than letting it drift undocumented.
- A known Razor build anomaly in the local apartments-web dev environment limits how much of the SRP/filter/badge consumer work can be verified there; the mock SRP (FR-47) is the primary, always-verifiable demo surface for search, mirroring how the mock LDP is primary for the badge/chip surface.

## 8. Out of scope (hackathon)

Standalone app / URL entry (removed in v1.4), manual photo upload (removed in v1.1), accounts and renter identity of any kind (persistence itself is now in scope, §3.8 — but there is still no login, no accounts, and no recording of who used the search filter), deploying to the real production LDPs (embedding in a locally run apartments-web LDP is in scope per FR-6b; merging or shipping that change to live brand sites is not — and, per the v2 publish/consume boundary (§7), neither is merging apartments-web's own consumer/filter/migration work), Matterport ingestion, satellite or street-view imagery analysis, birth-date/personal (Kua/BaZi) numerology, crawling multiple listings, multi-language, PDF export, cost tracking, automated tests, running the **production-scale backfill** itself (task zero and cost/wall-clock estimates are in scope, §3.10; executing the full 100k-property batch against real inventory is not), the **landlord-visible dispute view** (FR-54, recorded not built), and a production decision on the **staging-advisor guardrail** (FR-55, deferred).

**Moved into scope for v2** (previously excluded here): search-results badges and filters (mock SRP demo, §3.9), floor-plan-driven scoring and orientation resolution (§3.8), and persistence (§3.8/NFR-7).

## 9. Acceptance criteria (demo)

1. `npm run build --prefix frontend` + `dotnet run --project backend/HarmonIQ.Api` serves the **mock LDP** (single-listing and multi-plan fixtures) and the **mock SRP** at :5080.
2. **Single-listing path (unchanged from v1):** opening the mock LDP's single-listing fixture shows the HarmonIQ badge as a score card directly beneath the "Getting Around" score cards, loading then resolving to a grade with no user input; the attribution link opens `/harmoniq`. Expanding it reveals the full v1-shape report (gauge, element bars, room cards, site card, numbers card).
3. **Multi-plan path:** opening the mock LDP's multi-plan fixture (≥3 floor plans, at least one with more than one available unit, at least one deliberately unscored per FR-37) shows a **grade chip per scored plan** in each plan's stats line, resolved at first paint from a single bulk `GET /api/property/{key}/subjects` call (FR-42) — no per-chip spinner, no layout shift when chips resolve, and no property-level score card. The unscored plan shows no chip and no placeholder. A single **"Scores provided by HarmonIQ"** line appears once at the foot of the section, never once per chip.
4. Clicking a chip opens the single shared report drawer for that plan: per-set score(s) render separately (Feng Shui and Vastu each with their own grade — never averaged), floor-plan-layout findings (not room photo cards) fill the findings section, and the unit availability table shows numerology as an inline annotation next to each unit's number, not as a score.
5. **Orientation gating is visible and correct:** the multi-plan fixture includes at least one plan with a SightMap-resolved orientation (Vastu grade present, `cohort.orientationPath: with`) and at least one without (Vastu absent/`insufficient_evidence`, Feng Shui still present, a short disclaimer that orientation wasn't available). Opening the Refine drawer on the without-orientation plan and supplying a manual facing computes a session-only Vastu score, visibly labeled as unsaved and absent from the plan's chip and from a page reload.
6. **Insufficient evidence renders as null, never as a bad grade:** the unscored plan (step 3) and any set below the confidence floor render no chip/no card — verified against the stored `analyses` rows (SQLite): no row, or a `status = 'insufficient_evidence'`/`'pending'` row, never a fabricated F.
7. **Search (mock SRP):** typing "feng shui", "fengshui", "vastu shastra", or "vaastu" into the mock SRP's search box surfaces a suggestion chip that opens the SRP with the HarmonIQ filter open and the matching set pre-selected; the parent HarmonIQ checkbox with no sub-selection returns properties/plans at B− or better within cohort; a property with no qualifying grade is excluded, with the "N properties have HarmonIQ scores in this area" caveat visible.
8. **Persistence & publish semantics, demonstrated locally:** running the local backfill/sample job against the fixture data writes `analyses` rows and a local object-store report body; re-running with an unchanged `input_fingerprint` skips re-analysis (verified via logs or an unchanged `computed_at`); bumping the local `engine_versions` row and re-running publishes a new set of projection values atomically (old and new never mixed mid-request) without mutating the prior version's rows. A run flagged `mode: demo` never appears in the projection.
9. The brand switcher restyles the module convincingly for at least two network brands without a reload (unchanged from v1); the host page's own styling is unaffected (shadow DOM isolation) on both the single-listing and multi-plan fixtures.
10. An unknown listing/property ID renders the appropriate unobtrusive error state; the API returns 404/400 as specified. Geo-service failures still produce a working report with an editable, empty surroundings section (unchanged from v1).
11. **Real LDP (local), unchanged in mechanism:** with apartments-web running locally on the demo branch and the HarmonIQ backend on :5080, opening an actual listing page shows the appropriate v2 surface (chip column for a multi-plan property, slim card for a single listing); the module's attribution link opens `http://localhost:5080/harmoniq`. The filter/badge/migration work on the apartments-web side is verified as a **PR-ready, feature-flagged-off** branch state (diff review + flag-off smoke check), not as a live user-facing flow on that host, per the v2 publish/consume boundary (§7).

## 10. Future roadmap

1. **Production-scale backfill** — run task zero (§3.10) and the full batch backfill against real inventory (v2 designs the pipeline and estimates ≈$7.3k batch / ≈$14.6k interactive at 100k properties; running it is still future work, gated on internal listing/geo access, FR-49).
2. **Production LDP + SRP rollout** — ship the embed module and the apartments-web filter/badge consumer (now PR-ready per §7) on real network LDPs and SRPs behind a feature flag; requires the landlord-visible dispute view (FR-54) and a resolved cultural-filter/steering review (§3.11) first.
3. **Satellite & street-view site analysis** — replace/augment the map-data prefill with Claude vision on aerial and street imagery for richer, hands-free environment detection.
4. **Matterport integration** — pull per-room panorama frames from the scan API; a Matterport-derived true orientation could sit alongside SightMap as a second `IOrientationProvider` implementation without changing the seam (§3.8).
5. **Personal numerology** — opt-in Kua number / BaZi (birth-date) compatibility between the renter and the unit.
6. **Staging advisor** — landlord-facing suggestion checklist with before/after re-scoring; blocked on resolving FR-55's "pay to fix the published grade" framing risk.
7. **Multi-photo-per-room reconciliation** — merge findings when several photos show the same room (single-listing/photos evidence path only).
8. **Consultant marketplace** — connect renters to human Feng Shui/Vastu consultants for paid deep-dives.
9. **Additional orientation sources** — a second `IOrientationProvider` (e.g., Matterport-derived, or a resolved partner SightMap tier) to raise orientation coverage beyond SightMap's current ~55% of multi-plan properties.
10. **Additional principle sets** — the per-set scoring model (FR-25) generalizes to traditions beyond Feng Shui/Vastu without a schema change; none are scoped yet.
</content>
