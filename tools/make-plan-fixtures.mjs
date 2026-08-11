// Generates architectural line-drawing floor-plan schematics for the multi-plan
// sample fixture (Task 5, HarmonIQ v2). Same pattern as make-fixture-photos.mjs:
// hand-written SVG rasterized with sharp, run once, output committed.
//
// IMPORTANT: unescaped `&` inside SVG text is invalid XML and makes sharp throw.
// Always use `&amp;` for a literal ampersand in any label below.
import sharp from 'sharp';
import { mkdirSync } from 'fs';

const OUT = new URL('../backend/HarmonIQ.Api/Data/sample-plans/', import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const W = 1200, H = 900;

const txt = (x, y, t, size = 22, opts = {}) => {
  const { anchor = 'middle', weight = 'normal', fill = '#1a1a1a' } = opts;
  return `<text x="${x}" y="${y}" font-family="Helvetica, Arial" font-size="${size}" font-weight="${weight}" fill="${fill}" text-anchor="${anchor}">${t}</text>`;
};

// wall: rectangle outline. doorway: a gap drawn as a white break with a swing arc.
const wallRect = (x, y, w, h, stroke = 3) =>
  `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="none" stroke="#111" stroke-width="${stroke}"/>`;

const doorSwing = (hingeX, hingeY, r, startAngle) => {
  const rad = (startAngle * Math.PI) / 180;
  const endRad = rad + Math.PI / 2;
  const x1 = hingeX + r * Math.cos(rad);
  const y1 = hingeY + r * Math.sin(rad);
  const x2 = hingeX + r * Math.cos(endRad);
  const y2 = hingeY + r * Math.sin(endRad);
  return `
    <line x1="${hingeX}" y1="${hingeY}" x2="${x1}" y2="${y1}" stroke="#111" stroke-width="2"/>
    <path d="M ${x1} ${y1} A ${r} ${r} 0 0 1 ${x2} ${y2}" fill="none" stroke="#111" stroke-width="1.5" stroke-dasharray="4,4"/>
  `;
};

const frame = (title, body) => `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">
  <rect width="${W}" height="${H}" fill="#ffffff"/>
  <rect x="8" y="8" width="${W - 16}" height="${H - 16}" fill="none" stroke="#ccc" stroke-width="2"/>
  ${body}
  ${txt(W / 2, H - 30, title, 26, { weight: 'bold' })}
</svg>`;

const scenes = {
  // rk-101: 1 Bed 1 Bath — bath adjacent to the kitchen (shared plumbing wall).
  'plan-rk-101': frame('1 BED 1 BATH — UNIT PLAN A (RK-101)', `
    ${wallRect(100, 80, 1000, 680)}
    <line x1="640" y1="80" x2="640" y2="760" stroke="#111" stroke-width="3"/>
    <line x1="100" y1="440" x2="640" y2="440" stroke="#111" stroke-width="3"/>
    <line x1="640" y1="500" x2="1100" y2="500" stroke="#111" stroke-width="3"/>
    ${txt(370, 260, 'BEDROOM', 26)}
    ${txt(370, 620, 'LIVING ROOM', 26)}
    ${txt(870, 300, 'KITCHEN', 26)}
    ${txt(870, 660, 'BATH', 26)}
    ${txt(870, 460, 'SHARED PLUMBING WALL', 16, { fill: '#888' })}
    <rect x="600" y="120" width="80" height="8" fill="#fff"/>
    ${doorSwing(600, 124, 70, 0)}
    <rect x="1060" y="440" width="8" height="90" fill="#fff"/>
    ${doorSwing(1064, 440, 60, 90)}
    ${txt(150, 60, 'ENTRY', 18)}
    <rect x="100" y="130" width="8" height="70" fill="#fff"/>
    ${doorSwing(108, 130, 60, 0)}
  `),
  // rk-102: 1 Bed 1 Bath — different layout, straight entry-to-rear line.
  'plan-rk-102': frame('1 BED 1 BATH — UNIT PLAN B (RK-102)', `
    ${wallRect(100, 80, 1000, 680)}
    <line x1="100" y1="420" x2="1100" y2="420" stroke="#111" stroke-width="3"/>
    <line x1="700" y1="80" x2="700" y2="420" stroke="#111" stroke-width="3"/>
    <line x1="820" y1="420" x2="820" y2="760" stroke="#111" stroke-width="3"/>
    <line x1="100" y1="80" x2="700" y2="760" stroke="#999" stroke-width="2" stroke-dasharray="10,8"/>
    ${txt(360, 700, 'ENTRY → REAR: STRAIGHT SIGHTLINE', 18, { fill: '#888' })}
    ${txt(400, 260, 'LIVING ROOM', 26)}
    ${txt(900, 260, 'BEDROOM', 26)}
    ${txt(400, 620, 'KITCHEN', 26)}
    ${txt(960, 620, 'BATH', 26)}
    <rect x="96" y="380" width="8" height="80" fill="#fff"/>
    ${doorSwing(104, 380, 70, 0)}
    ${txt(150, 360, 'ENTRY', 18)}
    <rect x="1096" y="380" width="8" height="80" fill="#fff"/>
    ${doorSwing(1104, 460, 60, -90)}
  `),
  // rk-103: 2 Bed 2 Bath — toilet positioned on the bed-head wall of the primary bedroom.
  'plan-rk-103': frame('2 BED 2 BATH — UNIT PLAN (RK-103)', `
    ${wallRect(100, 80, 1000, 680)}
    <line x1="100" y1="420" x2="1100" y2="420" stroke="#111" stroke-width="3"/>
    <line x1="600" y1="80" x2="600" y2="420" stroke="#111" stroke-width="3"/>
    <line x1="850" y1="80" x2="850" y2="420" stroke="#111" stroke-width="3"/>
    <line x1="700" y1="420" x2="700" y2="760" stroke="#111" stroke-width="3"/>
    ${txt(350, 260, 'PRIMARY BEDROOM', 24)}
    <rect x="150" y="140" width="260" height="90" fill="none" stroke="#333" stroke-width="2"/>
    ${txt(280, 195, 'BED (HEAD AGAINST WALL BELOW)', 14, { fill: '#555' })}
    ${txt(725, 250, 'BATH 1', 22)}
    ${txt(725, 130, 'TOILET — ON BED-HEAD WALL', 15, { fill: '#c0392b' })}
    <line x1="850" y1="80" x2="850" y2="420" stroke="#c0392b" stroke-width="4" stroke-dasharray="2,4"/>
    ${txt(970, 260, 'BEDROOM 2', 24)}
    ${txt(340, 620, 'LIVING / KITCHEN', 26)}
    ${txt(900, 620, 'BATH 2', 22)}
    <rect x="96" y="600" width="8" height="80" fill="#fff"/>
    ${doorSwing(104, 600, 60, 0)}
    ${txt(150, 590, 'ENTRY', 18)}
  `),
  // rk-104: Studio — kitchen located at the entry.
  'plan-rk-104': frame('STUDIO — UNIT PLAN (RK-104)', `
    ${wallRect(100, 80, 1000, 680)}
    <line x1="100" y1="260" x2="380" y2="260" stroke="#111" stroke-width="3"/>
    <line x1="380" y1="80" x2="380" y2="760" stroke="#111" stroke-width="3"/>
    <line x1="820" y1="420" x2="1100" y2="420" stroke="#111" stroke-width="3"/>
    <line x1="820" y1="420" x2="820" y2="760" stroke="#111" stroke-width="3"/>
    ${txt(240, 190, 'KITCHEN — AT ENTRY', 22, { fill: '#c0392b' })}
    ${txt(240, 550, 'CLOSET', 20)}
    ${txt(600, 260, 'MAIN ROOM (SLEEP / LIVE)', 26)}
    ${txt(960, 590, 'BATH', 22)}
    <rect x="96" y="150" width="8" height="70" fill="#fff"/>
    ${doorSwing(104, 150, 60, 0)}
    ${txt(150, 130, 'ENTRY', 18)}
  `),
};

for (const [name, svg] of Object.entries(scenes)) {
  await sharp(Buffer.from(svg)).png().toFile(`${OUT}${name}.png`);
  console.log(`wrote ${name}.png`);
}
