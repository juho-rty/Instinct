// UI icons for Instinct: one distinct glyph per key, one style for all.
// Run: node tools/make-icons.mjs   (writes game/assets/ui/color_<key>.svg)
// Style: 96×96 canvas, bold dark ink outline (#172b2d), flat fills from one palette, a light highlight stroke.
// No two keys share a drawing; tools/icon-sheet.mjs renders them all side by side to check that.
import fs from 'node:fs';
import path from 'node:path';

const root = new URL('../game/assets/ui/', import.meta.url).pathname.replace(/^\/([A-Z]:)/i, '$1');
const ink = '#172b2d', SW = 3.2;
// palette
const K = {
  gold: '#e9b75d', gold2: '#c9902f', wood: '#b07a45', wood2: '#7a5232', bark: '#8a5a33', pale: '#e8c58c',
  steel: '#c3d6d9', steel2: '#8fa4aa', iron: '#5f6d74', dark: '#2c3a40', red: '#d8644e', red2: '#a8443c',
  green: '#79b75a', green2: '#4f8a46', blue: '#5aa6c8', blue2: '#3f7f9e', cream: '#f1e3bf', skin: '#e8b98f',
  purple: '#9b7cc4', stone: '#a9b4b8', stone2: '#87959b', clay: '#cf7c4c', white: '#f6f2e8', black: '#1f2a2e',
};

// ── primitives (everything gets the same ink outline) ──
const st = `stroke="${ink}" stroke-width="${SW}" stroke-linejoin="round" stroke-linecap="round"`;
const p = (d, f) => `<path d="${d}" fill="${f}" ${st}/>`;
const c = (x, y, r, f) => `<circle cx="${x}" cy="${y}" r="${r}" fill="${f}" ${st}/>`;
const e = (x, y, rx, ry, f, rot = 0) => `<ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="${f}" ${st}${rot ? ` transform="rotate(${rot} ${x} ${y})"` : ''}/>`;
const r = (x, y, w, h, f, rx = 2) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="${rx}" fill="${f}" ${st}/>`;
const poly = (pts, f) => `<polygon points="${pts}" fill="${f}" ${st}/>`;
/** coloured stroke with an ink casing: a "line" that still reads at 20px */
const ln = (d, col, w) => `<path d="${d}" fill="none" stroke="${ink}" stroke-width="${w + 2 * SW}" stroke-linecap="round" stroke-linejoin="round"/><path d="${d}" fill="none" stroke="${col}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`;
/** thin detail line, no casing */
const hl = (d, col = ink, w = 2.4) => `<path d="${d}" fill="none" stroke="${col}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`;
/** small filled dot, no outline */
const dot = (x, y, rr, f) => `<circle cx="${x}" cy="${y}" r="${rr}" fill="${f}"/>`;
const shine = d => hl(d, 'rgba(255,255,255,0.55)', 3);
const g = (t, b) => `<g transform="${t}">${b}</g>`;
const mirror = b => g('translate(96 0) scale(-1 1)', b);
const ring = (x, y, rr, col, w) => `<circle cx="${x}" cy="${y}" r="${rr}" fill="none" stroke="${ink}" stroke-width="${w + 2 * SW}"/><circle cx="${x}" cy="${y}" r="${rr}" fill="none" stroke="${col}" stroke-width="${w}"/>`;
const eRing = (x, y, rx, ry, col, w, rot = 0) => { const t = rot ? ` transform="rotate(${rot} ${x} ${y})"` : ''; return `<ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="none" stroke="${ink}" stroke-width="${w + 2 * SW}"${t}/><ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="none" stroke="${col}" stroke-width="${w}"${t}/>`; };
const star = (x, y, ro, ri, n, f, rot = -90) => {
  const pts = [];
  for (let i = 0; i < n * 2; i++) {
    const a = (rot + i * 180 / n) * Math.PI / 180, rr = i % 2 ? ri : ro;
    pts.push(`${(x + rr * Math.cos(a)).toFixed(1)},${(y + rr * Math.sin(a)).toFixed(1)}`);
  }
  return poly(pts.join(' '), f);
};
const gear = (x, y, ro, ri, n, f) => {
  const pts = [];
  for (let i = 0; i < n * 4; i++) {
    const a = (i * 90 / n) * Math.PI / 180, rr = (i % 4 < 2) ? ro : ri;
    pts.push(`${(x + rr * Math.cos(a)).toFixed(1)},${(y + rr * Math.sin(a)).toFixed(1)}`);
  }
  return poly(pts.join(' '), f);
};
const skull = (x, y, s = 1) => g(`translate(${x} ${y}) scale(${s})`,
  p('M-22 4 C-26 -24 26 -24 22 4 C22 12 16 14 14 16 L14 26 L-14 26 L-14 16 C-16 14 -22 12 -22 4Z', K.cream) +
  e(-10, 2, 7, 8, K.black) + e(10, 2, 7, 8, K.black) + poly('0,10 -4,17 4,17', K.black) +
  hl('M-6 20 L-6 26 M0 20 L0 26 M6 20 L6 26', ink, 2.4));
const crown = (x, y, s = 1, f = K.gold) => g(`translate(${x} ${y}) scale(${s})`,
  p('M-30 18 L-34 -16 L-16 0 L0 -22 L16 0 L34 -16 L30 18Z', f) + r(-31, 16, 62, 10, K.gold2, 2) +
  dot(-16, 21, 3.5, K.red) + dot(0, 21, 3.5, K.blue) + dot(16, 21, 3.5, K.green));
const coinFace = (x, y, rr) => c(x, y, rr, K.gold) + c(x, y, rr * 0.72, 'none') + shine(`M${x - rr * 0.5} ${y - rr * 0.35} Q${x - rr * 0.3} ${y - rr * 0.6} ${x} ${y - rr * 0.62}`);
const grains = (x0, y0, x1, y1, n) => {
  let s = '';
  for (let i = 0; i < n; i++) {
    const t = 0.25 + i * 0.75 / n, x = x0 + (x1 - x0) * t, y = y0 + (y1 - y0) * t;
    const a = Math.atan2(y1 - y0, x1 - x0) * 180 / Math.PI;
    s += e(x - 5, y, 4, 7, K.gold, a + 60) + e(x + 5, y, 4, 7, K.gold, a + 120);
  }
  return s + e(x1, y1, 3.5, 6.5, K.gold, Math.atan2(y1 - y0, x1 - x0) * 180 / Math.PI + 90);
};
const leaf = (x, y, a, f = K.green, s = 1) => g(`translate(${x} ${y}) rotate(${a}) scale(${s})`, p('M0 0 C6 -6 16 -6 22 0 C16 6 6 6 0 0Z', f));

// ── icons ──
const I = {};

// 자원과 일
I.food = ln('M48 88 L32 30 M48 88 L48 20 M48 88 L64 30', K.wood, 4) + grains(48, 88, 32, 26, 3) + grains(48, 88, 48, 14, 3) + grains(48, 88, 64, 26, 3) + r(36, 62, 24, 9, K.red2, 3);
I.farm = p('M6 66 L90 66 L84 90 L12 90Z', K.bark) + hl('M14 76 L82 76 M18 84 L78 84', '#5b3a1f', 3.5) + [26, 48, 70].map(x => ln(`M${x} 66 L${x} 30`, K.green2, 5) + e(x, 26, 7, 12, K.gold) + leaf(x, 50, -40, K.green, 0.7)).join('');
I.farming = p('M12 80 Q48 60 84 80 L84 88 L12 88Z', K.bark) + ln('M48 76 L48 40', K.green2, 5) + p('M48 52 C30 52 18 40 16 24 C34 24 46 34 48 52Z', K.green) + p('M48 44 C58 42 72 30 78 14 C60 14 50 26 48 44Z', '#9bd46e');
I.plow = ln('M10 26 L54 64', K.wood, 7) + ln('M54 64 L74 30 M60 66 L84 40', K.wood2, 4) + p('M44 60 L74 70 L56 84 L36 74Z', K.steel) + hl('M6 88 L90 88', K.bark, 4);
I.herding = ln('M34 66 L34 84 M58 66 L58 84', K.black, 5) + c(28, 50, 13, K.white) + c(44, 40, 15, K.white) + c(60, 46, 14, K.white) + c(54, 60, 13, K.white) + c(36, 62, 13, K.white) + e(76, 50, 10, 12, K.black) + dot(79, 47, 2.5, '#fff') + e(68, 40, 7, 3.5, K.black, -30);
I.hunt = ln('M30 8 Q84 48 30 88', K.wood, 9) + hl('M30 8 L30 88', K.cream, 3) + ln('M10 48 L74 48', K.pale, 6) + poly('72,36 92,48 72,60', K.steel) + p('M10 48 L4 36 L20 36 L26 48 L20 60 L4 60Z', K.red);
I.gather = c(34, 42, 9, '#c8445a') + c(50, 36, 10, '#d4566a') + c(64, 44, 9, '#c8445a') + leaf(52, 26, -70, K.green2, 0.8) + p('M14 50 L82 50 L72 86 L24 86Z', '#c99a5a') + hl('M20 62 L76 62 M24 74 L72 74', '#8a6232', 3) + hl('M38 52 L40 84 M58 52 L56 84', '#8a6232', 3);
I.fish = p('M70 48 L90 30 L86 48 L90 66Z', K.blue2) + p('M10 48 C26 24 60 24 74 48 C60 72 26 72 10 48Z', K.blue) + c(26, 44, 4, K.black) + hl('M42 34 C48 42 48 54 42 62', K.blue2, 3) + p('M44 30 L56 18 L60 34Z', K.blue2);
I.wood = [[28, 68], [68, 68], [48, 34]].map(([x, y]) => c(x, y, 19, K.bark) + c(x, y, 13.5, K.pale) + `<circle cx="${x}" cy="${y}" r="7" fill="none" stroke="#b88a52" stroke-width="2.4"/>` + dot(x, y, 2, '#b88a52')).join('');
I.stone = p('M8 82 L16 56 L38 46 L54 60 L50 84Z', K.stone) + p('M42 84 L48 50 L68 32 L88 50 L86 84Z', K.stone2) + shine('M20 58 L36 50 M52 50 L66 38');
I.build = ln('M24 84 L56 44', K.wood, 11) + p('M34 34 L56 12 L86 42 L64 64Z', K.steel2) + p('M56 12 L66 22 L44 44 L34 34Z', K.steel) + shine('M48 24 L58 16');
I.scout = ln('M14 78 L40 56', K.wood, 14) + ln('M40 56 L64 36', K.gold, 10) + ln('M64 36 L80 22', K.steel, 7) + c(82, 20, 6, '#8fd1e8');
I.guard = p('M48 8 L80 20 L76 54 C70 70 58 80 48 88 C38 80 26 70 20 54 L16 20Z', K.blue2) + p('M48 8 L48 88 C38 80 26 70 20 54 L16 20Z', K.blue) + shine('M24 26 L44 18');
I.steward = c(30, 30, 16, K.gold) + c(30, 30, 6, K.dark) + ln('M41 41 L80 80', K.gold, 7) + ln('M64 64 L56 72 M74 74 L66 82', K.gold, 6);
I.feast = p('M42 50 C20 64 14 40 24 26 C36 10 64 16 66 34 C68 44 58 50 50 54Z', '#b8643a') + ln('M50 54 L74 78', K.white, 8) + c(78, 76, 6, K.white) + c(72, 84, 6, K.white) + shine('M30 26 C36 20 46 20 52 24');

// 화면과 나라
I.law = ln('M48 20 L48 78', K.wood, 8) + p('M24 90 L72 90 L64 76 L32 76Z', K.wood2) + ln('M14 28 L82 28', K.gold, 8) + ln('M18 28 L10 54 M18 28 L28 54 M78 28 L68 54 M78 28 L88 54', K.gold2, 3) + p('M4 54 L34 54 Q19 74 4 54Z', K.gold) + p('M62 54 L92 54 Q77 74 62 54Z', K.gold) + c(48, 16, 8, K.gold);
I.log = p('M24 16 L72 16 L72 80 L24 80Z', K.cream) + r(16, 10, 64, 11, K.pale, 5) + r(16, 76, 64, 11, K.pale, 5) + hl('M32 34 L64 34 M32 44 L64 44 M32 54 L58 54 M32 64 L62 64', '#8a7350', 3);
I.realm = p('M8 24 L32 14 L62 24 L88 14 L88 72 L62 82 L32 72 L8 82Z', K.cream) + hl('M32 14 L32 72 M62 24 L62 82', '#b9a77a', 2.4) + hl('M16 62 C30 50 40 64 52 48 C60 38 70 46 78 34', K.red, 3) + hl('M72 28 L82 38 M82 28 L72 38', K.red2, 3.5);
I.land = p('M10 88 Q34 66 58 88Z', K.green) + ln('M34 82 L34 12', K.wood2, 4) + p('M36 14 L80 26 L36 40Z', K.blue) + shine('M40 20 L60 26');
I.home = p('M24 30 L24 86 L72 86 L72 30 L64 30 L64 20 L56 20 L56 30 L52 30 L52 20 L44 20 L44 30 L40 30 L40 20 L32 20 L32 30Z', '#cdb98f') + p('M40 86 L40 68 Q48 58 56 68 L56 86Z', '#5a3e2a') + r(44, 40, 8, 10, K.dark, 4) + ln('M48 20 L48 6', K.wood2, 2.5) + p('M49 6 L64 10 L49 14Z', K.red);
I.people = p('M14 88 C14 62 30 54 48 54 C66 54 82 62 82 88Z', K.blue) + c(48, 32, 17, K.skin) + p('M31 30 C30 14 66 12 65 30 C58 22 40 22 31 30Z', '#5a3e2a');
I.pop = p('M6 82 C6 64 14 58 26 58 C38 58 46 64 46 82Z', '#6b8f9e') + c(26, 42, 11, '#d9a77e') + p('M50 82 C50 64 58 58 70 58 C82 58 90 64 90 82Z', '#6b8f9e') + c(70, 42, 11, '#d9a77e') + p('M24 88 C24 66 34 60 48 60 C62 60 72 66 72 88Z', K.gold) + c(48, 44, 13, K.skin);
I.tech = p('M8 40 Q28 32 48 42 L48 86 Q28 76 8 84Z', K.cream) + p('M88 40 Q68 32 48 42 L48 86 Q68 76 88 84Z', '#e6d3a4') + hl('M16 52 Q28 48 40 54 M16 62 Q28 58 40 64 M56 54 Q68 48 80 52 M56 64 Q68 58 80 62', K.stone2, 3) + p('M48 6 L54 24 L48 34 L42 24Z', K.gold) + hl('M30 14 L36 26 M66 14 L60 26', K.gold, 4);
I.coin = c(48, 48, 34, K.gold) + c(48, 48, 25, '#f2c86e') + r(40, 40, 16, 16, K.gold2, 1) + shine('M26 32 Q34 20 48 18');
I.unrest = r(36, 58, 24, 30, K.skin, 4) + r(26, 18, 44, 44, K.skin, 12) + hl('M37 18 L37 36 M48 18 L48 36 M59 18 L59 36', ink, 2.6) + p('M26 40 C26 32 46 32 46 42 L46 48 C40 52 30 50 26 46Z', '#f0c9a2') + hl('M84 20 L76 30 M88 40 L78 42 M78 8 L72 20', K.red, 4);
I.cohesion = ring(34, 48, 20, K.gold, 8) + ring(62, 48, 20, K.gold2, 8) + `<path d="M34 28 A20 20 0 0 1 52 37" fill="none" stroke="${ink}" stroke-width="${8 + 2 * SW}"/><path d="M34 28 A20 20 0 0 1 52 37" fill="none" stroke="${K.gold}" stroke-width="8"/>`;
I.legit = (() => {
  let s = '';
  for (let i = 0; i < 6; i++) {
    const a = 110 + i * 28, rad = a * Math.PI / 180, x = 48 + 30 * Math.cos(rad), y = 50 + 30 * Math.sin(rad);
    s += leaf(x, y, a + 90, K.green, 0.95) + mirror(leaf(x, y, a + 90, K.green2, 0.95));
  }
  return s + p('M34 80 L48 72 L62 80 L58 90 L48 84 L38 90Z', K.red) + star(48, 46, 12, 5, 5, K.gold);
})();
I.army = p('M16 62 C16 30 32 14 48 14 C64 14 80 30 80 62 L80 78 L62 78 L62 58 L34 58 L34 78 L16 78Z', K.steel) + hl('M48 58 L48 74', ink, 3) + p('M30 16 C38 2 58 2 66 16 C58 10 38 10 30 16Z', K.red) + shine('M26 40 C28 30 34 24 40 22');
I.raid = (() => { const one = ln('M16 86 L66 24', K.wood, 5) + p('M56 18 C62 4 82 6 88 22 C80 26 72 32 68 38 L56 26Z', K.steel); return one + mirror(one); })();

// 길(네 갈래)
I.conquest = (() => { const one = ln('M18 82 L74 20', '#e6eef0', 6) + poly('70,16 84,10 78,24', '#e6eef0') + ln('M20 64 L36 80', K.gold, 5) + c(14, 86, 5, K.gold); return one + mirror(one); })();
I.commerce = p('M32 32 C12 48 14 86 48 86 C82 86 84 48 64 32Z', '#c99a5a') + p('M32 32 L24 14 L40 22 L48 10 L56 22 L72 14 L64 32Z', '#b0844a') + ln('M30 34 L66 34', K.red2, 4) + coinFace(48, 62, 12);
I.terror = skull(48, 48, 1.45);
I.tyranny = crown(48, 54, 1.2);

// 줄기 기술
I.fire = ln('M16 82 L80 66', K.bark, 7) + ln('M16 66 L80 82', K.wood2, 7) + p('M48 10 C64 28 72 40 66 56 C62 68 34 68 30 56 C24 42 38 32 48 10Z', '#f08a3a') + p('M48 34 C56 42 58 50 54 58 C50 62 44 62 42 58 C38 50 44 44 48 34Z', '#ffd36a');
I.pottery = p('M38 12 L58 12 L56 22 C76 32 76 60 60 78 L58 86 L38 86 L36 78 C20 60 20 32 40 22Z', K.clay) + hl('M30 42 L66 42 M28 54 L68 54', '#7a3f22', 3) + hl('M38 24 C26 22 24 34 32 38 M58 24 C70 22 72 34 64 38', ink, 3) + shine('M32 34 C30 44 32 56 38 66');
I.counting = r(10, 14, 76, 68, K.wood2, 6) + r(18, 22, 60, 52, '#2a3236', 3) + hl('M18 36 L78 36 M18 50 L78 50 M18 64 L78 64', K.pale, 2.4) + [26, 36, 60].map(x => e(x, 36, 5, 5, K.red)).join('') + [30, 52, 62, 72].map(x => e(x, 50, 5, 5, K.gold)).join('') + [24, 34, 44].map(x => e(x, 64, 5, 5, K.blue)).join('');
I.bronze = p('M48 6 C60 24 60 46 55 62 L41 62 C36 46 36 24 48 6Z', '#d69a5c') + hl('M48 14 L48 58', '#9a5f2e', 2.4) + r(30, 60, 36, 7, K.wood2, 3) + r(42, 67, 12, 15, '#5a3e2a', 2) + c(48, 86, 5, '#d69a5c');
I.script = p('M18 12 L74 10 C80 10 82 14 82 20 L80 80 C80 86 76 88 70 88 L22 86 C16 86 14 82 14 76 L14 18 C14 14 16 12 18 12Z', '#c99a6a') + [[28, 26], [44, 26], [60, 26], [28, 42], [48, 42], [64, 42], [28, 58], [40, 58], [58, 58], [30, 72], [50, 72]].map(([x, y]) => poly(`${x},${y} ${x + 9},${y - 3} ${x + 9},${y + 3}`, '#6d4626')).join('');
I.wheel = ring(48, 48, 32, K.wood, 9) + [0, 60, 120].map(a => ln(`M${48 + 30 * Math.cos(a * Math.PI / 180)} ${48 + 30 * Math.sin(a * Math.PI / 180)} L${48 - 30 * Math.cos(a * Math.PI / 180)} ${48 - 30 * Math.sin(a * Math.PI / 180)}`, K.wood, 4)).join('') + c(48, 48, 9, K.gold);
I.iron = p('M8 28 L68 28 C80 28 88 34 90 40 L64 44 L62 52 C62 56 68 58 72 60 L72 72 L26 72 L26 60 C30 58 36 56 36 52 L34 44 L14 40Z', K.iron) + r(20, 72, 58, 12, '#47535a', 3) + shine('M14 32 L60 32') + hl('M50 10 L60 22 M66 6 L68 20 M80 12 L74 22', '#ff9b3d', 3.5);
I.currency = [0, 1, 2, 3].map(i => r(18, 66 - i * 12, 52, 12, K.gold2, 6) + e(44, 66 - i * 12, 26, 6, K.gold)).join('') + coinFace(72, 70, 14);
I.masonry = r(8, 16, 80, 70, '#8f4a35', 3) + [0, 1, 2, 3, 4].map(row => {
  const y = 16 + row * 14, off = row % 2 ? 0 : 10;
  let s = '';
  for (let x = 8 - off; x < 88; x += 20) { const x0 = Math.max(8, x), x1 = Math.min(88, x + 20); s += r(x0 + 1, y + 1, x1 - x0 - 2, 12, '#c46a4a', 1.5); }
  return s;
}).join('');
I.crafts = r(22, 12, 40, 10, K.wood, 3) + r(22, 74, 40, 10, K.wood, 3) + r(28, 22, 28, 52, K.blue, 2) + hl('M28 32 L56 32 M28 42 L56 42 M28 52 L56 52 M28 62 L56 62', K.blue2, 2.4) + ln('M60 86 L88 18', '#e6eef0', 4) + hl('M56 50 C68 46 76 40 82 30', K.blue, 2.4);
I.theology = poly('48,8 88,32 8,32', '#e8dcc0') + r(8, 32, 80, 8, '#d8c8a0', 1) + [16, 34, 54, 72].map(x => r(x, 40, 10, 36, K.white, 1)).join('') + r(4, 76, 88, 10, '#d8c8a0', 2) + c(48, 24, 4, K.gold);
I.press = r(12, 10, 72, 12, K.wood2, 3) + r(16, 22, 10, 64, K.wood, 2) + r(70, 22, 10, 64, K.wood, 2) + ln('M48 22 L48 44', K.steel2, 6) + ln('M30 30 L66 30', K.steel2, 4) + r(30, 44, 36, 10, K.iron, 2) + r(26, 60, 44, 12, K.white, 1) + hl('M32 66 L64 66', '#555', 2) + r(8, 80, 80, 8, K.wood2, 2);
I.gunpowder = ln('M14 50 L72 30', K.iron, 18) + ring(72, 30, 5, K.iron, 4) + c(38, 64, 15, K.wood) + c(38, 64, 4, K.gold) + hl('M38 50 L38 78 M24 64 L52 64', K.wood2, 2.4) + c(70, 78, 7, K.black) + c(84, 82, 6, K.black);
I.navigation = c(48, 48, 36, K.cream) + c(48, 48, 28, 'none') + poly('48,12 56,48 48,84 40,48', K.red) + poly('12,48 48,40 84,48 48,56', K.steel2) + poly('48,12 56,48 40,48', '#e98a72') + c(48, 48, 4, K.gold);
I.banking = r(14, 54, 62, 26, '#8ec27e', 3) + r(20, 44, 62, 26, '#9fd08c', 3) + r(26, 34, 62, 26, '#b4e0a0', 3) + c(57, 47, 7, 'none') + hl('M34 40 L42 40 M72 54 L80 54', K.green2, 3);
I.steam = r(10, 44, 64, 34, K.iron, 8) + r(58, 30, 14, 16, '#47535a', 2) + c(28, 82, 8, K.dark) + c(56, 82, 8, K.dark) + r(14, 52, 18, 12, K.gold, 2) + c(68, 20, 8, K.white) + c(80, 12, 9, K.white) + c(58, 12, 6, K.white);
I.rail = ln('M10 88 L36 8', K.steel2, 4) + ln('M86 88 L60 8', K.steel2, 4) + [[16, 80, 64], [24, 62, 48], [31, 46, 34], [36, 32, 24], [40, 20, 16]].map(([x, y, w]) => r(x, y, w, 5, K.wood, 1.5)).join('');
I.medicine = p('M38 8 L58 8 L58 30 C76 38 80 60 72 74 C66 84 30 84 24 74 C16 60 20 38 38 30Z', '#cfe8ec') + p('M22 60 C30 54 66 54 74 60 C76 70 70 82 48 82 C26 82 20 70 22 60Z', '#7fd0a8') + r(34, 4, 28, 8, K.wood, 2) + p('M44 36 L52 36 L52 42 L58 42 L58 50 L52 50 L52 56 L44 56 L44 50 L38 50 L38 42 L44 42Z', K.red);
I.electricity = p('M48 8 C70 8 82 26 76 44 C72 54 62 58 62 70 L34 70 C34 58 24 54 20 44 C14 26 26 8 48 8Z', '#fff0ad') + hl('M40 66 L40 50 L44 40 L48 50 L52 40 L56 50 L56 66', K.gold2, 3) + r(34, 70, 28, 14, K.steel2, 3) + hl('M34 76 L62 76', ink, 2) + r(42, 84, 12, 6, K.iron, 2) + shine('M28 30 C30 20 38 14 46 14');
I.engine = gear(48, 48, 38, 30, 9, K.steel2) + c(48, 48, 14, K.iron) + c(48, 48, 5, K.dark);
I.broadcast = poly('48,18 66,88 30,88', 'none') + hl('M48 18 L36 88 M48 18 L60 88 M40 66 L56 66 M42 50 L54 50 M44 36 L52 36 M38 78 L58 78', ink, 3) + ln('M48 18 L36 88 M48 18 L60 88', K.red, 3) + c(48, 16, 6, K.gold) + hl('M30 6 C22 12 22 22 30 28 M66 6 C74 12 74 22 66 28 M20 0 C8 10 8 26 20 36 M76 0 C88 10 88 26 76 36', K.blue, 3.5);
I.fission = eRing(48, 48, 40, 13, '#8fd1e8', 4) + eRing(48, 48, 40, 13, '#8fd1e8', 4, 60) + eRing(48, 48, 40, 13, '#8fd1e8', 4, -60) + c(48, 48, 10, K.red) + c(86, 48, 5, K.gold) + c(28, 16, 5, K.gold) + c(28, 80, 5, K.gold);
I.computing = [26, 40, 54, 68].map(x => hl(`M${x} 8 L${x} 18 M${x} 78 L${x} 88 M8 ${x} L18 ${x} M78 ${x} L88 ${x}`, K.steel2, 4)).join('') + r(16, 16, 64, 64, K.black, 6) + r(30, 30, 36, 36, '#2e6f5e', 3) + hl('M36 40 L52 40 L52 56 M44 48 L60 48', '#9be0c4', 2.4);
I.ecology = p('M16 82 C10 44 40 12 86 10 C86 56 60 86 16 82Z', K.green) + hl('M18 80 C40 60 56 42 78 18 M36 62 L36 44 M50 48 L52 30 M36 62 L56 64 M52 48 L70 50', K.green2, 3);

// 갈림길(시대마다 넷)
I.phalanx = ln('M74 92 L74 14', K.wood, 4) + poly('74,2 82,18 66,18', '#e6eef0') + c(40, 54, 28, '#d69a5c') + c(40, 54, 20, 'none') + c(40, 54, 7, K.gold);
I.caravan = p('M8 30 L20 20 L28 24 L30 44 C34 26 44 24 50 38 C56 26 66 26 70 40 C80 42 86 52 82 62 L78 62 L78 86 L71 86 L69 66 L44 66 L42 86 L35 86 L33 62 C26 58 24 52 24 46 L21 32 L11 36Z', '#d9a86a') + r(40, 40, 22, 14, K.red2, 3) + dot(14, 27, 2, ink);
I.skulltower = skull(30, 66, 0.62) + skull(66, 66, 0.62) + skull(48, 34, 0.62) + hl('M6 90 L90 90', K.stone2, 4);
I.godking = [0, 1, 2, 3, 4, 5, 6, 7].map(i => { const a = i * 45 * Math.PI / 180; return poly(`${48 + 26 * Math.cos(a - 0.2)},${56 + 26 * Math.sin(a - 0.2)} ${48 + 40 * Math.cos(a)},${56 + 40 * Math.sin(a)} ${48 + 26 * Math.cos(a + 0.2)},${56 + 26 * Math.sin(a + 0.2)}`, K.gold); }).join('') + c(48, 56, 22, '#ffd36a') + crown(48, 30, 0.55, K.gold2);
I.chivalry = p('M28 88 L28 68 C20 62 16 50 20 38 L38 12 L44 22 L58 22 C72 26 84 40 82 52 L72 56 L60 46 L54 50 C62 60 66 74 66 88Z', '#cfd8dc') + p('M38 12 L44 22 L58 22 C50 24 40 34 34 50 C30 40 32 26 38 12Z', K.iron) + dot(64, 32, 3, ink) + r(22, 82, 50, 8, K.red, 2);
I.fairs = p('M48 14 L84 60 L84 84 L12 84 L12 60Z', K.white) + p('M48 14 L30 60 L30 84 L12 84 L12 60Z', K.red) + p('M48 14 L66 60 L66 84 L48 84 L48 60Z', K.red) + p('M38 84 L38 66 Q48 58 58 66 L58 84Z', K.dark) + ln('M48 14 L48 4', K.wood2, 2.4) + p('M49 4 L62 8 L49 12Z', K.gold);
I.reavers = p('M6 54 L90 54 C84 72 70 80 48 80 C26 80 12 72 6 54Z', K.bark) + p('M86 54 C92 44 90 30 80 30 C86 36 84 46 78 54Z', K.bark) + ln('M48 54 L48 8', K.wood2, 4) + p('M28 14 L68 14 L64 46 L32 46Z', K.white) + p('M38 14 L48 14 L48 46 L36 46Z', K.red) + p('M58 14 L68 14 L64 46 L58 46Z', K.red) + [20, 34, 62, 76].map(x => c(x, 62, 5, K.gold)).join('');
I.divineright = [0, 1, 2, 3, 4].map(i => { const a = (200 + i * 35) * Math.PI / 180; return hl(`M${48 + 32 * Math.cos(a)} ${30 + 32 * Math.sin(a)} L${48 + 44 * Math.cos(a)} ${30 + 44 * Math.sin(a)}`, K.gold, 4); }).join('') + p('M30 70 L30 16 C30 8 66 8 66 16 L66 70Z', K.red2) + p('M36 64 L36 20 C36 14 60 14 60 20 L60 64Z', '#c75a4c') + r(22, 60, 52, 12, K.gold, 3) + ln('M28 72 L26 88 M68 72 L70 88', K.gold2, 4) + c(48, 30, 5, K.gold);
I.drill = ln('M28 30 L60 6 M68 30 L36 6', K.wood, 4) + r(18, 40, 60, 40, K.red, 4) + hl('M18 44 L32 76 L48 44 L64 76 L78 44', K.cream, 3) + e(48, 40, 30, 8, K.cream) + r(18, 76, 60, 8, K.gold, 3);
I.jointstock = c(46, 50, 34, K.blue) + p('M46 50 L46 16 A34 34 0 0 1 76 66Z', '#7cc0dd') + p('M54 42 L54 8 A34 34 0 0 1 86 40Z', K.gold) + hl('M46 50 L16 66', ink, 3);
I.slavecoast = eRing(38, 36, 7, 5, K.steel2, 4) + eRing(48, 30, 7, 5, K.iron, 4) + eRing(58, 36, 7, 5, K.steel2, 4) + ring(24, 60, 16, K.steel2, 8) + ring(72, 60, 16, K.steel2, 8) + c(28, 46, 4, K.iron) + c(68, 46, 4, K.iron);
I.spycourt = p('M6 48 C26 18 70 18 90 48 C70 78 26 78 6 48Z', K.white) + c(48, 48, 17, '#5a8a6a') + c(48, 48, 8, K.black) + dot(53, 43, 3.5, '#fff') + p('M6 48 C26 18 70 18 90 48 C72 34 24 34 6 48Z', K.dark);
I.generalstaff = p('M30 8 L48 40 L66 8Z', K.blue2) + p('M40 8 L48 22 L56 8Z', K.red) + star(48, 60, 28, 12, 5, K.gold) + c(48, 60, 7, K.gold2);
I.exchange = hl('M12 10 L12 84 L88 84', ink, 4) + r(20, 60, 10, 22, K.stone, 1) + r(36, 48, 10, 34, K.stone, 1) + r(52, 54, 10, 28, K.stone, 1) + r(68, 34, 10, 48, K.stone, 1) + ln('M16 64 L38 42 L54 50 L80 18', K.green, 5) + poly('70,14 88,10 84,28', K.green);
I.camps = ln('M10 90 L22 14 M86 90 L74 14', K.wood2, 6) + [22, 38, 54, 70].map(x => eRing(x + 2, 52, 11, 16, K.steel, 3, 12)).join('') + [[16, 30], [40, 26], [64, 30], [30, 76], [56, 78], [80, 72]].map(([x, y]) => hl(`M${x - 4} ${y - 4} L${x + 4} ${y + 4} M${x + 4} ${y - 4} L${x - 4} ${y + 4}`, K.steel, 3)).join('');
I.partystate = ln('M20 90 L20 8', K.wood2, 4) + p('M22 10 C40 4 54 18 84 10 L84 50 C54 58 40 44 22 50Z', '#c9333a') + star(46, 29, 10, 4.2, 5, K.gold);
I.missiles = p('M74 8 C88 4 92 8 88 22 L46 64 L32 50Z', '#e6eaec') + p('M74 8 C88 4 92 8 88 22 L80 16Z', K.red) + p('M38 56 L22 54 L14 62 L32 62Z', K.red) + p('M40 58 L42 74 L34 82 L34 64Z', K.red) + p('M32 64 L14 82 C10 86 8 84 10 80 L26 60Z', '#ffb347') + c(66, 30, 5, K.blue);
I.offshore = p('M4 74 C16 68 26 80 38 74 C50 68 60 80 72 74 C82 68 88 74 92 74 L92 92 L4 92Z', K.blue) + p('M18 74 C26 58 70 58 78 74Z', '#e8d39a') + ln('M48 66 C48 48 44 34 40 26', K.wood, 4) + leaf(40, 26, 180, K.green, 1.2) + leaf(40, 26, -30, K.green, 1.2) + leaf(40, 26, 220, K.green2, 1.0) + leaf(40, 26, 30, K.green2, 1.0) + coinFace(70, 32, 11);
I.chemwar = p('M20 30 C20 10 76 10 76 30 L76 56 C76 70 62 78 48 78 C34 78 20 70 20 56Z', K.iron) + c(34, 38, 10, '#b8e0a8') + c(62, 38, 10, '#b8e0a8') + r(38, 62, 20, 26, K.dark, 4) + hl('M38 70 L58 70 M38 78 L58 78', K.steel2, 2.4) + hl('M80 14 C86 18 90 24 88 32 M8 14 C4 20 4 28 8 32', '#9bd46e', 3);
I.personality = p('M16 40 L48 22 L48 74 L16 56Z', K.gold) + r(8, 38, 10, 20, K.gold2, 2) + p('M48 22 L60 14 L60 82 L48 74Z', K.gold2) + ln('M30 56 L34 80', K.iron, 5) + hl('M70 34 C76 40 76 56 70 62 M78 24 C88 36 88 60 78 72', K.red, 3.5);

// 땅에 묻힌 자원 7종
I.res_livestock = p('M14 58 C14 38 30 30 52 30 C70 30 82 38 82 54 L80 82 L68 82 L66 64 L32 64 L30 82 L18 82Z', '#b88250') + p('M64 36 C74 34 86 40 86 52 C86 60 76 62 66 58Z', '#c99462') + ln('M70 34 C76 20 88 18 88 28 M62 34 C56 20 46 20 48 30', K.cream, 4.5) + dot(76, 46, 2.8, ink) + r(76, 52, 10, 8, '#e8b98f', 3);
I.res_copper = p('M10 78 L22 44 L48 30 L76 42 L86 76 L52 86Z', '#7a5a44') + poly('28,52 46,38 62,48 52,66 32,64', '#d97b42') + poly('54,58 72,48 78,68 60,76', '#4aa88c') + shine('M34 48 L46 42');
I.res_iron = p('M16 74 L28 50 L72 50 L84 74Z', K.iron) + poly('16,74 28,50 44,50 34,74', K.steel2) + p('M26 48 L36 28 L74 28 L82 48Z', '#6e7d85') + shine('M32 54 L66 54 M38 34 L68 34');
I.res_spice = p('M24 38 L72 38 C78 52 76 76 64 86 L32 86 C20 76 18 52 24 38Z', K.cream) + p('M24 38 C32 18 64 18 72 38Z', '#d85532') + c(40, 26, 5, '#e99b38') + c(54, 24, 5, '#b83228') + c(48, 32, 5, '#e99b38') + r(20, 36, 56, 8, K.wood, 3);
I.res_coal = p('M10 80 L18 52 L38 42 L56 56 L50 82Z', '#2d3438') + p('M42 84 L50 46 L72 34 L88 56 L82 84Z', '#1f262a') + poly('26,84 36,62 56,58 64,84', '#384146') + shine('M22 56 L36 48 M54 50 L70 40');
I.res_oil = p('M48 10 C64 34 76 48 76 64 C76 80 63 88 48 88 C33 88 20 80 20 64 C20 48 32 34 48 10Z', '#262b34') + p('M44 26 C34 42 28 52 28 64 C28 74 36 80 44 80 C36 74 34 64 36 52 C38 42 42 34 44 26Z', '#5c6678') + shine('M36 48 C34 56 34 66 40 72');
I.res_uranium = c(48, 48, 38, K.gold) + c(48, 48, 8, K.dark) + [0, 120, 240].map(a => {
  const a0 = (a - 30) * Math.PI / 180, a1 = (a + 30) * Math.PI / 180;
  const r0 = 15, r1 = 32;
  return p(`M${(48 + r0 * Math.cos(a0)).toFixed(1)} ${(48 + r0 * Math.sin(a0)).toFixed(1)} L${(48 + r1 * Math.cos(a0)).toFixed(1)} ${(48 + r1 * Math.sin(a0)).toFixed(1)} A${r1} ${r1} 0 0 1 ${(48 + r1 * Math.cos(a1)).toFixed(1)} ${(48 + r1 * Math.sin(a1)).toFixed(1)} L${(48 + r0 * Math.cos(a1)).toFixed(1)} ${(48 + r0 * Math.sin(a1)).toFixed(1)} A${r0} ${r0} 0 0 0 ${(48 + r0 * Math.cos(a0)).toFixed(1)} ${(48 + r0 * Math.sin(a0)).toFixed(1)}Z`, K.dark);
}).join('');

// 연대기 갈래와 화면 표식
I.cat_disaster = p('M48 10 L88 82 L8 82Z', '#e89238') + p('M48 24 L76 76 L20 76Z', '#ffd36a') + r(44, 38, 8, 22, ink, 3) + c(48, 68, 4.5, ink);
I.cat_diplomacy = c(32, 48, 20, K.blue) + c(64, 48, 20, K.gold) + p('M36 42 L60 42 L64 52 L56 58 L40 58 L32 52Z', K.cream) + hl('M42 48 L54 48 M42 54 L52 54', ink, 2.6);
I.gift = r(18, 42, 60, 44, K.red, 4) + r(12, 30, 72, 14, '#e4745c', 4) + r(42, 30, 12, 56, K.gold, 1) + r(18, 56, 60, 10, K.gold, 1) + p('M48 30 C32 10 18 16 26 28 C32 32 42 30 48 30Z', K.gold) + p('M48 30 C64 10 78 16 70 28 C64 32 54 30 48 30Z', K.gold2);
I.road = p('M24 88 L40 12 L56 12 L72 88Z', K.stone2) + p('M30 88 L43 12 L53 12 L66 88Z', '#c9ba96') + r(45, 20, 6, 12, K.white, 1) + r(45, 42, 6, 16, K.white, 1) + r(44, 66, 8, 18, K.white, 1);
I.woman = p('M16 88 C18 62 32 54 48 54 C64 54 78 62 80 88Z', '#c85a6e') + p('M26 30 C24 10 72 10 70 30 L74 56 C64 50 32 50 22 56Z', '#4a2e1f') + c(48, 33, 16, K.skin) + p('M32 28 C36 16 60 16 64 28 C56 22 40 22 32 28Z', '#4a2e1f');
I.child = p('M24 86 C24 66 34 58 48 58 C62 58 72 66 72 86Z', K.green) + c(48, 40, 16, K.skin) + p('M33 36 C34 20 62 20 63 36 C56 30 40 30 33 36Z', '#6a4426') + dot(43, 41, 2.2, ink) + dot(53, 41, 2.2, ink);
I.skull = skull(48, 46, 1.35);
I.gear = gear(48, 48, 38, 29, 8, K.steel) + c(48, 48, 15, K.dark) + c(48, 48, 7, K.gold);
I.save = r(16, 14, 64, 68, K.blue2, 6) + p('M28 14 L64 14 L64 38 L28 38Z', K.cream) + r(50, 18, 8, 14, K.dark, 1) + r(24, 50, 48, 32, K.white, 3) + hl('M32 60 L64 60 M32 70 L58 70', K.stone2, 3);
I.load = p('M12 24 L38 24 L46 34 L82 34 L82 78 L12 78Z', K.gold2) + p('M12 40 L88 40 L80 78 L12 78Z', K.gold) + shine('M20 48 L76 48');
I.new = p('M12 84 Q48 68 84 84 L84 90 L12 90Z', K.bark) + ln('M48 80 L48 36', K.green2, 6) + p('M48 52 C28 52 16 38 18 20 C36 20 48 34 48 52Z', K.green) + p('M48 42 C66 40 80 26 78 10 C60 12 48 24 48 42Z', '#9bd46e');
I.guide = p('M12 18 L84 18 C88 18 90 20 90 24 L90 62 C90 66 88 68 84 68 L44 68 L24 84 L28 68 L12 68 C8 68 6 66 6 62 L6 24 C6 20 8 18 12 18Z', K.blue) + c(30, 43, 5, K.white) + c(48, 43, 5, K.white) + c(66, 43, 5, K.white);
I.sound = p('M14 36 L30 36 L52 18 L52 78 L30 60 L14 60Z', K.gold) + hl('M62 32 C72 40 72 56 62 64 M72 22 C88 36 88 60 72 74', K.cream, 5);
I.eye = p('M8 48 C24 22 72 22 88 48 C72 74 24 74 8 48Z', K.white) + c(48, 48, 16, K.blue) + c(48, 48, 7, K.black) + dot(53, 43, 3.5, '#fff');
I.info = c(48, 48, 36, K.blue) + r(43, 42, 10, 28, K.white, 4) + c(48, 28, 6, K.white);

// ── write ──
const svg = b => `<svg xmlns="http://www.w3.org/2000/svg" width="288" height="288" viewBox="0 0 96 96">${b}</svg>`;
let n = 0;
for (const [k, b] of Object.entries(I)) { fs.writeFileSync(path.join(root, `color_${k}.svg`), svg(b)); n++; }
// 겹침 검사: 같은 그림을 두 키가 나눠 쓰면 안 된다.
const seen = new Map();
for (const [k, b] of Object.entries(I)) { if (seen.has(b)) throw new Error(`same icon: ${k} = ${seen.get(b)}`); seen.set(b, k); }
console.log(`${n} icons written.`);
