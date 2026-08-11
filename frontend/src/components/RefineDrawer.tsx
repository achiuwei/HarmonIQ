import { useState } from 'react';
import {
  Listing, SideEnvironment, Systems, TRADITIONS, traditionLabel,
} from '../api';
import { apiUrl } from '../base';
import { Refinement } from '../useHarmonIQ';

const ROOM_TYPES = ['Auto-detect', 'Bedroom', 'Living Room', 'Kitchen', 'Bathroom',
  'Dining Room', 'Home Office', 'Entryway', 'Balcony'];
const ORIENTATIONS = ['unknown', 'north', 'northeast', 'east', 'southeast',
  'south', 'southwest', 'west', 'northwest'];
const SIDES = ['north', 'east', 'south', 'west'] as const;
const ENV_FIELDS: { key: keyof SideEnvironment; label: string; options: string[] }[] = [
  { key: 'road', label: 'Road', options: ['unknown', 'none', 'quiet', 'busy', 't-junction', 'highway'] },
  { key: 'water', label: 'Water', options: ['unknown', 'none', 'pond', 'lake', 'river', 'pool'] },
  { key: 'structures', label: 'Structures', options: ['unknown', 'open', 'similar', 'taller-building'] },
  { key: 'slope', label: 'Slope', options: ['unknown', 'level', 'rises', 'falls'] },
];

export interface RefineDrawerProps {
  listing: Listing;
  refinement: Refinement;
  onApply: (r: Refinement) => void;
  onClose: () => void;
}

export function RefineDrawer({ listing, refinement, onApply, onClose }: RefineDrawerProps) {
  const [draft, setDraft] = useState<Refinement>(() => ({
    ...refinement,
    photos: refinement.photos.map(p => ({ ...p })),
    environment: structuredClone(refinement.environment),
    numbers: { ...refinement.numbers },
  }));

  const selectedIds = new Set(draft.photos.map(p => p.photoId));
  const togglePhoto = (photoId: string, suggested: string | null) =>
    setDraft(d => ({
      ...d,
      photos: selectedIds.has(photoId)
        ? d.photos.filter(p => p.photoId !== photoId)
        : [...d.photos, { photoId, roomType: suggested }],
    }));
  const setRoomType = (photoId: string, roomType: string) =>
    setDraft(d => ({
      ...d,
      photos: d.photos.map(p => p.photoId === photoId
        ? { ...p, roomType: roomType === 'Auto-detect' ? null : roomType } : p),
    }));
  const setEnv = (side: typeof SIDES[number], key: keyof SideEnvironment, value: string) =>
    setDraft(d => ({
      ...d,
      environment: { ...d.environment, [side]: { ...d.environment[side], [key]: value } },
    }));

  const count = draft.photos.length;
  const valid = count >= 1 && count <= 6;

  return (
    <div className="hiq-drawer">
      <h4>Photos to analyze ({count}/6)</h4>
      <div className="hiq-photo-grid">
        {listing.photos.map(p => {
          const sel = selectedIds.has(p.photoId);
          const chosen = draft.photos.find(x => x.photoId === p.photoId);
          return (
            <div className="hiq-photo-cell" key={p.photoId} style={sel ? { borderColor: 'var(--hiq-primary)' } : undefined}>
              <img src={apiUrl(p.thumbnailUrl)} alt={p.caption ?? p.photoId} />
              <label style={{ display: 'flex', gap: 4, alignItems: 'center', margin: '4px 0' }}>
                <input type="checkbox" checked={sel}
                  disabled={!sel && count >= 6}
                  onChange={() => togglePhoto(p.photoId, p.suggestedRoomType)} />
                {p.caption ?? (p.interior ? 'Interior' : 'Other')}
              </label>
              {sel && (
                <select value={chosen?.roomType ?? 'Auto-detect'}
                  onChange={e => setRoomType(p.photoId, e.target.value)}>
                  {ROOM_TYPES.map(t => <option key={t}>{t}</option>)}
                </select>
              )}
            </div>
          );
        })}
      </div>

      <h4>Surroundings (what's on each side)</h4>
      <div className="hiq-env-grid">
        {SIDES.map(side => (
          <div className="hiq-env-side" key={side}>
            <b style={{ textTransform: 'capitalize' }}>{side}</b>
            {ENV_FIELDS.map(f => (
              <label key={f.key}>{f.label}
                <select value={draft.environment[side][f.key]}
                  onChange={e => setEnv(side, f.key, e.target.value)}>
                  {f.options.map(o => <option key={o}>{o}</option>)}
                </select>
              </label>
            ))}
          </div>
        ))}
      </div>

      <h4>Numbers</h4>
      <div className="hiq-row">
        <label>Unit <input value={draft.numbers.unitNumber ?? ''} size={6}
          onChange={e => setDraft(d => ({ ...d, numbers: { ...d.numbers, unitNumber: e.target.value || null } }))} /></label>
        <label>Floor <input value={draft.numbers.floor ?? ''} size={4} inputMode="numeric"
          onChange={e => setDraft(d => ({
            ...d,
            numbers: { ...d.numbers, floor: e.target.value === '' ? null : Number(e.target.value) || null },
          }))} /></label>
        <label>Street # <input value={draft.numbers.streetNumber ?? ''} size={6}
          onChange={e => setDraft(d => ({ ...d, numbers: { ...d.numbers, streetNumber: e.target.value || null } }))} /></label>
      </div>

      <h4>Entrance orientation &amp; tradition</h4>
      <div className="hiq-row">
        <select value={draft.orientation}
          onChange={e => setDraft(d => ({ ...d, orientation: e.target.value }))}>
          {ORIENTATIONS.map(o => <option key={o}>{o}</option>)}
        </select>
        {/* Driven by TRADITIONS rather than a literal list, so a sixth tradition needs no edit here. */}
        <span className="hiq-seg">
          {(['all', ...TRADITIONS.map(t => t.id)] as Systems[]).map(s => (
            <button key={s} type="button" className={draft.systems === s ? 'on' : ''}
              onClick={() => setDraft(d => ({ ...d, systems: s }))}
              title={s === 'all' ? 'Every tradition' : TRADITIONS.find(t => t.id === s)?.culture}>
              {s === 'all' ? 'All' : traditionLabel(s)}
            </button>
          ))}
        </span>
      </div>

      <div className="hiq-row" style={{ marginTop: 14 }}>
        <button className="hiq-btn" type="button" disabled={!valid}
          onClick={() => { onApply(draft); onClose(); }}>
          Re-grade with these settings
        </button>
        {!valid && <span style={{ fontSize: 12, color: 'var(--hiq-bad)' }}>Select 1–6 photos.</span>}
        <button className="hiq-btn hiq-btn--ghost" type="button" onClick={onClose}>Cancel</button>
      </div>
    </div>
  );
}
