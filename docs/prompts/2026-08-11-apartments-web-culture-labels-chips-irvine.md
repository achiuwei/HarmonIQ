# Prompt — apartments-web repo: drop culture labels, label the LDP chips, move to the Irvine listings

**Repo:** `apartments-web` (local `harmoniq-demo` branch) · **Date:** 2026-08-11
**Companion prompt (other repo):** `docs/prompts/2026-08-11-retire-culture-and-irvine-fixtures.md`

---

## Paste this into a session in the apartments-web repo

You are working in the **apartments-web** repo on the local `harmoniq-demo` branch, on the HarmonIQ
consumer surfaces. Background contract: `<HarmonIQ repo>/docs/handoffs/2026-08-11-apartments-web-five-tradition-consumer.md`.

**Standing rules, not yours to lift:** no push, no merge, no PR — local branch only. Everything stays
behind the existing feature flag, defaulting off. There is a known Razor build anomaly in this dev
environment, so acceptance is **diff review plus a flag-off smoke check**, not a live user-facing
flow. Do not burn hours trying to get the live path green.

Four changes.

### Change 1 — remove country names from the Cultural Fit filter

The filter currently renders `China — Feng Shui`, `India — Vastu Shastra`, and so on. It should show
the tradition name alone: `Feng Shui`, `Vastu Shastra`, `Pungsu-jiri`, `Kasō`, `Phong Thủy`.

- `Source/AptsWeb/Apps/ListingSearch/Client/components/filters/use-cultural-fit.ts:19-25` — drop
  `culture` from the `TRADITIONS` table and from the `TraditionOption` interface (line 36-40) and
  the `availableTraditions` mapping.
- `Source/AptsWeb/Apps/ListingSearch/Client/components/filters/sections/cultural-fit-filter-section.vue`
  — the label expression at the `cultural-fit-tradition-label` span becomes `{{ tradition.label }}`.
  Update the ASCII mockup in the file's doc comment (lines 6-8), which still shows the country form.
- Check `Client/styles/components/cultural-fit-filter.css` for width or two-column assumptions sized
  around the longer `Country — Tradition` strings.

Keep the behaviour that the sub-selection list is generated from the `principleSet` values **present
on the page**, never a hardcoded list. The unknown-id fallback (title-cased, sorted last) still
applies — it just no longer has a culture to omit.

### Change 2 — remove country names from the reasoning page

`Source/AptsWeb/Modules/HarmonIQ/Views/Reasoning.cshtml`:

- Line 54 — the per-tradition heading is `@set.Display.Label@(set.Display.Culture != null ? $" ({set.Display.Culture})" : "")`.
  It becomes the label alone.
- Lines 174-183 — the "What each tradition evaluates" list hardcodes `Feng Shui (Chinese)`,
  `Vastu Shastra (Indian)`, `Pungsu-jiri (Korean)`, `Kasō / Fūsui (Japanese)`,
  `Phong Thủy (Vietnamese)`. Drop the parentheses; keep each tradition's description.

Then retire the field itself so it cannot come back:

- `Source/AptsWeb/Modules/HarmonIQ/Models/TraditionDisplay.cs` — remove `Culture` from the record and
  from all five `Known` entries. Update the type's doc comment, which currently says an unknown id
  renders "title-cased, no culture label"; that distinction disappears.
- Its stale comment also claims HarmonIQ "emits only `fengshui` and `vastu` today; the other three
  are designed upstream but not implemented". All five are implemented upstream now. Fix it while
  you are in the file.

**Leave the numerology notes alone.** Strings like "Contains 4, read as inauspicious in several
Chinese-speaking regions" in `Modules/HarmonIQ/Content/harmoniq-grades.json` are the tradition's own
reading of a number, not a label on the tradition. They stay.

### Change 3 — show the tradition name next to the grade, on the LDP only

Today `Modules/HarmonIQ/Views/_GradeChips.cshtml` renders the **grade only** (`B+`), with the
tradition carried in `title`/`aria-label`. The LDP floor-plan chips should read `Feng Shui B+`.

The complication: that partial is used **verbatim** by both the LDP price grid
(`Modules/Profile/Views/Sections/Shared/PriceGridModelV3.cshtml:576`) and ~14 SRP placard views
(Diamond, PremiumPlus, Silver, Gold, Platinum, Basic, TierTwo, and their `.mobile` variants). The
placard badge was deliberately sized so five grade-only chips fit; labels would overflow it.

So: **add an opt-in label flag, defaulting to off.** Pass it from `PriceGridModelV3.cshtml` only —
via `ViewDataDictionary` on the `RenderPartialAsync` call, or a small wrapper model, whichever reads
better against the surrounding code. Every placard call site stays untouched and keeps rendering
grade-only chips.

Two details:

- The `title` and `aria-label` currently carry `"{Label} grade {Grade}. See why this score was
  given."` Once the label is visible, that text duplicates it for screen-reader users. Keep the
  "See why this score was given" affordance, and drop the now-redundant part when the label renders.
- `Modules/HarmonIQ/Content/harmoniq.less:16-30` (`.harmoniq-chips` / `.harmoniq-chip`) sizes a
  ~22px grade-only pill. The labelled variant needs its own rule; do not widen the base class, or
  you widen the placard badge with it. `Apps/ListingSearch/Client/styles/components/harmoniq-badge.css`
  should not need to change at all — if it does, the flag is leaking.

### Change 4 — move the demo to the two Irvine listings

The seeded grades currently point at Austin listings — Beck at Wells Branch (`n3cqt3m`), 44 South
(`sqvymq4`), 1900 Parmer (`hj7bec1`), Arboretum Oaks (`51bfv4m`). Retarget the two primary demo
listings:

| Role | Listing | Key |
|---|---|---|
| Multi-plan — a grade chip per floor plan | https://www.apartments.com/enzo-irvine-ca/349246f/ | `349246f` |
| Single listing — the Getting Around score card | https://www.apartments.com/108-ambiance-irvine-ca/tk93cec/ | `tk93cec` |

Edit `Modules/HarmonIQ/Content/harmoniq-grades.json`. Subjects stay **per floor plan** — "grade per
unit" in the original request meant "a grade on each row of the floor-plan grid", not a new subject
grain.

**Verify these two things before writing any fixture rows** — if either fails, stop and report
rather than authoring rows that can never render:

1. The local environment actually resolves both Irvine listings. Every current fixture row is an
   Austin listing, which suggests an Austin-seeded local dataset.
2. The real `data-rentalkey` values on Enzo's price grid. `floorPlanId` must equal the rentalkey the
   LDP renders, or no chip appears. Read them off the rendered page or the DB — do not guess. The
   companion HarmonIQ prompt has a session scraping the same listing; those values should agree.

Preserve the edge cases the current fixture deliberately covers, retargeted onto Enzo's plans:

- a plan where Vastu is unscored by the orientation gate, so the chip column shows Feng Shui only;
- a plan storing three traditions, where only the two defaults render but the third still rides in
  `data-harmoniq-sets` so the SRP filter can match on it;
- a subject whose only stored set is unscored — no chip, no card, no badge, but the reasoning page
  still returns 200.

Keep `hj7bec1` (unknown-`principleSet` fallback) and `51bfv4m` (fully unscored) as they are unless
the local dataset no longer resolves them. They are cheap coverage of two paths the Irvine listings
will not exercise.

### Done means

- The filter, the reasoning page headings, and the reasoning page explainer list carry no country or
  culture names; `TraditionDisplay.Culture` no longer exists.
- LDP chips read `Feng Shui B+`; SRP placard badges are byte-identical to before.
- Both Irvine listings render their intended surface, with the scraped rentalkeys recorded in your
  summary.
- Flag off ⇒ nothing renders anywhere. Branch unpushed.

---

## Why this split

`Culture` never crossed the wire — it is absent from `ProjectionRow` and `SetGrade`, and this repo
keeps its own copy in `TraditionDisplay.cs`. So the two repos can retire it independently, in either
order, with no contract change.

The one genuine cross-repo dependency is the **Enzo floor-plan keys**: HarmonIQ's fixture
`rentalKey` and this repo's `floorPlanId` must be the same strings. Whichever session scrapes first
should publish the list.
