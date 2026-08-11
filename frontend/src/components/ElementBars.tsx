import { ElementBalance } from '../api';

const ELEMENT_COLORS: Record<keyof ElementBalance, string> = {
  wood: '#4a7c2f', fire: '#c0392b', earth: '#b98a2f', metal: '#8e9aa5', water: '#2e6b8a',
};

export function ElementBars({ balance }: { balance: ElementBalance }) {
  return (
    <div className="hiq-elements">
      {(Object.keys(ELEMENT_COLORS) as (keyof ElementBalance)[]).map(el => (
        <div className="hiq-el-row" key={el}>
          <span className="hiq-el-label">{el}</span>
          <div className="hiq-el-track">
            <div className="hiq-el-fill"
              style={{ width: `${balance[el]}%`, background: ELEMENT_COLORS[el] }} />
          </div>
          <span className="hiq-el-val">{balance[el]}</span>
        </div>
      ))}
    </div>
  );
}
