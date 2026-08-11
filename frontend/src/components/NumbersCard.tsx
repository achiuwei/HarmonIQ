import { NumerologyCheck, NumerologyResult } from '../api';

const SUBJECT_LABELS: Record<string, string> = {
  unitNumber: 'Unit', floor: 'Floor', streetNumber: 'Street number',
};
const VERDICT_COLOR: Record<NumerologyCheck['verdict'], string> = {
  lucky: 'var(--hiq-good)', neutral: 'var(--hiq-muted)', unlucky: 'var(--hiq-bad)',
};

export function NumbersCard({ numerology }: { numerology: NumerologyResult }) {
  if (numerology.checks.length === 0) return null;
  return (
    <div className="hiq-card">
      <div className="hiq-card-head">
        <h4 className="hiq-card-title">Numbers</h4>
        <span className="hiq-pill">
          score {numerology.scoreAdjustment >= 0 ? '+' : ''}{numerology.scoreAdjustment}
        </span>
      </div>
      {numerology.checks.map((c, i) => (
        <div className="hiq-finding" key={i}>
          <b>
            {SUBJECT_LABELS[c.subject] ?? c.subject} {c.value}
            <span className="hiq-tag" style={{
              background: 'transparent',
              border: `1px solid ${VERDICT_COLOR[c.verdict]}`,
              color: VERDICT_COLOR[c.verdict],
            }}>{c.verdict}</span>
            <span className="hiq-tag hiq-tag--sys">{c.tradition}</span>
          </b>
          {c.reason}
          {c.remedy && <div style={{ color: 'var(--hiq-muted)', marginTop: 2 }}>Remedy: {c.remedy}</div>}
        </div>
      ))}
      <div className="hiq-banner" style={{ marginBottom: 0 }}>
        Number readings are cultural tradition, offered as guidance — not statements of fact about this home.
      </div>
    </div>
  );
}
