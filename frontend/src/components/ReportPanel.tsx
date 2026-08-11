import { AnalyzeResponse, Listing } from '../api';
import { Phase, Refinement } from '../useHarmonIQ';

export interface ReportPanelProps {
  phase: Phase;
  listing: Listing | null;
  report: AnalyzeResponse | null;
  refinement: Refinement | null;
  onRefine: (r: Refinement) => void;
}

export function ReportPanel({ phase, report }: ReportPanelProps) {
  return <div className="hiq-panel">{phase === 'analyzing' ? 'Analyzing…' : report?.analysis.summary}</div>;
}
