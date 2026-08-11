import { useCallback, useRef, useState } from 'react';
import {
  AnalyzeResponse, Listing, ListingEnvironment, ListingNumbers,
  PhotoSelection, Systems, fetchListing, postAnalyze,
} from './api';

export type Phase = 'idle' | 'fetching-listing' | 'analyzing' | 'report' | 'error';

export interface Refinement {
  photos: PhotoSelection[];
  systems: Systems;
  orientation: string;
  environment: ListingEnvironment;
  numbers: ListingNumbers;
}

export function defaultRefinement(listing: Listing): Refinement {
  return {
    photos: listing.photos.filter(p => p.selected)
      .map(p => ({ photoId: p.photoId, roomType: p.suggestedRoomType })),
    systems: 'all',
    orientation: 'unknown',
    environment: listing.environment,
    numbers: listing.numbers,
  };
}

export function useHarmonIQ(listingId: string, brand: string) {
  const [phase, setPhase] = useState<Phase>('idle');
  const [listing, setListing] = useState<Listing | null>(null);
  const [report, setReport] = useState<AnalyzeResponse | null>(null);
  const [refinement, setRefinement] = useState<Refinement | null>(null);
  const [error, setError] = useState<string | null>(null);
  const started = useRef(false);

  const runAnalyze = useCallback(async (id: string, r: Refinement) => {
    setPhase('analyzing');
    try {
      const resp = await postAnalyze({
        listingId: id, photos: r.photos, systems: r.systems, orientation: r.orientation,
        environment: r.environment, numbers: r.numbers, brand,
      });
      setReport(resp);
      setPhase('report');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Analysis failed');
      setPhase('error');
    }
  }, [brand]);

  const start = useCallback(async () => {
    if (started.current || !listingId) return;
    started.current = true;
    setPhase('fetching-listing');
    try {
      const l = await fetchListing(listingId, brand);
      setListing(l);
      const r = defaultRefinement(l);
      setRefinement(r);
      await runAnalyze(l.listingId, r);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Listing unavailable');
      setPhase('error');
    }
  }, [listingId, brand, runAnalyze]);

  const refine = useCallback((r: Refinement) => {
    if (!listing) return;
    setRefinement(r);
    void runAnalyze(listing.listingId, r);
  }, [listing, runAnalyze]);

  return { phase, listing, report, refinement, error, start, refine };
}
