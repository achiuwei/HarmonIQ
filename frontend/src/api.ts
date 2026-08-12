import { apiUrl } from './base';

/**
 * A tradition's wire id. Kept as a widened string rather than a closed union so the UI is driven
 * by the ids the API actually returns — the day the backend adds a sixth tradition, nothing here
 * needs to change.
 */
export type PrincipleSet = 'fengshui' | 'vastu' | 'pungsu' | 'kaso' | 'phongthuy' | (string & {});

/**
 * The renter's tradition selection. `'all'` replaces the old `'both'`, which was a two-tradition
 * encoding — there is still no blended score, only a union of the stored per-set rows.
 */
export type Systems = 'all' | PrincipleSet;

/** Display metadata for a tradition id. Unknown ids fall back to the title-cased id, sorted last. */
export const TRADITIONS: ReadonlyArray<{ id: PrincipleSet; label: string }> = [
  { id: 'fengshui', label: 'Feng Shui' },
  { id: 'vastu', label: 'Vastu Shastra' },
  { id: 'pungsu', label: 'Pungsu-jiri' },
  { id: 'kaso', label: 'Kasō' },
  { id: 'phongthuy', label: 'Phong Thủy' },
];

/** With no renter preference the surfaces show these two — today's visual weight, unchanged. */
export const DEFAULT_SETS: readonly PrincipleSet[] = ['fengshui', 'vastu'];

export const traditionLabel = (id: string): string =>
  TRADITIONS.find(t => t.id === id)?.label
  ?? id.charAt(0).toUpperCase() + id.slice(1);
export type Severity = 'minor' | 'moderate' | 'major';
export type Level = 'low' | 'medium' | 'high';

export interface ListingPhoto {
  photoId: string; thumbnailUrl: string; caption: string | null;
  interior: boolean; selected: boolean; suggestedRoomType: string | null;
}
export interface SideEnvironment { road: string; water: string; structures: string; slope: string; }
export interface ListingEnvironment {
  north: SideEnvironment; east: SideEnvironment; south: SideEnvironment; west: SideEnvironment;
}
export interface ListingNumbers { unitNumber: string | null; floor: number | null; streetNumber: string | null; }
export interface Listing {
  listingId: string; title: string; address: string; url: string;
  photos: ListingPhoto[]; numbers: ListingNumbers; environment: ListingEnvironment;
}

export interface PhotoSelection { photoId: string; roomType: string | null; }
export interface AnalyzeRequest {
  listingId: string; photos: PhotoSelection[]; systems: Systems; orientation: string;
  environment: ListingEnvironment | null; numbers: ListingNumbers | null; brand: string | null;
}

export interface Finding { principle: string; observation: string; system: string; }
export interface ViolationFinding extends Finding { severity: Severity; }
export interface Suggestion { title: string; detail: string; effort: Level; impact: Level; }
export interface ElementBalance { wood: number; fire: number; earth: number; metal: number; water: number; }
export interface RoomAnalysis {
  photoId: string; roomType: string; score: number; elementBalance: ElementBalance;
  adhering: Finding[]; violations: ViolationFinding[]; suggestions: Suggestion[];
}
export interface SiteAnalysis {
  score: number; adhering: Finding[]; violations: ViolationFinding[]; suggestions: Suggestion[];
}
export interface NumerologyCheck {
  subject: string; value: string; verdict: 'lucky' | 'neutral' | 'unlucky';
  tradition: string; reason: string; remedy: string | null;
}
export interface NumerologyResult { scoreAdjustment: number; checks: NumerologyCheck[]; }
export interface AnalysisResult {
  overallScore: number; grade: string; summary: string; elementBalance: ElementBalance;
  rooms: RoomAnalysis[]; site: SiteAnalysis; numerology: NumerologyResult;
}
export interface AnalyzeResponse {
  mode: 'live' | 'demo'; modelId?: string; notice?: string;
  listing: { listingId: string; title: string; address: string; url: string };
  analysis: AnalysisResult;
}

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message); }
}

async function handle<T>(resp: Response): Promise<T> {
  if (!resp.ok) {
    let message = `Request failed (${resp.status})`;
    try { message = (await resp.json()).error ?? message; } catch { /* keep default */ }
    throw new ApiError(resp.status, message);
  }
  return resp.json() as Promise<T>;
}

export function fetchListing(listingId: string, brand: string): Promise<Listing> {
  return fetch(apiUrl(`/api/listing/${encodeURIComponent(listingId)}?brand=${encodeURIComponent(brand)}`))
    .then(r => handle<Listing>(r));
}

export function postAnalyze(req: AnalyzeRequest): Promise<AnalyzeResponse> {
  return fetch(apiUrl('/api/analyze'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(req),
  }).then(r => handle<AnalyzeResponse>(r));
}
