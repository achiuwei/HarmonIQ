# HarmonIQ — Project Specification

**Version:** 1.6 · **Date:** August 10, 2026 · **Event:** Apartments.com Hackathon (Aug 10–11, 2026)

> **v1.1:** Photo upload removed; photos pull from the Apartments.com listing.
> **v1.2:** Added surrounding-environment analysis (roads, water, structures, slopes per side) and numerology checks on the listing's numbers.
> **v1.3:** Added LDP surfacing and multi-brand integration across the CoStar rentals network.
> **v1.4:** Standalone app and URL-pasting removed. HarmonIQ is **an LDP module only**: it appears on the listing page, gets its listing context from the host page, and runs automatically.
> **v1.5:** Badge placement finalized to match the real apartments-web LDP (`BuildingProfile`): the compact score renders as a native score card directly beneath the listing's existing scores — the Transportation section's "Getting Around" score-card grid, which follows the Schools/Education section — with a **"Data provided by HarmonIQ"** attribution link to the HarmonIQ page, following the LDP's Local Logic / GreatSchools attribution convention.
> **v1.6:** Second demo host added: the module is also embedded in the **real apartments-web LDP running locally** (a local-only demo branch — never merged or deployed). The module gains cross-origin support (API base derived from the embed script's origin, overridable via `api-base`) and the API enables CORS.

---

## 1. Overview

HarmonIQ grades apartments against **Feng Shui** and **Vastu Shastra** principles and surfaces the result **directly on the Listing Detail Page (LDP)**. There is no separate app and nothing to paste: when a renter views a listing, a **HarmonIQ badge** appears with the listing's other scores — directly beneath the Transportation ("Getting Around") and Schools score cards; expanding it reveals a full report built from data the listing already has — its photos, its address and surroundings, and its numbers — across three lenses: **interiors** (per-room photo analysis), **site** (what surrounds the building on each side: roads, water, other structures, slopes), and **numbers** (numerology of the unit, floor, and street address), with an overall grade, adhering/violating findings, a five-element balance profile, and concrete renter-friendly suggestions.

### 1.1 Problem

For millions of renters — particularly households that practice Vastu or Feng Shui — spatial harmony is a top-of-funnel filter when choosing a home, yet no listing platform surfaces it. Renters today tour units with a consultant, or guess from photos. Landlords and agents have no way to communicate (or improve) this quality of their inventory.

### 1.2 Vision within the CoStar rentals network

Because the analysis runs on data the platform already hosts, every listing carries a **HarmonIQ Score** with no seller effort and no renter effort:
- An **LDP HarmonIQ module**: score badge rendered as a native score card beneath the LDP's existing scores (the "Getting Around" Walk/Transit/Bike cards and Schools ratings), expandable full report (rooms, site, numbers) inline on the page the renter is already reading.
- A **search filter/badge** for culturally-minded renters — differentiating the network in a way competitors don't offer.
- **One engine, every brand**: the same analysis service powers the whole network — Apartments.com, ApartmentFinder, ForRent.com, ApartmentHomeLiving, and sibling sites — since they share listing inventory; a listing analyzed once carries its score to every brand's LDP.
- A **staging advisor upsell** for landlords: the suggestions engine tells them exactly what to move before the photographer arrives.
- With **Matterport scans** (already common on listings), analysis could run automatically across every room with true compass orientation.

### 1.3 Hackathon scope

A working demo of the LDP experience on **two hosts**: (1) a **mock listing detail page** (static replica of a network LDP) hosting the embeddable HarmonIQ module — offline-safe, brand switcher, the primary demo; and (2) the **real apartments-web LDP running locally** — the module injected into the actual `BuildingProfile` page on a local-only demo branch to prove it drops into the production page unchanged. The apartments-web change is never merged or deployed. Opening either page shows the badge computing live; expanding shows the full report; the mock's brand switcher shows the module themed for at least two network brands. Live Claude vision + site + numerology analysis behind it. Single machine, no persistence, no auth.

---

## 2. Users & core flow

| User | Need |
|---|---|
| **Renter** (primary) | "Does this apartment have good energy? What would I need to fix?" — answered on the listing page they're already viewing. |
| **Listing agent / landlord** | "How do I stage this unit to score better?" |
| **Judge / stakeholder** (demo) | See the concept work live in under 2 minutes on a realistic LDP. |

**Core flow:** Renter opens a listing page → the embedded HarmonIQ module reads the listing identity from the host page and **automatically** fetches photos, address context, and numbers → the badge fills in with the grade (~15–20 s on first view) → renter expands the module to read the full report → optionally opens the **Refine** drawer to correct room tags, surroundings, numbers, entrance orientation, or tradition (Feng Shui / Vastu / both) → the report re-grades in place.

No URL entry, no upload, no separate destination: the module's only required input is the `listing-id` provided by the host LDP.

---

## 3. Functional requirements

### 3.1 LDP module & listing context
- **FR-1** HarmonIQ ships **only** as an embeddable module (self-contained web component / embed bundle: `<harmoniq-module listing-id brand [api-base]>`) rendered inside a host LDP. There is no standalone app and no user-entered listing URL. The module works cross-origin: it derives its API base from the origin the embed script was loaded from (overridable via the `api-base` attribute), and the HarmonIQ API serves `/api/*` with CORS enabled — so a host page on any local origin (e.g., the real apartments-web LDP) can embed it with a single script tag.
- **FR-2** The module reads its **listing identity from the host page** (`listing-id` attribute; production hosts would inject it server-side). Listing IDs are the network's **shared identity**, so any brand's LDP (apartments.com, apartmentfinder.com, forrent.com, apartmenthomeliving.com, …) resolves to the same listing and the same analysis.
- **FR-3** The module has two states: a **compact badge** (grade + score with a loading state while analysis runs) and an **expanded panel** (full report inline). The badge renders **beneath the listing's existing scores**, matching the apartments-web LDP (`Modules/BuildingProfile`): directly after the Transportation section's "Getting Around" score-card grid (`#vendor-score-cards`, the Local Logic Walk/Transit/Bike cards), which itself follows the Schools/Education section. The badge reuses the host's score-card pattern (title "HarmonIQ Score", grade tagline, score `/ 100`) so it reads as a native listing score; other network brands slot the module into their equivalent scores region. Beneath the card, a **"Data provided by HarmonIQ"** attribution line — "HarmonIQ" being the link — directs to the **HarmonIQ page** (demo: served by the demo host at `/harmoniq`), following the same convention as the LDP's "Scores provided by [Local Logic]" and "School data provided by [GreatSchools]" attributions. Analysis starts automatically when the badge first becomes visible.
- **FR-4** **Brand-agnostic theming:** all colors/typography derive from a design-token set overridable per brand; ship presets for Apartments.com, ApartmentFinder, and ForRent so the module visually belongs on each brand's LDP. The `brand` attribute (or host CSS tokens) selects the theme.
- **FR-5** The analysis API is **brand-aware** (`brand` parameter for attribution/theming) while results remain keyed by the shared listing ID — analyze once, render on every brand.
- **FR-6** Hackathon demo hosts, two of them:
  - **(a) Mock LDP** — a static replica of the apartments-web `BuildingProfile` layout (including a Schools/Education section with GreatSchools ratings and a Transportation section ending in the "Getting Around" score-card grid, so the badge renders in its real slot beneath those scores) embedding the module in both states with a brand switcher.
  - **(b) Real LDP, local only** — the module injected into the actual apartments-web `BuildingProfile` page (`Modules/BuildingProfile/Views/Index.cshtml`, immediately after the `_TransportationSection` partial), with `listing-id` bound to the page's `PropertyKey` and the embed script loaded from the HarmonIQ origin (`http://localhost:5080`). This lives on a **local-only demo branch of apartments-web that is never merged, pushed, or deployed**. A bundled **sample listing** fixture (metadata + illustrated room photos with deliberate violations — bed in line with the door, mirror facing the bed, under-bed storage, blocked chi path, heavy bookcase in the light-facing corner) backs the mock LDP so the demo works offline.

### 3.2 Automatic listing data ingestion
- **FR-7** Given a listing ID, the backend fetches the listing and extracts: title/address, photo URLs, photo captions/labels, and the unit/floor/street numbers.
- **FR-8** Photos are classified as **interior** or **non-interior** (exterior, floor plan, amenity, pool, map). Interior photos are **auto-selected up to a cap of 6** (by listing photo order) for analysis — no user action required.
- **FR-9** Room-type tags are **pre-filled from listing captions** when available, otherwise "Auto-detect" (the model identifies the room from the image).
- **FR-10** The backend downscales fetched photos server-side to ≤1568 px on the long edge before sending to the model.
- **FR-11** Defaults are refinable, not required: the expanded module includes a **Refine drawer** where the renter can deselect/reselect photos, correct room tags, edit surroundings and numbers, set entrance orientation, and switch tradition (`both` default | `fengshui` | `vastu`). Re-grading applies in place.
- **FR-12** Listing fetch failures (unknown ID, no photos, blocked request) render the badge in an unobtrusive error state ("HarmonIQ Score unavailable"); the module never breaks the host page.

### 3.3 Surrounding environment (site analysis)
- **FR-13** For each of the four sides of the building (N/E/S/W, resolved from the entrance orientation), the app captures what lies immediately outside: **road** (and type: quiet street / busy road / T-junction pointing at the building / highway), **water** (river, lake, pond, pool), **other structures** (taller building, similar buildings, open land), and **slope** (ground rises / falls / level).
- **FR-14** Environment data is **derived automatically from the listing address** — geocode, then query public map data (e.g., OpenStreetMap) for nearby roads, water bodies, and buildings, and an elevation service for slope direction. Values are editable in the Refine drawer; anything not derivable defaults to "unknown / not sure".
- **FR-15** Site findings are graded against form-school Feng Shui and Vastu site rules, including at minimum: *Feng Shui* — armchair position (higher support behind, open "bright hall" in front), T-junction or straight road aimed at the entrance (sha chi), water placement relative to the facing direction, being overshadowed by a much taller adjacent structure. *Vastu* — water bodies auspicious in N/NE, ground sloping down toward N/E auspicious and toward S/W inauspicious, heavier/taller masses auspicious in S/W, road-facing direction effects.
- **FR-16** Site analysis produces the same finding shape as rooms (adhering / violations with severity / suggestions) and its own score. Site suggestions must be renter-realistic: mitigation (curtains, plants, mirrors, entrance screening) rather than "move the river". Unknown environment values simply produce no findings — never guessed ones.

### 3.4 Numerology
- **FR-17** The unit number, floor number, and street address number are extracted from the listing (correctable in the Refine drawer).
- **FR-18** Each number is checked against numerology traditions consistent with the selected tradition filter: *Chinese/Feng Shui* — tetraphobia (4 and 4-containing numbers inauspicious, 8 wealth, 9 longevity, combinations like 14/24); *Vastu/Indian numerology* — digit-sum reading of the unit and street numbers; *Western* — 13, plus 666 flagged as culturally sensitive. The rules are **deterministic** (a rules engine, not the LLM).
- **FR-19** Numerology results render as a dedicated "Numbers" card: each number with a lucky / neutral / unlucky verdict, the tradition and reasoning, and renter-feasible remedies for unlucky numbers (e.g., interior door-number plaque adding a digit-sum, red accent at the threshold) — clearly framed as cultural guidance, not fact.
- **FR-20** Numerology contributes at most a small, bounded adjustment to the overall score (±3 points) so a photo-perfect apartment isn't tanked by its unit number.

### 3.5 Interior analysis
- **FR-21** Analyze each selected photo with Claude vision against the selected tradition(s). Findings must reference **only what is visible** in the image.
- **FR-22** Principles checked (non-exhaustive): *Feng Shui* — commanding position (bed/desk/stove), chi flow and clutter, five-element balance, mirror placement, bed under window/beam, pairs and symmetry, natural light, poison arrows. *Vastu* — directional alignment of rooms, Brahmasthan (open center), heavy furniture in S/W, water in N/NE, sleep orientation, direction-appropriate colors.
- **FR-23** Per room, return: room type, score 0–100, five-element balance (wood/fire/earth/metal/water, each 0–100), 2–4 **adhering** findings, 0–4 **violations** (each with severity `minor|moderate|major`), 2–4 **suggestions** (each with `effort` and `impact` rated `low|medium|high`). Every finding is tagged with its tradition (`fengshui|vastu|both`).
- **FR-24** Suggestions must be **renter-feasible**: furniture rearrangement, decor, plants, mirrors, textiles, lighting — never structural renovation.
- **FR-25** Aggregate to a whole-listing result: overall score (weighted: rooms 70%, site 30%, then numerology adjustment of ±3), letter grade (A+ ≥95 … F <40), averaged element balance, and a 2–3 sentence natural-language summary naming the strongest asset and highest-impact fix across all three lenses.

### 3.6 Report (expanded module)
- **FR-26** The expanded panel header shows the animated circular gauge with letter grade and score; gauge color follows score (green ≥75, amber ≥55, red below). The compact badge is a host-style score card showing grade + score plus the "Data provided by HarmonIQ" attribution link (FR-3).
- **FR-27** Five-element balance rendered as labeled, color-coded bars.
- **FR-28** One card per analyzed photo: thumbnail, room score chip, two-column findings (green "Working in your favor" / red "Breaking the principles" with severity badges), and suggestion cards with impact/effort tags.
- **FR-29** A **Site & Surroundings card** in the same two-column finding format, headed by a compass diagram summarizing what's on each side.
- **FR-30** A **Numbers card** per FR-19.
- **FR-31** A mode pill inside the expanded panel: `Live · <model>` or `Demo mode`; demo mode also shows an explanatory banner.

### 3.7 Resilience / demo safety
- **FR-32** If the Claude endpoint is unreachable, or the key is missing or rejected (401/403), the API returns a realistic **built-in demo analysis** (template-based, respecting the tradition filter and room-type hints) flagged `mode: "demo"` — the demo must never dead-end.
- **FR-33** Transient upstream failures (429, 5xx) — from the Claude proxy, the listing fetch, or map/elevation services — are retried with linear backoff (up to 3 retries) before failing. Map/elevation failures degrade to an empty (editable) surroundings section, never a blocked module.
- **FR-34** Invalid input (unknown listing ID, zero photos selected in Refine, >6 photos) returns HTTP 400 with a human-readable error; the module surfaces it inside its own bounds without affecting the host page.

---

## 4. API contract

Base URL: the HarmonIQ origin (default `http://localhost:5080`). The module calls these endpoints itself; the host page supplies only `listing-id`. Same-origin hosts (the mock LDP) use relative paths; cross-origin hosts (the local real LDP) work because the module prefixes its API base (derived from the embed script's origin, or `api-base`) and the API sends permissive CORS headers. Relative URLs the API returns (photo thumbnails) are resolved against the same base.

### 4.1 `GET /api/listing/{listingId}` — listing context for the module

(`listingId` is the network's shared listing identity; `sample` returns the bundled fixture. Optional `?brand=` for attribution.)

Response `200`:
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
`numbers` and `environment` are best-effort prefills (address parsing + map/elevation lookups); any field may be `"unknown"`. `selected` marks the auto-chosen analysis set (interior, capped at 6). All of it is editable via the Refine drawer.

Errors: `404` when the listing or its photos can't be found; `502` when the listing source can't be reached.

### 4.2 `POST /api/analyze`

Called automatically by the module with the defaults from `/api/listing`, and again with edited values when the renter refines.

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
(`brand` is optional attribution; results are keyed by the shared `listingId`, so an analysis performed via one brand serves all of them.)

Response `200`:
```json
{
  "mode": "live | demo",
  "modelId": "claude-sonnet-5 (live only)",
  "notice": "explanation (demo only)",
  "listing": { "listingId": "xyz123", "title": "…", "address": "…", "url": "…" },
  "analysis": {
    "overallScore": 67,
    "grade": "C+",
    "summary": "2-3 sentence assessment…",
    "elementBalance": { "wood": 38, "fire": 12, "earth": 42, "metal": 8, "water": 5 },
    "rooms": [
      {
        "photoId": "p1",
        "roomType": "Bedroom",
        "score": 62,
        "elementBalance": { "wood": 0, "fire": 0, "earth": 0, "metal": 0, "water": 0 },
        "adhering":   [ { "principle": "…", "observation": "…", "system": "fengshui|vastu|both" } ],
        "violations": [ { "principle": "…", "observation": "…", "severity": "minor|moderate|major", "system": "…" } ],
        "suggestions": [ { "title": "…", "detail": "…", "effort": "low|medium|high", "impact": "low|medium|high" } ]
      }
    ],
    "site": {
      "score": 71,
      "adhering":   [ { "principle": "Armchair Position", "observation": "…", "system": "fengshui" } ],
      "violations": [ { "principle": "T-Junction Facing the Entrance", "observation": "…", "severity": "major", "system": "fengshui" } ],
      "suggestions": [ { "title": "Screen the entrance line", "detail": "…", "effort": "low", "impact": "high" } ]
    },
    "numerology": {
      "scoreAdjustment": -2,
      "checks": [
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

Errors: `400 { "error": "…" }` for invalid input (unknown `photoId`, zero/too many photos); `502 { "error": "…" }` for non-fallback upstream failures. `rooms[i]` corresponds to `photos[i]` in request order.

### 4.3 `GET /api/health`

`200 { "ok": true, "live": <bool: Claude key configured> }`

---

## 5. Architecture

```
┌─ Host LDP (mock + real, local) ────────┐      ┌─ ASP.NET Core API (:5080) ─────────────────────┐
│ mock-ldp.html (brand switcher)         │      │ ListingController → ListingService ────────────┼──▶ Listing source
│  <harmoniq-module listing-id brand> ───┼──────┼→   (resolve shared ID, fetch, extract +        │    (network listing page or
│   badge state → auto GET /api/listing  │      │     classify photos, cache, thumbnails)        │     internal listing API)
│   → auto POST /api/analyze             │      │   → GeoContextService (environment prefill) ───┼──▶ Geocoder / OSM Overpass /
│   expanded state: report + Refine      │      │→ AnalysisController                            │    elevation (public, keyless)
│   drawer → re-POST /api/analyze        │      │  → ClaudeAnalysisService ──┬─ per photo ──┐    │
│  (shadow DOM, brand theme tokens)      │      │    (fan-out, aggregate)    │ (parallel)   │    │
└────────────────────────────────────────┘      │  → SiteAnalysisService     ▼              │    │
                                                │  → NumerologyService    ClaudeClient ─────┼────┼──▶ Hackathon proxy
                                                │  MockAnalysisService (fallback)           │    │    (Anthropic Messages API,
                                                │  Prompts (system prompt + tool schema)    │    │     claude-sonnet-5)
                                                └────────────────────────────────────────────────┘
```

### 5.1 Backend — `backend/HarmonIQ.Api` (ASP.NET Core, .NET 10)

| Component | Responsibility |
|---|---|
| `ListingController` | `GET /api/listing/{id}`, thumbnail passthrough endpoint |
| `ListingService` | Resolve the shared listing ID → fetch listing (page scrape or internal listing API) → extract title/address/photo URLs/captions/numbers → classify interior vs. non-interior → auto-select up to 6 → suggest room types from captions → download + downscale photos into a short-lived in-memory cache (keyed `listingId/photoId`, TTL ~30 min) |
| `SampleListingProvider` | Serves the bundled offline fixture for `sample` |
| `GeoContextService` | Geocode the listing address; query public map data (OpenStreetMap Overpass) for roads/water/buildings and an elevation service for slope on each side of the building; produce the best-effort `environment` prefill (unknowns allowed); short-lived cache per listing |
| `NumerologyService` | Deterministic rules engine for Chinese/Feng Shui, Vastu digit-sum, and Western checks; emits verdicts, reasons, remedies, and the bounded score adjustment |
| `SiteAnalysisService` | Grade the confirmed environment against form-school Feng Shui and Vastu site rules (deterministic rules for the clear-cut cases; one small Claude text call to phrase observations/suggestions, with template fallback) |
| `AnalysisController` | Validation, live/demo routing, error mapping; resolves `photoId`s against the listing cache; merges room, site, and numerology results into the weighted overall score |
| `ClaudeClient` (`IClaudeClient`) | Typed `HttpClient` for `POST /v1/messages`; retries 429/5xx with linear backoff; maps 401/403 and network failures to `ClaudeUnavailableException` |
| `ClaudeAnalysisService` (`IAnalysisService`) | One request per photo fanned out via `Task.WhenAll`; forced `record_room_analysis` tool call (JSON Schema); aggregates scores/elements; one small text call for the overall summary with local fallback |
| `MockAnalysisService` | Demo fallback from `Data/mock-analysis.json` templates keyed by room type |
| `Prompts` | System prompt (tradition + orientation aware) and the tool JSON Schema |
| `Models/` | Record DTOs (camelCase JSON; `Tradition` serialized as `system`) |
| `Program.cs` | DI, root `.env` loader (env vars take precedence), serves the demo host + embed bundle |

**Photo classification:** caption keywords first (cheap, reliable when present); photos without informative captions fall back to a single batched low-cost Claude call ("classify these thumbnails: interior room / exterior / floor plan / amenity") or, offline, to a permissive default (include, tagged Auto-detect).

### 5.2 Frontend — `frontend/` (React 18, TypeScript strict, Vite)

The build has one product target: the **embed bundle**, registering `<harmoniq-module listing-id="…" brand="…" state="badge|expanded">` as a web component (shadow DOM for style isolation). Internally it's a React app with a state machine (`idle → fetching-listing → analyzing → report`, with `refining` re-entering `analyzing`). `src/api.ts` mirrors backend DTOs.

Components: `HarmonIQBadge` (compact score card matching the host LDP's score-card pattern, with loading/error states and the "Data provided by HarmonIQ" attribution link), `ReportPanel` (expanded view), `ScoreGauge`, `ElementBars`, `RoomCard`, `SiteCard` (compass diagram + findings), `NumbersCard`, `RefineDrawer` (photo selection + room tags, surroundings quadrant editor, numbers editor, orientation + tradition controls), `ModePill`.

**Theming:** all styling flows through CSS custom-property **design tokens** (`--hiq-primary`, `--hiq-font-display`, …) with per-brand presets (`themes/apartments.css`, `themes/apartmentfinder.css`, `themes/forrent.css`); the `brand` attribute selects a preset, and host pages may override tokens directly.

**Demo hosts:** (1) a static `mock-ldp.html` replicating the apartments-web `BuildingProfile` layout (gallery, pricing, amenities, Schools/Education with GreatSchools ratings, and a Transportation section ending in the "Getting Around" score-card grid) with the badge embedded beneath those score cards and the expanded report in the body, plus a brand switcher — served by the backend, which also serves the **HarmonIQ page** the attribution link points to (`/harmoniq`); no separate app remains. (2) the **real apartments-web LDP run locally**, embedding the same bundle from `http://localhost:5080/embed/harmoniq-module.js` on a local-only demo branch (see FR-6b) — the module detects the cross-origin script and prefixes all API calls, thumbnails, and the attribution link with the HarmonIQ origin. Dev mode: Vite on :5173 proxies `/api` to :5080.

### 5.3 LLM integration design decisions

| Decision | Rationale |
|---|---|
| **One request per photo, in parallel** | The proxy sits behind API Gateway (~29 s hard timeout); a single multi-image request with large structured output times out (observed 503). Per-room calls finish in ~10–15 s and stay far under the shared 50 req/s room limit. |
| **Forced tool call with JSON Schema** | `tool_choice: {type:"tool"}` guarantees parseable, schema-shaped output — no prose parsing, deterministic UI rendering. |
| **Separate small summary call** | Whole-listing narrative needs cross-room context; a 300-token text call on the findings digest is fast, with a deterministic local fallback. |
| **`claude-sonnet-5`** | Event guidance: Sonnet for iteration/volume; vision + tool use are sufficient for this task. Opus 5 reserved via `CLAUDE_MODEL` override. |
| **No streaming, no `temperature`** | Proxy returns 400 for `stream: true`; proxy rejects `temperature` as deprecated for Sonnet 5. |

### 5.4 Configuration

Root `.env` (gitignored) → mapped into .NET config; real environment variables override. `CLAUDE_API_KEY` (required for live mode; shared event key), `CLAUDE_BASE_URL`, `CLAUDE_MODEL`, plus `LISTING_SOURCE` (`scrape` | `api`), optional internal listing-API settings, and `GEO_PROVIDER` settings (geocoder, Overpass, and elevation endpoints — public defaults, keyless). Non-secret defaults live in `appsettings.json`.

---

## 6. Non-functional requirements

- **NFR-1 Latency:** badge shows a grade ≤ 25 s after the module loads (listing fetch + prefills ≤ 7 s, then parallel analysis; site and numerology run concurrently with room analysis). Refine re-grades ≤ 25 s.
- **NFR-2 Demo resilience:** the happy path must complete with no network and no key (sample listing fixture + demo mode, including fixture environment and numbers).
- **NFR-3 Secrets:** the event key never appears in source or client code; server-side only, gitignored.
- **NFR-4 Rate-limit citizenship:** ≤ 9 concurrent Claude requests per analysis (6 rooms + classification + site phrasing + summary); backoff on 429 per event ground rules. Listing fetches are polite: one page request per analysis, honest User-Agent, no crawling. Map/geocoding/elevation calls respect the public providers' usage policies (throttled, cached per listing).
- **NFR-5 Photo handling:** fetched photos are cached in memory only (TTL ~30 min), downscaled server-side, never persisted to disk.
- **NFR-6 Type safety:** API contract expressed as typed DTOs on both sides (C# records ↔ TS interfaces).
- **NFR-7 No persistence:** stateless beyond the short-lived photo cache; nothing is stored durably.
- **NFR-8 Cultural framing:** numerology and site verdicts are presented as cultural tradition ("in Chinese numerology…"), never as objective claims about safety or value.
- **NFR-9 Host isolation:** the module must not leak styles into or inherit breaking styles from the host page (shadow DOM), must not block host rendering (async load), and must fail closed to a hidden/unobtrusive state on error.

## 7. Constraints

- Hackathon Claude proxy only (Anthropic Messages API shape); **endpoint shuts down Wed Aug 12** — after that the module runs in demo mode unless repointed via `CLAUDE_BASE_URL`/`CLAUDE_API_KEY`.
- Shared event key and 50 req/s room-wide rate limit; `max_tokens` ≤ 16,384; no streaming.
- Listing photo access depends on either public listing-page markup (scrape fragility accepted for the hackathon) or an internal CoStar listing API if credentials are available at the event.
- Environment prefill depends on public, keyless geo services (OSM/Overpass, open elevation data); accuracy is best-effort and always user-correctable. Satellite/street-view **imagery** analysis is out of scope — only vector map data and elevation are used.
- Runs on a single dev machine; no deployment target in scope.
- The real-LDP host (FR-6b) requires a working local apartments-web dev environment; the integration lives on a local demo branch of that repo and is **never merged, pushed, or deployed**. HarmonIQ's own repo carries only the snippet/instructions, not apartments-web code.

## 8. Out of scope (hackathon)

Standalone app / URL entry (removed in v1.4), manual photo upload (removed in v1.1), persistence/accounts, **deploying to the real production LDPs** (embedding in a *locally run* apartments-web LDP is in scope per FR-6b; merging or shipping that change to live brand sites is not), search-results badges and filters, Matterport ingestion, floor-plan/orientation inference, satellite or street-view imagery analysis, birth-date/personal (Kua/BaZi) numerology, crawling multiple listings, multi-language, PDF export, cost tracking, automated tests.

## 9. Acceptance criteria (demo)

1. `npm run build --prefix frontend` + `dotnet run --project backend/HarmonIQ.Api` serves the **mock LDP** at :5080.
2. Opening the mock LDP shows the HarmonIQ badge as a score card directly beneath the Transportation section's "Getting Around" score cards (which follow the Schools section), in a loading state, resolving to a grade without any user input (~15–25 s live). The badge shows a "Data provided by HarmonIQ" link that opens the HarmonIQ page (`/harmoniq`).
3. Expanding the badge reveals the full report inline: gauge, element bars, room cards whose findings reference visible photo content (pill shows `Live · claude-sonnet-5`), a **Site & Surroundings card** (compass diagram + findings — e.g., a T-junction produces the sha-chi violation), and a **Numbers card** (e.g., unit 414 flagged with reasoning and a remedy under Feng Shui).
4. The **Refine drawer** works: editing room tags, surroundings, numbers, orientation, or tradition re-grades in place and changes the corresponding findings deterministically.
5. The brand switcher restyles the module convincingly for at least two network brands without a reload; the host page's own styling is unaffected (shadow DOM isolation).
6. The bundled **sample listing** completes the same flow offline (demo mode banner when no key is configured), including fixture environment and numbers.
7. An unknown listing ID renders the badge's unobtrusive error state; the API returns 404/400 as specified when called directly. Geo-service failures still produce a working report with an editable, empty surroundings section.
8. **Real LDP (local):** with apartments-web running locally on the demo branch and the HarmonIQ backend on :5080, opening an actual listing page shows the HarmonIQ score card directly beneath the "Getting Around" score cards; the module analyzes that listing (or falls back to demo mode), expands inline, and its "Data provided by HarmonIQ" link opens `http://localhost:5080/harmoniq`. The host page's own styling and scripts are unaffected.

## 10. Future roadmap

1. **Internal listing API integration** — replace page scraping with CoStar's listing/photo services; batch-analyze whole markets.
2. **Production LDP rollout** — ship the embed module on real network LDPs behind a feature flag; persist reports keyed by shared listing ID; badge search results and filter by score/tradition across all brands.
3. **Satellite & street-view site analysis** — replace/augment the map-data prefill with Claude vision on aerial and street imagery for richer, hands-free environment detection.
4. **Matterport integration** — pull per-room panorama frames from the scan API; derive true orientation from the floor plan; auto-run on listing publish.
5. **Personal numerology** — opt-in Kua number / BaZi (birth-date) compatibility between the renter and the unit.
6. **Staging advisor** — landlord-facing suggestion checklist with before/after re-scoring.
7. **Multi-photo-per-room reconciliation** — merge findings when several photos show the same room.
8. **Consultant marketplace** — connect renters to human Feng Shui/Vastu consultants for paid deep-dives.
