import sharp from 'sharp';
import { mkdirSync } from 'fs';

const OUT = new URL('../backend/HarmonIQ.Api/Data/sample-photos/', import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const W = 1200, H = 900;
const txt = (x, y, t, size = 26, fill = '#3a3a3a') =>
  `<text x="${x}" y="${y}" font-family="Helvetica, Arial" font-size="${size}" font-weight="bold" fill="${fill}" text-anchor="middle">${t}</text>`;
const room = (wall, floor, body) =>
  `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">
     <rect width="${W}" height="${H}" fill="${wall}"/>
     <rect y="600" width="${W}" height="300" fill="${floor}"/>${body}</svg>`;

const scenes = {
  // Bedroom: bed in direct line with the door, mirror facing the bed, storage under the bed, beam over the headboard.
  bedroom: room('#f3ead9', '#c9a97a', `
    <rect x="60" y="60" width="1080" height="34" fill="#8b7355"/>${txt(600, 84, 'EXPOSED CEILING BEAM', 22, '#fff')}
    <rect x="90" y="150" width="280" height="230" fill="#bfe0f5" stroke="#7d7d7d" stroke-width="8"/>${txt(230, 410, 'WINDOW')}
    <rect x="520" y="120" width="170" height="340" fill="#a0522d"/><rect x="530" y="130" width="150" height="320" fill="#8b4020"/>${txt(605, 100, 'OPEN DOOR')}
    <rect x="470" y="470" width="280" height="60" fill="#7a5c3e"/>
    <rect x="470" y="530" width="280" height="220" rx="10" fill="#ffffff" stroke="#8a8a8a" stroke-width="5"/>${txt(610, 640, 'BED — FOOT POINTS AT DOOR', 22)}
    <rect x="485" y="760" width="110" height="48" fill="#9a8465"/><rect x="625" y="760" width="110" height="48" fill="#9a8465"/>${txt(610, 845, 'STORAGE BOXES UNDER BED', 22)}
    <rect x="960" y="280" width="130" height="330" fill="#d7ecf2" stroke="#8a8a8a" stroke-width="7"/>${txt(1025, 255, 'MIRROR (FACES BED)', 22)}
  `),
  // Living room: sofa blocking the path from the door (blocked chi), heavy bookcase in the bright window corner, clutter.
  living: room('#efe7dc', '#b58d5f', `
    <rect x="60" y="130" width="150" height="330" fill="#a0522d"/>${txt(135, 110, 'DOOR')}
    <rect x="240" y="380" width="420" height="170" rx="14" fill="#5b7ea3"/>${txt(450, 470, 'SOFA BLOCKS PATH FROM DOOR', 22, '#fff')}
    <rect x="880" y="120" width="260" height="250" fill="#bfe0f5" stroke="#7d7d7d" stroke-width="8"/>${txt(1010, 100, 'BRIGHT WINDOW CORNER')}
    <rect x="900" y="380" width="220" height="330" fill="#6e4b2a"/>${txt(1010, 545, 'HEAVY BOOKCASE', 22, '#fff')}
    <rect x="300" y="620" width="70" height="50" fill="#c96"/><rect x="390" y="640" width="90" height="40" fill="#996"/><rect x="500" y="615" width="60" height="60" fill="#a77"/>${txt(430, 730, 'CLUTTER: BOXES &amp; PILES', 22)}
    <rect x="680" y="560" width="120" height="150" fill="#4c7a4c"/>${txt(740, 545, 'PLANT')}
  `),
  // Kitchen: stove directly beside the sink (fire/water clash), knife block on the counter, decent light.
  kitchen: room('#f2f2ea', '#9aa2a8', `
    <rect x="80" y="430" width="1040" height="120" fill="#d8d2c6"/><rect x="80" y="550" width="1040" height="200" fill="#7a746a"/>
    <rect x="330" y="360" width="220" height="80" fill="#333"/><circle cx="380" cy="400" r="24" fill="#e25822"/><circle cx="480" cy="400" r="24" fill="#e25822"/>${txt(440, 340, 'STOVE')}
    <rect x="560" y="380" width="180" height="60" rx="8" fill="#b9c7cf"/>${txt(650, 360, 'SINK (TOUCHES STOVE)', 22)}
    <rect x="820" y="360" width="90" height="80" fill="#5a3d2b"/>${txt(865, 340, 'KNIFE BLOCK', 20)}
    <rect x="900" y="90" width="230" height="220" fill="#cdeafd" stroke="#7d7d7d" stroke-width="8"/>${txt(1015, 70, 'WINDOW')}
    <rect x="120" y="620" width="140" height="120" fill="#4c7a4c"/>${txt(190, 780, 'HERB PLANTS', 22)}
  `),
  // Bathroom: toilet lid up, mirror over sink, dark and windowless.
  bathroom: room('#dfe3e6', '#aab4ba', `
    <rect width="${W}" height="${H}" fill="#c9ced3" opacity="0.45"/>${txt(600, 60, 'NO WINDOW — DIM LIGHT', 24)}
    <rect x="180" y="380" width="180" height="230" rx="16" fill="#fff" stroke="#888" stroke-width="5"/>
    <ellipse cx="270" cy="380" rx="90" ry="34" fill="#eef"/>${txt(270, 660, 'TOILET — LID OPEN', 22)}
    <rect x="620" y="430" width="260" height="110" rx="10" fill="#dfe9ee" stroke="#888" stroke-width="5"/>${txt(750, 580, 'SINK')}
    <rect x="640" y="150" width="220" height="240" fill="#d7ecf2" stroke="#8a8a8a" stroke-width="7"/>${txt(750, 130, 'MIRROR')}
    <circle cx="1030" cy="700" r="26" fill="#556"/>${txt(1030, 760, 'FLOOR DRAIN', 20)}
  `),
  // Home office: desk with back to the door (not commanding), cable clutter, one plant.
  office: room('#efe9df', '#b58d5f', `
    <rect x="920" y="120" width="160" height="340" fill="#a0522d"/>${txt(1000, 100, 'DOOR BEHIND DESK')}
    <rect x="300" y="400" width="420" height="40" fill="#7a5c3e"/><rect x="330" y="440" width="30" height="180" fill="#7a5c3e"/><rect x="660" y="440" width="30" height="180" fill="#7a5c3e"/>${txt(510, 380, 'DESK — CHAIR BACK TO DOOR', 22)}
    <rect x="430" y="300" width="170" height="100" fill="#222"/>${txt(515, 290, 'MONITOR', 20)}
    <path d="M320 630 q60 40 140 10 t180 20 t120 -15" stroke="#444" stroke-width="8" fill="none"/>${txt(520, 700, 'CABLE CLUTTER', 22)}
    <rect x="120" y="140" width="240" height="220" fill="#bfe0f5" stroke="#7d7d7d" stroke-width="8"/>${txt(240, 120, 'WINDOW')}
    <rect x="130" y="520" width="110" height="140" fill="#4c7a4c"/>${txt(185, 690, 'PLANT', 22)}
  `),
  // Non-interior shots for classification/selection behavior.
  exterior: `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">
    <rect width="${W}" height="${H}" fill="#bfe0f5"/><rect y="700" width="${W}" height="200" fill="#7fa96b"/>
    <rect x="380" y="200" width="440" height="520" fill="#c9b8a3"/>
    ${[0,1,2,3].map(r => [0,1,2].map(c => `<rect x="${420 + c * 130}" y="${240 + r * 110}" width="80" height="70" fill="#8fb6cf"/>`).join('')).join('')}
    <rect x="560" y="620" width="90" height="100" fill="#6e4b2a"/>${txt(600, 780, 'THE ELM — BUILDING EXTERIOR', 28)}</svg>`,
  floorplan: `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}">
    <rect width="${W}" height="${H}" fill="#ffffff"/><rect x="150" y="100" width="900" height="700" fill="none" stroke="#333" stroke-width="6"/>
    <line x1="600" y1="100" x2="600" y2="500" stroke="#333" stroke-width="5"/><line x1="150" y1="500" x2="1050" y2="500" stroke="#333" stroke-width="5"/>
    ${txt(370, 300, 'BEDROOM 1')}${txt(830, 300, 'BEDROOM 2')}${txt(600, 660, 'LIVING / KITCHEN')}${txt(600, 860, 'UNIT 414 — FLOOR PLAN', 30)}</svg>`,
};

for (const [name, svg] of Object.entries(scenes)) {
  await sharp(Buffer.from(svg)).jpeg({ quality: 88 }).toFile(`${OUT}${name}.jpg`);
  console.log(`wrote ${name}.jpg`);
}
