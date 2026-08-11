import { useState } from 'react';
import { AnalyzeResponse, Listing } from '../api';
import { apiUrl } from '../base';
import { Phase, Refinement } from '../useHarmonIQ';
import { ScoreGauge } from './ScoreGauge';
import { ElementBars } from './ElementBars';
import { ModePill } from './ModePill';
import { RoomCard } from './RoomCard';
import { SiteCard } from './SiteCard';
import { NumbersCard } from './NumbersCard';

export interface ReportPanelProps {
  phase: Phase;
  listing: Listing | null;
  report: AnalyzeResponse | null;
  refinement: Refinement | null;
  onRefine: (r: Refinement) => void;
}

export function ReportPanel({ phase, listing, report, refinement, onRefine: _onRefine }: ReportPanelProps) {
  const [drawerOpen, setDrawerOpen] = useState(false);
  if (!report) {
    return (
      <div className="hiq-panel">
        <span className="hiq-spinner" /> Reading this home's energy — photos, surroundings, and numbers…
      </div>
    );
  }
  const a = report.analysis;
  const thumb = (photoId: string) => {
    const rel = listing?.photos.find(p => p.photoId === photoId)?.thumbnailUrl;
    return rel ? apiUrl(rel) : undefined; // thumbnails are API-relative; resolve cross-origin
  };
  return (
    <div className="hiq-panel" style={phase === 'analyzing' ? { opacity: 0.5, pointerEvents: 'none' } : undefined}>
      <div className="hiq-row" style={{ justifyContent: 'space-between' }}>
        <ModePill mode={report.mode} modelId={report.modelId} />
        <button className="hiq-btn hiq-btn--ghost" type="button"
          onClick={() => setDrawerOpen(o => !o)}>
          {drawerOpen ? 'Close refine' : 'Refine'}
        </button>
      </div>
      {report.mode === 'demo' && report.notice && (
        <div className="hiq-banner">{report.notice}</div>
      )}
      <div className="hiq-panel-head">
        <ScoreGauge score={a.overallScore} grade={a.grade} />
        <div style={{ flex: 2, minWidth: 260 }}>
          <h3 className="hiq-panel-title">HarmonIQ Report</h3>
          <div className="hiq-summary">{a.summary}</div>
        </div>
        <ElementBars balance={a.elementBalance} />
      </div>
      {/* RefineDrawer: Task 16 (render when drawerOpen) */}
      {a.rooms.map(room => (
        <RoomCard key={room.photoId} room={room} thumbnailUrl={thumb(room.photoId)} />
      ))}
      <SiteCard site={a.site}
        environment={refinement?.environment ?? listing!.environment} />
      <NumbersCard numerology={a.numerology} />
    </div>
  );
}
