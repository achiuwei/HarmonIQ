# apartments-web consumer — five-tradition HarmonIQ grades

**Date:** 2026-08-11 · **Audience:** a session working in the **apartments-web** repo, not this one.
**Status of the thing you are consuming:** designed and agreed, **not yet built** (see §6).

---

## 0. Read this first

You are building the **consumer** side. HarmonIQ **publishes**; apartments-web **consumes**.
HarmonIQ never writes into apartments-web's database. That boundary is fixed (SPEC §7).

Three standing rules that are **not yours to lift**:

1. **No push, no merge, no PR** until apartments-web's owners lift it. Work lands on the local
   `harmoniq-demo` branch only.
2. Every user-visible change ships **behind a feature flag defaulting OFF**.
3. The schema change is **additive-nullable only**. No column is made non-null, no existing column
   changes type, nothing is backfilled destructively.

There is a **known Razor build anomaly** in the local apartments-web dev environment. It limits how
much of the SRP/filter/badge work can actually be verified there. Acceptance for this work is
*diff review + a flag-off smoke check*, **not** a live user-facing flow. Do not burn hours trying to
get the live path green; that is a known environment problem, not your bug.

---

## 1. What changed in the design

HarmonIQ scored **two** traditions. It is moving to **five**:

| id | Tradition | Culture | Orientation required for a stored grade? |
|---|---|---|---|
| `fengshui` | Feng Shui | Chinese | No — degrades via coverage |
| `vastu` | Vastu Shastra | Indian | **Yes** |
| `pungsu` | Pungsu-jiri (풍수지리) | Korean | No — degrades via coverage |
| `kaso` | Kasō / Fūsui (家相) | Japanese | **Yes** |
| `phongthuy` | Phong Thủy | Vietnamese | No — degrades via coverage |

`vastu` and `kaso` sit behind an orientation gate: with no resolved facing from Engrain SightMap,
they produce `insufficient_evidence`, **not** a renormalized score. The other three drop their
directional rules to not-applicable, which lowers coverage (and that lens's weight) rather than the
score.

**There is still no blended headline number.** Five scores, never averaged, never ranked against
each other. This is a hard product rule, not a preference — it predates this change and survives it.

### The two things most likely to break your existing assumptions

- **`"both"` is dead.** It was a two-tradition encoding. Anywhere you have a
  `fengshui | vastu | both` tri-state, it becomes a **set of tradition ids**. "Both" was always a UI
  union of two rows, never a third score — now it is a union of up to five.
- **Default display is still two.** Renters pick their traditions; with no preference the surface
  shows **Feng Shui + Vastu**, exactly today's visual weight. All five are *stored and served*; only
  two *render* by default. Do not size your UI for one chip, and do not size it for five.

---

## 2. The contract you consume

Base URL locally: `http://localhost:5080`. CORS is enabled on `/api/*`.

### `GET /api/feed/grades` — the versioned feed (this is your primary integration)

```
?engineVersion=<string>   &cursor=<string>   &limit=<1..500, default 100>
&includeUnpublished=<bool, default false>
```

Response — `GradesFeedPage`:

```jsonc
{
  "engineVersion": "…",
  "rows": [
    {
      "id": "…",
      "listingId": "…",
      "floorPlanId": null,        // nullable — mirrors your child-table shape
      "principleSet": "fengshui", // ← now one of FIVE values
      "score": 82,                // nullable
      "grade": "B+",              // nullable
      "cohort": "…",              // evidence path × orientation path
      "confidence": 0.74,         // nullable
      "engineVersion": "…",
      "computedAt": "2026-08-11T…Z"
    }
  ],
  "nextCursor": "…"               // null on the last page
}
```

**Status codes that matter:**

- `404` — unknown engine version.
- `409` — the version exists but is **not published**. This is deliberate: a consumer must never
  silently ingest rows from a version mid-rollout. `?includeUnpublished=true` is the explicit opt-in
  for internal tooling.

> **Correction (verified in code).** `includeUnpublished=true` will **not** get you data, and you
> should not plan around it. Projection rows are written *only* by `PublishVersionAsync`
> (`PublicationService.cs:83`), which sets `PublishedAt` in the same transaction (`:91`). So an
> unpublished version has **zero rows** — the flag turns a 409 into an empty 200. A published
> version never needed the flag. Worse, publish selects only `Mode == "live" && Status == "ok"`
> (`:46`), so on a machine with no Claude key (everything demo mode) publish writes zero rows
> permanently. **Consuming the live feed locally requires a live-key run plus an explicit publish.**
> Build against a fixture source; treat the feed as the production path.

**Version pinning is the whole contract.** Ask for version X, get exactly X's rows, forever — even
after a newer version publishes. That is what keeps an SRP badge and the LDP card it links to in
agreement when they are fetched seconds apart. Carry `engineVersion` from the SRP into the LDP
request. Projection rows are never mutated in place.

### `GET /api/property/{propertyKey}/subjects` — what the LDP module itself calls

`?engineVersion=&sets=` (`sets` = comma-separated tradition ids; empty means all).
Returns `SubjectsResponse { propertyKey, engineVersion, mode, subjects[] }` where each
`SubjectGrade` carries `sets: SetGrade[]` and `units: UnitNumerologyAnnotation[]`.

```jsonc
// SetGrade
{ "principleSet": "pungsu", "status": "ok", "score": 77, "grade": "B",
  "confidence": 0.68, "evidencePath": "…", "orientationPath": "…" }
```

Two behaviours to respect:
- `sets: []` (empty) is the **only** unscored signal. Render **nothing** — never a placeholder
  grade, never a zero, never an "F". The subject is still returned so the section's footprint is
  known at first paint.
- `score`/`grade` are null unless `status` is `ok`. `insufficient_evidence` is **permanent and
  non-retryable** — it is an answer, not a pending state.

### `GET /api/search` and `/api/search/suggest`

`/api/search?sets=&min=&engineVersion=&limit=` — `min` defaults to `B-`. Search reads **only
published** rows; there is **no** `includeUnpublished` escape hatch here (409 if unpublished).
`/api/search/suggest?q=` returns `404` (not a 200 with a null body) when the query is not a
recognized synonym.

A `SearchHit` carries **every** stored set for its `bestSubjectId`, not just the filtered-on ones —
so a renter who matched on Vastu still sees the Feng Shui badge. `bestSubjectId` is the
best-qualifying single subject on a multi-plan property, **never a cross-plan blend**.

---

## 3. Your work items on the apartments-web side

1. **Migration** — additive-nullable grade columns on the listing/floor-plan child table. Five
   traditions means the shape is *rows keyed by `principleSet`*, not five column pairs. If your
   current sketch has `FengShuiScore` / `VastuScore` columns, that does not extend — go to a child
   table keyed `(listingId, floorPlanId, principleSet, engineVersion)`.
2. **Feed consumer** — paginate via `cursor` until `nextCursor` is null, pinned to one
   `engineVersion` per ingest run. Ingest is per-version and atomic; never merge two versions' rows.
3. **Filter UI (SRP)** — a "HarmonIQ" parent checkbox with five sub-selections. Parent checked with
   no sub-selection = union of all five. Threshold defaults to `B-`.
4. **Search synonyms** — the typeahead needs to recognize the new traditions and their common
   spellings: `pungsu` / `pungsu-jiri` / `poongsu` / `풍수`; `kaso` / `kasou` / `fusui` / `家相`;
   `phong thuy` / `phong thủy`. Plus existing Feng Shui and Vastu spellings.
5. **Badge/chip** — reuse one component across SRP badge and LDP chip. ~22px inline-flex pill, grade
   only, no card chrome, no gauge, no heading.
6. **Attribution** — a single "Scores provided by HarmonIQ" line at the foot of the floor-plans
   section. **Never once per chip.**
7. **Feature flag** — one flag, default off, gating all of the above.

---

## 4. Things that will bite you

- **Do not average the five scores.** Not for sorting, not for a summary tile, not "just for the
  filter". Ranking traditions against each other implies they measure the same thing.
- **Cohort is not decoration.** Scores rank and filter **within cohort** (evidence path × orientation
  path). Comparing a photo-evidence score to a floorplan-evidence score is comparing incomparable
  things.
- **Numerology no longer affects any score — fixed upstream.** FR-20 is now honoured in code:
  `ScoreMath.Aggregate` has no numerology parameter at all (a unit test asserts the parameter
  cannot be reintroduced), and `NumerologyService` returns an adjustment of 0 for every tradition.
  UI copy stating numerology is annotation-only is now accurate.
- **The feed carries `subjectId` — added upstream.** `ProjectionRow.SubjectId` is a first-class
  field on every feed row, so you can go straight to
  `/api/subject/{subjectId}/report/{principleSet}` for the reasoning page. Do **not** parse the row
  `Id`; its composition remains an internal detail.
- **Locally, nothing is published** — see the correction in §2. The feed returns an empty page, not
  an error. Search will 409 with no workaround; that part is intended.

---

## 5. Cultural-sensitivity guardrail

A "filter by cultural practice" surface is a **fair-housing-adjacent** feature: it correlates
strongly with national origin and religion. The SPEC already flags a resolved
cultural-filter/steering review (§3.11) as an explicit **blocker for production LDP + SRP rollout**.
Two implications for you:

- Keep the flag off. That is not a formality here.
- Do not add anything that lets a **landlord or agent** filter or sort *renters*, or that surfaces
  which tradition a renter selected. The filter is renter-facing only, and no record of who used it
  is kept (there is no accounts system and none should be added for this).

---

## 6. Status — the five traditions are implemented

**All five now exist end to end upstream.** `PrincipleSets` carries five constants resolved through
a `TraditionRegistry`, each tradition owning its own site rule catalogue, numerology, rules version,
orientation gate, display name, and interpretation prompt. The full suite is green (313 tests).

What that means for your side:

- The feed emits up to **five** `principleSet` values per subject: `fengshui`, `vastu`, `pungsu`,
  `kaso`, `phongthuy`.
- **`vastu` and `kaso` are orientation-gated** — with no resolved facing they come back
  `insufficient_evidence` with no score. The other three degrade through coverage.
- **`elementBalance` is present for four traditions, absent for `vastu`.** It is wǔxíng, shared by
  the Sinitic traditions; Vastu's pancha bhuta are a different five and the section is omitted
  rather than zeroed. Any UI rule saying "Feng Shui only" is wrong in both directions.
- Search synonyms for all five ship upstream, including native script (`家相`, `풍수지리`, `वास्तु`).
  Note `風水` resolves to **Feng Shui**, not Kasō — the characters are shared and the query is
  routed to the older, far more commonly searched tradition.
- `ProjectionRow.PrincipleSet` was already a free-form string, so **the feed's wire shape did not
  change**. One additive migration ran upstream, for `SubjectId` only.

Still write the consumer **driven by the `principleSet` values present in the feed**, not by a
hardcoded list of five. That is what makes a sixth tradition a no-op on your side.

### Known limitation to design around

The per-culture interpretation runs on the **photos** evidence path. On the **floor-plan** path the
findings are adjacency facts, which are genuinely tradition-neutral, so plan-only subjects will show
less divergence between traditions than photo subjects do — their scores differ via the site
catalogues rather than the interiors lens. Do not treat two traditions agreeing closely on a
floor-plan subject as a bug.

---

## 7. Paste-able opener for the other terminal

> I'm working in the apartments-web repo on the local `harmoniq-demo` branch, building the
> consumer side for HarmonIQ grades. Read
> `<HarmonIQ repo>/docs/handoffs/2026-08-11-apartments-web-five-tradition-consumer.md` for the full
> contract and constraints. Hard rules: no push/merge/PR, everything behind a feature flag defaulting
> off, additive-nullable migration only, and HarmonIQ never writes to our DB — we only read its
> versioned feed at `GET http://localhost:5080/api/feed/grades`. Note the feed returns 409 until a
> version is published, and nothing is ever published locally, so pass `includeUnpublished=true`.
> Key design point: grades are keyed by a `principleSet` string that is moving from 2 values to 5, so
> build everything driven by the values present in the feed rather than a hardcoded list.
