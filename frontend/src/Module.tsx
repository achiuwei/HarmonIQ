export interface ModuleProps {
  listingId: string;
  brand: string;
  initialState: 'badge' | 'expanded';
}

export function Module({ listingId }: ModuleProps) {
  return <div className="hiq-root"><div className="hiq-badge"><span className="hiq-badge-logo">HarmonIQ</span><span className="hiq-badge-score">{listingId}</span></div></div>;
}
