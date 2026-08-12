# Orientation data sources — what actually exists

**Date:** 2026-08-11 · **Status:** blocked on one question to Engrain · **Supersedes** the orientation
assumptions in [ARCHITECTURE.md §8](ARCHITECTURE.md) and §17 risk 2.

Two of five traditions — Vastu and Kasō — are orientation-gated, so a facing is the difference
between a stored, filterable grade and an explanatory absence. This note records what the available
data sources actually provide, verified against published specs rather than assumed.

---

## 1. The correction

`ARCHITECTURE.md §8` states that SightMap is the only orientation source, and that whether its unit
polygons are exposed as true-north geo-referenced vectors is *unverified*. It is now verified, and
the answer is no.

**SightMap's REST API carries no unit geometry at all.** From its published OpenAPI spec, a unit is:

```
id, asset_id, building_id, floor_id, floor_plan_id, provider_group_id, map_id,
unit_number, label, area, is_affordable_housing_unit,
address_line1/2, address_city, address_state, address_country, address_postal_code,
view_image_url, secondary_view_image_url, created_at, updated_at
```

No polygon, no coordinate, no bearing. Assets carry `address_latitude`/`address_longitude` — a
single point for the whole property, which is the geocode §8 already refuses to infer orientation
from.

So the per-unit exterior-wall bearing that `SightMapClient` was written to consume **does not exist
on that API**, and `SightMapOrientationProvider` could not work even with a key.

## 2. Two products, not one

| | SightMap | Unit Map |
|---|---|---|
| API | `api.sightmap.com` | `api.unitmap.com` |
| Docs | `developers.sightmap.com` | `developers.unitmap.com` |
| Unit geometry | none | **polygons** — `shape.points`, e.g. `"1482.5,1144.65 …"` |
| Coordinate space | n/a | **map space (pixels)**, not geographic |

Unit Map's own docs gate some features on "a georeferenced unit map" and refer to "the map's
georeference", so the transform exists internally — but **no documented endpoint exposes it**. No
projection, no CRS, no rotation, no origin.

## 3. What apartments-web already has

Relevant because it changes the size of the ask. apartments-web runs a live Engrain integration:

- **Server side** — `PropertyMapHelper` → `MarketplaceService` (cached) → `MarketplaceProxy` →
  `{MarketplaceRoot}/vendor/engrain/get/mapped-unit`, keyed by `ListingKey`. Returns
  `EngrainMappedUnit`: `ListingKey, UnitKey, UnitNumber, MapId, AssetId, BuildingId, FloorId,
  FloorPlanId, FloorName`. **IDs only — no geometry.**
- **Client side** — the Unit Map SDK, `cdn.unitmap.com/sdk/js/next/unitmap-build-74`. Its documented
  surface is indoor wayfinding (Trip, Nav, Maneuver, Locations), all in pixel coordinates. No
  compass, bearing, or georeference accessor.
- Gated on ad level (Diamond, DiamondPlus, Platinum, Elite in the US), paid profile, multifamily,
  and `PropertyMapConfig.IsActive`.

Two consequences:

1. **The identity problem is already solved.** `ListingKey` (the apartments.com key, e.g. `349246f`)
   → Engrain `AssetId`/`MapId`/`BuildingId`/`FloorId` is in production. HarmonIQ does not need to
   build that seam, and does not need its own partner key — it needs the Marketplace endpoint.
2. **Coverage would track ad spend.** If orientation ever flows this way, Vastu and Kasō become
   scoreable only on premium paid listings. That is a product decision to take deliberately, not a
   side effect to discover later — see §17 risk 7 on the cultural-filter review.

## 4. The deciding unknown: `geojson_url`

The Unit Map OpenAPI spec has a field `geojson_url` on the **Unit Maps** resource (`listUnitMaps`
and `getUnitMap`). String, nullable, `null` in the public example, and **entirely undocumented** —
no description of contents or coordinate system.

Engrain's data offering is described by a third-party listing as including "Building, Floor, Floor
Plan and Latitude/Longitude data" per unit, and "Property and Unit Geolocation and GeoJSON".

GeoJSON is WGS84 by definition (RFC 7946). **If that field is populated with unit polygons, the
whole problem dissolves**: no alignment, no ambiguity, no OSM dependency. Read the polygons, compute
each unit's exterior-wall normal, fill `UnitPlacement.FacingDegrees`, and the existing
`OrientationResolution` runs untouched.

**The one question to Engrain:**

> For our licensed assets, is `geojson_url` populated on the unit maps resource, and does it contain
> unit polygons in WGS84?

Checkable in one call by anyone with a Unit Map key:

```
curl -s -H "API-Key: $UNITMAP_KEY" "https://api.unitmap.com/v1/maps?asset_id=$ENGRAIN_ASSET_ID"
```

Non-null `geojson_url` → fetch it and confirm coordinates look like `[-117.85, 33.67]` (lat/lon)
rather than `[1482.5, 1144.65]` (pixels).

## 5. Fallback if `geojson_url` is null

If Unit Map yields only map-space polygons, true north can be recovered by aligning the map-space
floor outline to a real building footprint (OSM via Overpass — `GeoContextService` already queries
that host; `out geom` instead of `out center` returns full polygons).

This was prototyped and measured. **It works, conditionally.**

| Condition | Correct | Wrong |
|---|---|---|
| identical building sets | 100% | 0% |
| identical sets + 2m digitisation noise | 98% | 2% |
| map space missing one building | 67% | **33%** |
| footprint missing one building | 57% | **43%** |

Noise and scale error are harmless. **Building-set disagreement is the hazard** — and it is the
expected case, since OSM carries garages, clubhouses and leasing offices that Engrain's unit maps do
not. The failure mode is a 180° flip, which inverts the entire Vastu directional scheme.

Gating on the algorithm-visible margin (winner vs nearest rival) over 240 trials:

| Gate | Coverage | Error rate |
|---|---|---|
| none | 100% | 19.6% |
| ≥1.5 | 64% | 2.6% |
| **≥2.0** | **43%** | **0% (0/102)** |

Zero errors in 102 samples bounds the true rate near 3% by the rule of three, not at zero. Given the
error is *backwards*, treat ~1–3% residual as the honest number.

Two mitigations worth taking before trusting it: filter both sets to residential buildings
(`building=apartments|residential` on the OSM side, buildings containing mapped units on the Engrain
side) to drive conditions C/D toward A; and note that the trials used randomly placed buildings,
which manufactures asymmetry. Real complexes are often deliberately symmetric — a four-building quad
scored 0.94x and failed outright — so **real coverage may be well below 43%.**

A rejected match degrades to `Source = "none"`, an existing state. No new failure mode is introduced.

## 6. What is ruled out

- **Inferring from the floor-plan image or the geocode** — §8 forbids it, correctly. A wrong facing
  yields a confidently wrong grade; the gate explanation is the better answer.
- **A property-level building axis alone** — the footprint is already in true-north coordinates, so
  there is no rotation to solve without map-space geometry to align. It also yields one value for
  all twenty plans, which is not what a plan's facing means in this model.
- **Scraping the Unit Map SDK bundle** — fragile against a vendor build, and the wrong footing for a
  licensed data relationship.

## 7. Status of each claim

| Claim | Basis |
|---|---|
| SightMap has no unit geometry | Published OpenAPI spec |
| Unit Map has polygons, in pixel space | Published OpenAPI spec + docs |
| Georeference transform not exposed | Absence across both specs and both doc hubs |
| `geojson_url` exists, undocumented, null in sample | Published OpenAPI spec |
| Engrain sells per-unit lat/lon and GeoJSON | Third-party vendor listing — **not confirmed with Engrain** |
| apartments-web integration shape | Read from source |
| Alignment feasibility numbers | Simulation on synthetic sites — **never run against Enzo** |

The fixture in `Data/sample-orientation.json` remains invented and is labelled as such in
`FixtureOrientationProvider`. Nothing here changes that; it exercises all three resolution shapes and
is the honest local stand-in until the question above is answered.
