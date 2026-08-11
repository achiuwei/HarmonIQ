export function ModePill({ mode, modelId }: { mode: 'live' | 'demo'; modelId?: string }) {
  return (
    <span className="hiq-pill">
      <span className={`hiq-pill-dot${mode === 'demo' ? ' hiq-pill-dot--demo' : ''}`} />
      {mode === 'live' ? `Live · ${modelId ?? 'claude'}` : 'Demo mode'}
    </span>
  );
}
