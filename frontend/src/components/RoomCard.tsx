import { Finding, RoomAnalysis, Suggestion, ViolationFinding } from '../api';
import { scoreColor } from './ScoreGauge';

export function FindingColumns({ adhering, violations }: {
  adhering: Finding[]; violations: ViolationFinding[];
}) {
  return (
    <div className="hiq-cols">
      <div>
        <div className="hiq-col-title hiq-col-title--good">Working in your favor</div>
        {adhering.length === 0 && <div className="hiq-finding">Nothing notable detected.</div>}
        {adhering.map((f, i) => (
          <div className="hiq-finding" key={i}>
            <b>{f.principle}<span className="hiq-tag hiq-tag--sys">{f.system}</span></b>
            {f.observation}
          </div>
        ))}
      </div>
      <div>
        <div className="hiq-col-title hiq-col-title--bad">Breaking the principles</div>
        {violations.length === 0 && <div className="hiq-finding">No violations detected.</div>}
        {violations.map((f, i) => (
          <div className="hiq-finding" key={i}>
            <b>{f.principle}
              <span className={`hiq-tag hiq-tag--${f.severity}`}>{f.severity}</span>
              <span className="hiq-tag hiq-tag--sys">{f.system}</span>
            </b>
            {f.observation}
          </div>
        ))}
      </div>
    </div>
  );
}

export function SuggestionCards({ suggestions }: { suggestions: Suggestion[] }) {
  if (suggestions.length === 0) return null;
  return (
    <div className="hiq-sugs">
      {suggestions.map((s, i) => (
        <div className="hiq-sug" key={i}>
          <b>{s.title}</b>
          {s.detail}
          <div className="hiq-sug-tags">
            <span>impact: {s.impact}</span><span>effort: {s.effort}</span>
          </div>
        </div>
      ))}
    </div>
  );
}

export function RoomCard({ room, thumbnailUrl }: { room: RoomAnalysis; thumbnailUrl?: string }) {
  return (
    <div className="hiq-card">
      <div className="hiq-card-head">
        {thumbnailUrl && <img className="hiq-thumb" src={thumbnailUrl} alt={room.roomType} />}
        <h4 className="hiq-card-title">{room.roomType}</h4>
        <span className="hiq-chip" style={{ background: scoreColor(room.score) }}>{room.score}</span>
      </div>
      <FindingColumns adhering={room.adhering} violations={room.violations} />
      <SuggestionCards suggestions={room.suggestions} />
    </div>
  );
}
