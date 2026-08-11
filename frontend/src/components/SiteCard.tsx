import { ListingEnvironment, SideEnvironment, SiteAnalysis } from '../api';
import { FindingColumns, SuggestionCards } from './RoomCard';
import { scoreColor } from './ScoreGauge';

function sideSummary(s: SideEnvironment): string {
  const bits: string[] = [];
  if (s.road !== 'none' && s.road !== 'unknown') bits.push(s.road === 't-junction' ? 'T-junction' : `${s.road} road`);
  if (s.water !== 'none' && s.water !== 'unknown') bits.push(s.water);
  if (s.structures === 'taller-building') bits.push('taller bldg');
  else if (s.structures === 'similar') bits.push('buildings');
  else if (s.structures === 'open') bits.push('open');
  if (s.slope === 'rises' || s.slope === 'falls') bits.push(`ground ${s.slope}`);
  return bits.length ? bits.join(' · ') : '?';
}

function Compass({ environment }: { environment: ListingEnvironment }) {
  const sides = [
    { label: 'N', x: 110, y: 24, text: sideSummary(environment.north), tx: 110, ty: 44 },
    { label: 'E', x: 202, y: 114, text: sideSummary(environment.east), tx: 168, ty: 114 },
    { label: 'S', x: 110, y: 206, text: sideSummary(environment.south), tx: 110, ty: 186 },
    { label: 'W', x: 18, y: 114, text: sideSummary(environment.west), tx: 52, ty: 114 },
  ];
  return (
    <svg viewBox="0 0 220 220" width="220" height="220" className="hiq-compass"
      role="img" aria-label="What surrounds the building on each side">
      <rect x="60" y="60" width="100" height="100" rx="8"
        fill="var(--hiq-surface-2)" stroke="var(--hiq-border)" strokeWidth="2" />
      <text x="110" y="115" textAnchor="middle" fontSize="11" fill="var(--hiq-muted)">BUILDING</text>
      {sides.map(s => (
        <g key={s.label}>
          <text x={s.x} y={s.y} textAnchor="middle" fontSize="14" fontWeight="700"
            fill="var(--hiq-primary)">{s.label}</text>
          <text x={s.tx} y={s.ty} textAnchor="middle" fontSize="8.5" fill="var(--hiq-text)">
            {s.text.length > 26 ? s.text.slice(0, 25) + '…' : s.text}
          </text>
        </g>
      ))}
    </svg>
  );
}

export function SiteCard({ site, environment }: { site: SiteAnalysis; environment: ListingEnvironment }) {
  return (
    <div className="hiq-card">
      <div className="hiq-card-head">
        <h4 className="hiq-card-title">Site &amp; Surroundings</h4>
        <span className="hiq-chip" style={{ background: scoreColor(site.score) }}>{site.score}</span>
      </div>
      <div className="hiq-row" style={{ alignItems: 'flex-start' }}>
        <Compass environment={environment} />
        <div style={{ flex: 1, minWidth: 260 }}>
          <FindingColumns adhering={site.adhering} violations={site.violations} />
        </div>
      </div>
      <SuggestionCards suggestions={site.suggestions} />
    </div>
  );
}
