import { useEffect, useRef, useState } from 'react';
import { HarmonIQBadge } from './components/HarmonIQBadge';
import { ReportPanel } from './components/ReportPanel';
import { useHarmonIQ } from './useHarmonIQ';

export interface ModuleProps {
  listingId: string;
  brand: string;
  initialState: 'badge' | 'expanded';
}

export function Module({ listingId, brand, initialState }: ModuleProps) {
  const { phase, listing, report, refinement, error, start, refine } = useHarmonIQ(listingId, brand);
  const [expanded, setExpanded] = useState(initialState === 'expanded');
  const rootRef = useRef<HTMLDivElement>(null);

  // FR-3: analysis starts automatically when the badge first becomes visible.
  useEffect(() => {
    const el = rootRef.current;
    if (!el) return;
    const io = new IntersectionObserver((entries) => {
      if (entries.some(e => e.isIntersecting)) { void start(); io.disconnect(); }
    }, { threshold: 0.1 });
    io.observe(el);
    return () => io.disconnect();
  }, [start]);

  return (
    <div className="hiq-root" ref={rootRef}>
      <HarmonIQBadge phase={phase} report={report} error={error}
        expanded={expanded} onToggle={() => setExpanded(e => !e)} />
      {expanded && phase !== 'error' && (
        <ReportPanel phase={phase} listing={listing} report={report}
          refinement={refinement} onRefine={refine} />
      )}
    </div>
  );
}
