export function scoreColor(score: number): string {
  return score >= 75 ? 'var(--hiq-good)' : score >= 55 ? 'var(--hiq-warn)' : 'var(--hiq-bad)';
}

export function ScoreGauge({ score, grade }: { score: number; grade: string }) {
  const r = 52;
  const c = 2 * Math.PI * r;
  return (
    <svg viewBox="0 0 120 120" width="130" height="130" role="img"
      aria-label={`HarmonIQ grade ${grade}, ${score} out of 100`}>
      <circle cx="60" cy="60" r={r} fill="none" stroke="var(--hiq-surface-2)" strokeWidth="10" />
      <circle cx="60" cy="60" r={r} fill="none" stroke={scoreColor(score)} strokeWidth="10"
        strokeLinecap="round" strokeDasharray={`${(c * score) / 100} ${c}`}
        transform="rotate(-90 60 60)"
        style={{ transition: 'stroke-dasharray 1s ease, stroke .4s' }} />
      <text x="60" y="58" textAnchor="middle" fontSize="30" fontWeight="800"
        fill={scoreColor(score)} className="hiq-gauge-num">{grade}</text>
      <text x="60" y="80" textAnchor="middle" fontSize="13" fill="var(--hiq-muted)">{score}/100</text>
    </svg>
  );
}
