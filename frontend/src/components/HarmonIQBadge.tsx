import { AnalyzeResponse } from '../api';
import { apiUrl } from '../base';
import { Phase } from '../useHarmonIQ';

export interface BadgeProps {
  phase: Phase;
  report: AnalyzeResponse | null;
  error: string | null;
  expanded: boolean;
  onToggle: () => void;
}

function Attribution() {
  return (
    <div className="hiq-attribution">
      Data provided by <a href={apiUrl('/harmoniq')} target="_blank" rel="noopener">HarmonIQ</a>
    </div>
  );
}

export function HarmonIQBadge({ phase, report, error, expanded, onToggle }: BadgeProps) {
  if (phase === 'error') {
    return (
      <div className="hiq-badge" role="status">
        <span className="hiq-badge-logo">HarmonIQ Score</span>
        <span className="hiq-badge-error">Score unavailable</span>
      </div>
    );
  }
  const loading = phase === 'idle' || phase === 'fetching-listing' ||
    (phase === 'analyzing' && !report);
  const a = report?.analysis;
  const color = !a ? 'var(--hiq-muted)'
    : a.overallScore >= 75 ? 'var(--hiq-good)'
    : a.overallScore >= 55 ? 'var(--hiq-warn)' : 'var(--hiq-bad)';
  return (
    <>
      <button className="hiq-badge" onClick={onToggle}
        aria-expanded={expanded} title="HarmonIQ harmony score" type="button"
        style={{ font: 'inherit' }}>
        <span className="hiq-badge-info">
          <span className="hiq-badge-logo">HarmonIQ Score</span>
          <span className="hiq-badge-tagline">Feng Shui &amp; Vastu harmony</span>
        </span>
        {loading ? (
          <span className="hiq-badge-value">
            <span className="hiq-spinner" aria-hidden="true" />
            <span className="hiq-badge-score">reading the energy…</span>
          </span>
        ) : (
          <span className="hiq-badge-value">
            <span className="hiq-badge-grade" style={{ color }}>{a!.grade}</span>
            <span className="hiq-badge-score">{a!.overallScore} / 100</span>
          </span>
        )}
      </button>
      <Attribution />
    </>
  );
}
