'use strict';
// 지도: 땅 조각(지방)마다의 지형·주인·자원을 그리고, 그 위에 건물과 걸어 다니는 사람들을 올린다.
// 그림은 모두 여기서 도형으로 그린다(그림 파일 없음). 시뮬레이션의 난수와는 무관하다.
const GameMap = (() => {
  const TERRAIN = ['바다', '들', '숲', '언덕', '산', '초원'];
  const T_COL = [null, [150, 186, 104], [96, 150, 92], [176, 170, 112], [158, 154, 148], [204, 192, 128]];
  const RIVAL_COLS = ['#e4572e', '#8e5bd0', '#e0a100', '#12a3a3', '#d6478f', '#7a8b2a'];
  const US = '#2f6fe0';
  const RES = ['', '가축', '구리', '철', '향료', '석탄', '석유', '우라늄'];
  const CLAN_COLS = ['#c8553d', '#3d7cc8', '#4f9d5d', '#b07cc6', '#d99a2b', '#3aa6a6', '#b5536c', '#7a8f3a', '#5a6fc0', '#c27a3e'];
  const SKIN = ['#f1c9a5', '#e0ac7e', '#c98f62'];
  const HAIR = ['#2b2118', '#4a3524', '#111', '#6b4a2a'];
  const BIG = ['시대', '군장사회', '도시국가', '왕국', '제국', '몰락', '정복', '역병', '기근', '왕조 교체', '개척', '발견', '기술', '혁명', '민란', '농민 반란', '노예 반란',
    '종교 전쟁', '총파업', '석유 파동', '학살', '정변', '귀족 반란', '혼인 동맹', '화폐 가치 절하', '공황', '독립'];

  let shape = null, S = null, cv = null, ctx = null, base = null, cs = 14, dpr = 1;
  let cellsOf = [], sprites = new Map(), flashes = [], toasts = [], wars = [], lastOwners = null, baseSig = '';
  let vx0 = 0, vy0 = 0, vw = 48, vh = 30; // 보이는 범위: 뭍을 감싸는 네모에 바다를 조금 둘렀다
  let hoverProv = -1, hoverSprite = null, mouse = null, lastT = performance.now(), tipFn = null;

  const hash = (a, b, c = 0) => {
    let h = (Math.imul(a | 0, 374761393) + Math.imul(b | 0, 668265263) + Math.imul(c | 0, 1274126177)) | 0;
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    h ^= h >>> 16;
    return (h >>> 0) / 4294967296;
  };
  const ownerColor = o => o === 0 ? US : RIVAL_COLS[(o - 1) % RIVAL_COLS.length];
  const rgb = (c, d = 0) => `rgb(${Math.max(0, Math.min(255, c[0] + d)) | 0},${Math.max(0, Math.min(255, c[1] + d)) | 0},${Math.max(0, Math.min(255, c[2] + d)) | 0})`;
  const cellAt = (x, y) => (x < 0 || y < 0 || x >= shape.w || y >= shape.h) ? -1 : shape.cells[y * shape.w + x];
  const ownerOf = id => id < 0 ? -2 : S.lands[id][0];

  function init(canvas, showTip) {
    cv = canvas; ctx = cv.getContext('2d'); tipFn = showTip;
    cv.addEventListener('pointermove', ev => { const b = cv.getBoundingClientRect(); mouse = { x: (ev.clientX - b.left) / cs + vx0, y: (ev.clientY - b.top) / cs + vy0, cx: ev.clientX, cy: ev.clientY }; });
    cv.addEventListener('pointerleave', () => { mouse = null; hoverProv = -1; hoverSprite = null; tipFn(null); });
    addEventListener('resize', () => { baseSig = ''; });
    requestAnimationFrame(frame);
  }

  function setShape(m) {
    shape = m;
    cellsOf = m.provinces.map(() => []);
    let x0 = m.w, y0 = m.h, x1 = 0, y1 = 0;
    m.cells.forEach((id, i) => {
      if (id < 0) return;
      cellsOf[id].push(i);
      const x = i % m.w, y = (i / m.w) | 0;
      x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y);
    });
    vx0 = Math.max(0, x0 - 2); vy0 = Math.max(0, y0 - 2);
    vw = Math.min(m.w, x1 + 3) - vx0; vh = Math.min(m.h, y1 + 3) - vy0;
    sprites = new Map(); flashes = []; toasts = []; wars = []; lastOwners = null; baseSig = '';
  }

  function update(state) {
    S = state;
    if (!shape || shape.gameId !== S.gameId || S.lands.length !== shape.provinces.length) return;
    const owners = S.lands.map(l => l[0]);
    if (lastOwners) owners.forEach((o, i) => { if (o !== lastOwners[i]) flashes.push({ prov: i, t: performance.now(), col: o >= 0 ? ownerColor(o) : '#ffffff' }); });
    lastOwners = owners;
    syncSprites();
  }

  /** 새로 들어온 연대기에서 큰일은 지도 위에 띄운다. */
  function notify(entries) {
    if (!shape || !S) return;
    const now = performance.now();
    entries.forEach(e => {
      const m = /^【(.+?)】\s*(.*)$/s.exec(e.x);
      if (!m) return;
      if (m[1] === '전쟁' || m[1] === '총력전') {
        const foe = (S.rivals || []).find(v => e.x.includes(v.name));
        wars.push({ t: now, seat: foe && foe.seat >= 0 ? foe.seat : -1 });
      }
      if (BIG.includes(m[1])) toasts.push({ t: now, tag: m[1], text: m[2] });
    });
    if (toasts.length > 3) toasts = toasts.slice(-3);
    if (wars.length > 2) wars = wars.slice(-2);
  }

  // ───────── 사람들 ─────────
  function randomSpot(prov) {
    const list = cellsOf[prov];
    const i = list[(Math.random() * list.length) | 0];
    return [i % shape.w + 0.15 + Math.random() * 0.7, ((i / shape.w) | 0) + 0.15 + Math.random() * 0.7];
  }
  function nearSpot(prov, spread) {
    const p = shape.provinces[prov];
    for (let k = 0; k < 12; k++) {
      const x = p.x + 0.5 + (Math.random() - 0.5) * spread * 2, y = p.y + 0.5 + (Math.random() - 0.5) * spread * 2;
      if (cellAt(Math.floor(x), Math.floor(y)) === prov) return [x, y];
    }
    return [p.x + 0.5, p.y + 0.5];
  }
  /** 지방의 크기 등급: 그 땅에 사는 백성 수(코어의 LandPops)에서 나온다. 0 외딴집 … 4 큰 도시. */
  function tier(id) {
    const n = (S.landPop || [])[id] || 0;
    return n < 300 ? 0 : n < 3000 ? 1 : n < 30000 ? 2 : n < 200000 ? 3 : 4;
  }
  function syncSprites() {
    const mine = [], want = new Map();
    S.lands.forEach((l, i) => { if (l[0] === 0) mine.push(i); });
    if (!mine.length) mine.push(shape.capital);
    const tribe = S.stageIndex === 0;
    S.people.slice(0, 28).forEach(p => {
      const far = !tribe && !p.chief && mine.length > 1 && hash(p.id, 7) > 0.5;
      want.set('p' + p.id, { prov: far ? mine[(hash(p.id, 9) * mine.length) | 0] : shape.capital, person: p });
    });
    if (!tribe) {
      S.lands.forEach((l, i) => {
        if (l[0] < 0) return;
        const n = l[0] === 0 ? 1 + tier(i) : 2;
        for (let k = 0; k < n; k++) want.set(`c${i}_${k}`, { prov: i, owner: l[0], k });
      });
    }
    for (const key of [...sprites.keys()]) if (!want.has(key)) sprites.delete(key);
    want.forEach((w, key) => {
      let s = sprites.get(key);
      if (!s || s.prov !== w.prov) {
        const [x, y] = w.person && w.prov === shape.capital ? nearSpot(w.prov, tribe ? 1.6 : 2.4) : randomSpot(w.prov);
        s = { key, prov: w.prov, x, y, tx: x, ty: y, wait: Math.random() * 3, phase: Math.random() * 6, dir: 1 };
        sprites.set(key, s);
      }
      s.person = w.person || null; s.owner = w.owner; s.k = w.k;
    });
  }
  function moveSprite(s, dt) {
    const dx = s.tx - s.x, dy = s.ty - s.y, d = Math.hypot(dx, dy);
    if (d < 0.05) {
      s.moving = false;
      s.wait -= dt;
      if (s.wait <= 0) {
        const tribe = S.stageIndex === 0;
        [s.tx, s.ty] = s.person && s.prov === shape.capital && Math.random() < 0.7 ? nearSpot(s.prov, tribe ? 1.8 : 2.6) : randomSpot(s.prov);
        s.wait = 1 + Math.random() * 5;
      }
      return;
    }
    const v = (s.person ? 0.55 : 0.4) * dt;
    s.x += dx / d * Math.min(v, d); s.y += dy / d * Math.min(v, d);
    s.dir = dx >= 0 ? 1 : -1; s.moving = true; s.phase += dt * 9;
  }
  function drawPerson(s) {
    const p = s.person, px = s.x * cs, py = s.y * cs;
    const child = p && p.age < 14;
    const u = cs / 14 * (p ? (child ? 0.62 : 1) : 0.6);
    const sw = s.moving ? Math.sin(s.phase) * 1.6 * u : 0;
    ctx.fillStyle = 'rgba(0,0,0,0.22)';
    ctx.beginPath(); ctx.ellipse(px, py + 0.5 * u, 3.2 * u, 1.2 * u, 0, 0, 6.3); ctx.fill();
    // 다리
    ctx.strokeStyle = '#3a2f26'; ctx.lineWidth = Math.max(1, 1.3 * u); ctx.lineCap = 'round';
    ctx.beginPath(); ctx.moveTo(px - 1.1 * u, py - 3 * u); ctx.lineTo(px - 1.1 * u + sw, py);
    ctx.moveTo(px + 1.1 * u, py - 3 * u); ctx.lineTo(px + 1.1 * u - sw, py); ctx.stroke();
    // 몸
    let body;
    if (!p) body = s.owner === 0 ? ['#a08a6a', '#8c7b63', '#b39b78'][s.k % 3] : ownerColor(s.owner);
    else body = p.thrall ? '#8d8d8d' : CLAN_COLS[p.clan % CLAN_COLS.length];
    ctx.fillStyle = body;
    ctx.beginPath();
    if (p && !p.male) { ctx.moveTo(px - 1.6 * u, py - 8 * u); ctx.lineTo(px + 1.6 * u, py - 8 * u); ctx.lineTo(px + 2.8 * u, py - 2.4 * u); ctx.lineTo(px - 2.8 * u, py - 2.4 * u); }
    else ctx.rect(px - 2.1 * u, py - 8 * u, 4.2 * u, 5.4 * u);
    ctx.closePath(); ctx.fill();
    ctx.strokeStyle = 'rgba(0,0,0,0.45)'; ctx.lineWidth = Math.max(0.6, 0.7 * u); ctx.stroke();
    if (p && p.thrall) { ctx.strokeStyle = '#4a3a2a'; ctx.lineWidth = Math.max(1, u); ctx.beginPath(); ctx.moveTo(px - 2.2 * u, py - 5.4 * u); ctx.lineTo(px + 2.2 * u, py - 5.4 * u); ctx.stroke(); }
    // 머리
    const id = p ? p.id : s.prov * 7 + s.k;
    ctx.fillStyle = SKIN[(hash(id, 3) * SKIN.length) | 0];
    ctx.beginPath(); ctx.arc(px, py - 10 * u, 2.3 * u, 0, 6.3); ctx.fill();
    ctx.strokeStyle = 'rgba(0,0,0,0.45)'; ctx.lineWidth = Math.max(0.6, 0.7 * u); ctx.stroke();
    ctx.fillStyle = p && p.age >= 55 ? '#d9d6cf' : HAIR[(hash(id, 5) * HAIR.length) | 0];
    ctx.beginPath(); ctx.arc(px, py - 10.6 * u, 2.3 * u, Math.PI, 0); ctx.fill();
    if (p && !p.male) ctx.fillRect(px - 2.4 * u, py - 10.6 * u, 1.1 * u, 3 * u), ctx.fillRect(px + 1.3 * u, py - 10.6 * u, 1.1 * u, 3 * u);
    if (p && p.chief) {
      ctx.fillStyle = '#f2c230'; ctx.strokeStyle = '#7a5a00'; ctx.lineWidth = Math.max(0.6, 0.6 * u);
      ctx.beginPath(); ctx.moveTo(px - 2.6 * u, py - 12.2 * u); ctx.lineTo(px - 2.6 * u, py - 15 * u); ctx.lineTo(px - 1.2 * u, py - 13.6 * u); ctx.lineTo(px, py - 15.6 * u);
      ctx.lineTo(px + 1.2 * u, py - 13.6 * u); ctx.lineTo(px + 2.6 * u, py - 15 * u); ctx.lineTo(px + 2.6 * u, py - 12.2 * u); ctx.closePath(); ctx.fill(); ctx.stroke();
    }
    if (hoverSprite === s) { ctx.strokeStyle = '#fff'; ctx.lineWidth = 1.5; ctx.beginPath(); ctx.arc(px, py - 6 * u, 8.5 * u, 0, 6.3); ctx.stroke(); }
  }

  // ───────── 바탕(지형·국경·건물): 주인이나 시대가 바뀔 때만 다시 그린다 ─────────
  function ensureBase() {
    const wrap = cv.parentElement, W = wrap.clientWidth;
    const want = `${W}|${S.gameId}|${S.stageIndex}|${S.eraIndex}|${S.lands.map(l => l.join(',')).join(';')}|${S.rivals.map(v => v.seat + (v.trade ? 't' : '') + (v.allied ? 'a' : '')).join()}|${(S.bands || []).map(b => b.name + b.allied + (b.hostility > 0.6)).join()}|${S.institutions.filter(d => d.on).length}|${S.lands.map((l, i) => l[0] === 0 ? tier(i) : 0).join('')}`;
    if (want === baseSig) return;
    baseSig = want;
    dpr = Math.min(2, devicePixelRatio || 1);
    cs = Math.max(7, Math.min(Math.floor(W / vw), Math.floor(innerHeight * 0.46 / vh)));
    const w = cs * vw, h = cs * vh;
    cv.style.width = w + 'px'; cv.style.height = h + 'px';
    cv.width = w * dpr; cv.height = h * dpr;
    base = document.createElement('canvas'); base.width = cv.width; base.height = cv.height;
    const g = base.getContext('2d');
    g.setTransform(dpr, 0, 0, dpr, -vx0 * cs * dpr, -vy0 * cs * dpr);
    drawTerrain(g); drawBorders(g); drawRoutes(g); drawPlaces(g);
  }

  function drawTerrain(g) {
    const { w, h } = shape;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const id = cellAt(x, y), j = (hash(x, y, 1) - 0.5) * 12;
      if (id < 0) {
        const shore = cellAt(x + 1, y) >= 0 || cellAt(x - 1, y) >= 0 || cellAt(x, y + 1) >= 0 || cellAt(x, y - 1) >= 0
          || cellAt(x + 1, y + 1) >= 0 || cellAt(x - 1, y - 1) >= 0 || cellAt(x + 1, y - 1) >= 0 || cellAt(x - 1, y + 1) >= 0;
        g.fillStyle = shore ? rgb([126, 190, 214], j * 0.5) : rgb([84, 150, 196], j * 0.6);
      } else g.fillStyle = rgb(T_COL[shape.provinces[id].terrain], j);
      g.fillRect(x * cs, y * cs, cs + 0.5, cs + 0.5);
    }
    // 주인의 빛깔을 엷게 덧씌운다.
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const o = ownerOf(cellAt(x, y));
      if (o < 0) continue;
      g.globalAlpha = o === 0 ? 0.2 : 0.26; g.fillStyle = ownerColor(o);
      g.fillRect(x * cs, y * cs, cs + 0.5, cs + 0.5);
    }
    g.globalAlpha = 1;
    // 물결, 나무, 봉우리, 풀, 밭.
    const farms = S.institutions.some(d => d.key === 'agriculture' && d.on);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const id = cellAt(x, y), r = hash(x, y, 2), ox = (x + 0.2 + hash(x, y, 3) * 0.6) * cs, oy = (y + 0.3 + hash(x, y, 4) * 0.6) * cs;
      if (id < 0) {
        if (r < 0.1) { g.strokeStyle = 'rgba(255,255,255,0.35)'; g.lineWidth = 1; g.beginPath(); g.arc(ox, oy, cs * 0.28, Math.PI * 1.15, Math.PI * 1.85); g.stroke(); }
        continue;
      }
      const p = shape.provinces[id], o = S.lands[id][0];
      if (p.terrain === 2 && r < 0.8) tree(g, ox, oy, cs * (0.34 + hash(x, y, 5) * 0.14));
      else if (p.terrain === 4 && r < 0.62) peak(g, ox, oy, cs * (0.6 + hash(x, y, 5) * 0.35));
      else if (p.terrain === 3 && r < 0.45) { g.strokeStyle = 'rgba(96,88,48,0.55)'; g.lineWidth = 1.2; g.beginPath(); g.arc(ox, oy, cs * 0.34, Math.PI, 0); g.stroke(); }
      else if (p.terrain === 5 && r < 0.35) grass(g, ox, oy, 'rgba(120,104,48,0.7)');
      else if (p.terrain === 1) {
        if (o >= 0 && farms && r < 0.3) field(g, x, y, hash(x, y, 6));
        else if (r > 0.86) grass(g, ox, oy, 'rgba(70,110,50,0.6)');
        else if (r > 0.82) tree(g, ox, oy, cs * 0.3);
      }
    }
  }
  function tree(g, x, y, s) {
    g.fillStyle = '#5b4126'; g.fillRect(x - s * 0.12, y - s * 0.2, s * 0.24, s * 0.5);
    g.fillStyle = '#2f6b3c'; g.beginPath(); g.moveTo(x, y - s * 1.5); g.lineTo(x + s * 0.7, y - s * 0.15); g.lineTo(x - s * 0.7, y - s * 0.15); g.closePath(); g.fill();
    g.fillStyle = '#3f8a4c'; g.beginPath(); g.moveTo(x, y - s * 1.5); g.lineTo(x + s * 0.2, y - s * 0.15); g.lineTo(x - s * 0.7, y - s * 0.15); g.closePath(); g.fill();
  }
  function peak(g, x, y, s) {
    g.fillStyle = '#7d7a75'; g.beginPath(); g.moveTo(x, y - s); g.lineTo(x + s * 0.8, y + s * 0.2); g.lineTo(x - s * 0.8, y + s * 0.2); g.closePath(); g.fill();
    g.fillStyle = '#a9a6a0'; g.beginPath(); g.moveTo(x, y - s); g.lineTo(x + s * 0.1, y + s * 0.2); g.lineTo(x - s * 0.8, y + s * 0.2); g.closePath(); g.fill();
    g.fillStyle = '#f4f4f1'; g.beginPath(); g.moveTo(x, y - s); g.lineTo(x + s * 0.27, y - s * 0.6); g.lineTo(x + s * 0.05, y - s * 0.7); g.lineTo(x - s * 0.12, y - s * 0.55); g.lineTo(x - s * 0.27, y - s * 0.6); g.closePath(); g.fill();
  }
  function grass(g, x, y, col) {
    g.strokeStyle = col; g.lineWidth = 1; g.beginPath();
    g.moveTo(x - 2, y); g.lineTo(x - 3, y - 4); g.moveTo(x, y); g.lineTo(x, y - 5); g.moveTo(x + 2, y); g.lineTo(x + 3, y - 4); g.stroke();
  }
  function field(g, x, y, r) {
    g.fillStyle = r < 0.5 ? 'rgba(214,190,96,0.75)' : 'rgba(170,196,96,0.75)';
    g.fillRect(x * cs + 1.5, y * cs + 1.5, cs - 3, cs - 3);
    g.strokeStyle = 'rgba(110,90,40,0.45)'; g.lineWidth = 1; g.beginPath();
    for (let k = 1; k < 4; k++) { const d = k * (cs - 3) / 4 + 1.5; if (r < 0.5) { g.moveTo(x * cs + 1.5, y * cs + d); g.lineTo((x + 1) * cs - 1.5, y * cs + d); } else { g.moveTo(x * cs + d, y * cs + 1.5); g.lineTo(x * cs + d, (y + 1) * cs - 1.5); } }
    g.stroke();
  }

  function drawBorders(g) {
    const { w, h } = shape, inset = Math.max(2, cs * 0.2);
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const id = cellAt(x, y);
      if (id < 0) continue;
      const o = ownerOf(id), px = x * cs, py = y * cs;
      [[1, 0], [-1, 0], [0, 1], [0, -1]].forEach(([dx, dy]) => {
        const n = cellAt(x + dx, y + dy);
        if (n === id) return;
        const x1 = dx > 0 ? px + cs : px, y1 = dy > 0 ? py + cs : py, x2 = dx !== 0 ? x1 : px + cs, y2 = dy !== 0 ? y1 : py + cs;
        if (n < 0) { g.strokeStyle = 'rgba(70,60,40,0.55)'; g.lineWidth = 1.2; }
        else if (ownerOf(n) !== o) { g.strokeStyle = 'rgba(25,25,25,0.6)'; g.lineWidth = 1.4; }
        else { g.strokeStyle = 'rgba(40,40,30,0.16)'; g.lineWidth = 1; }
        g.beginPath(); g.moveTo(x1, y1); g.lineTo(x2, y2); g.stroke();
        // 국경 안쪽에 주인의 빛깔 띠.
        if (o >= 0 && (n < 0 || ownerOf(n) !== o)) {
          g.fillStyle = ownerColor(o); g.globalAlpha = 0.75;
          if (dx > 0) g.fillRect(px + cs - inset, py, inset, cs); else if (dx < 0) g.fillRect(px, py, inset, cs);
          else if (dy > 0) g.fillRect(px, py + cs - inset, cs, inset); else g.fillRect(px, py, cs, inset);
          g.globalAlpha = 1;
        }
      });
    }
  }

  const center = id => { const p = shape.provinces[id]; return [(p.x + 0.5) * cs, (p.y + 0.5) * cs]; };

  function drawRoutes(g) {
    const [cx, cy] = center(shape.capital);
    S.rivals.forEach(v => {
      if (v.seat < 0 || !(v.trade || v.allied)) return;
      const [x, y] = center(v.seat);
      g.strokeStyle = v.allied ? 'rgba(255,255,255,0.85)' : 'rgba(120,80,20,0.75)'; g.lineWidth = v.allied ? 2 : 1.5; g.setLineDash(v.allied ? [] : [5, 4]);
      g.beginPath(); g.moveTo(cx, cy); g.quadraticCurveTo((cx + x) / 2, (cy + y) / 2 - cs * 1.5, x, y); g.stroke();
      g.setLineDash([]);
    });
  }

  function house(g, x, y, s, wall, roof) {
    g.fillStyle = wall; g.fillRect(x - s * 0.5, y - s * 0.6, s, s * 0.6);
    g.fillStyle = roof; g.beginPath(); g.moveTo(x - s * 0.65, y - s * 0.6); g.lineTo(x, y - s * 1.2); g.lineTo(x + s * 0.65, y - s * 0.6); g.closePath(); g.fill();
    g.strokeStyle = 'rgba(0,0,0,0.4)'; g.lineWidth = 0.8; g.strokeRect(x - s * 0.5, y - s * 0.6, s, s * 0.6);
  }
  function tent(g, x, y, s, col) {
    g.fillStyle = col; g.beginPath(); g.moveTo(x, y - s); g.lineTo(x + s * 0.7, y); g.lineTo(x - s * 0.7, y); g.closePath(); g.fill();
    g.strokeStyle = 'rgba(0,0,0,0.45)'; g.lineWidth = 0.8; g.stroke();
    g.fillStyle = '#3a2a1c'; g.beginPath(); g.moveTo(x, y - s * 0.5); g.lineTo(x + s * 0.18, y); g.lineTo(x - s * 0.18, y); g.closePath(); g.fill();
  }
  function tower(g, x, y, wd, ht, col, flag) {
    g.fillStyle = col; g.fillRect(x - wd / 2, y - ht, wd, ht);
    g.strokeStyle = 'rgba(0,0,0,0.5)'; g.lineWidth = 0.8; g.strokeRect(x - wd / 2, y - ht, wd, ht);
    const m = wd / 3; g.fillRect(x - wd / 2, y - ht - m * 0.8, m * 0.8, m * 0.8); g.fillRect(x + wd / 2 - m * 0.8, y - ht - m * 0.8, m * 0.8, m * 0.8);
    if (flag) {
      g.strokeStyle = '#2a2a2a'; g.lineWidth = 1; g.beginPath(); g.moveTo(x, y - ht); g.lineTo(x, y - ht - wd * 1.3); g.stroke();
      g.fillStyle = flag; g.beginPath(); g.moveTo(x, y - ht - wd * 1.3); g.lineTo(x + wd * 0.9, y - ht - wd); g.lineTo(x, y - ht - wd * 0.7); g.closePath(); g.fill();
    }
  }
  function block(g, x, y, wd, ht, col) {
    g.fillStyle = col; g.fillRect(x - wd / 2, y - ht, wd, ht);
    g.strokeStyle = 'rgba(0,0,0,0.45)'; g.lineWidth = 0.8; g.strokeRect(x - wd / 2, y - ht, wd, ht);
    g.fillStyle = 'rgba(255,236,150,0.9)';
    for (let yy = y - ht + 2; yy < y - 2; yy += 3.5) for (let xx = x - wd / 2 + 1.5; xx < x + wd / 2 - 1.5; xx += 3) g.fillRect(xx, yy, 1.4, 1.6);
  }
  /** 마을에서 도시까지. level: 0 천막, 1 움집 마을, 2 성벽 도시, 3 성, 4 큰 성. era가 근대를 넘으면 굴뚝과 높은 집이 선다. */
  function town(g, x, y, level, era, col) {
    const u = cs / 14;
    g.fillStyle = 'rgba(0,0,0,0.18)'; g.beginPath(); g.ellipse(x, y + 2 * u, (8 + 3 * level) * u, 3 * u, 0, 0, 6.3); g.fill();
    if (level === 0) {
      tent(g, x - 7 * u, y, 8 * u, '#c9a36a'); tent(g, x + 7 * u, y - 1 * u, 8 * u, '#b98f58'); tent(g, x, y + 3 * u, 9 * u, '#d6b47c');
      g.fillStyle = '#e8622a'; g.beginPath(); g.arc(x, y - 5 * u, 1.8 * u, 0, 6.3); g.fill();
      return;
    }
    if (era >= 5) {
      block(g, x - 8 * u, y, 6 * u, 13 * u, '#8f9aa8'); block(g, x + 7 * u, y, 6 * u, 16 * u, '#7b8796'); block(g, x - 1 * u, y + 2 * u, 7 * u, 21 * u, '#a3adb9');
      if (level >= 3) tower(g, x + 13 * u, y + 1 * u, 3.5 * u, 9 * u, '#b9b3a6', col);
      return;
    }
    if (level === 1) {
      g.strokeStyle = '#6b4a2a'; g.lineWidth = 1.5 * u; g.beginPath(); g.arc(x, y - 2 * u, 11 * u, Math.PI * 0.05, Math.PI * 0.95); g.stroke();
      house(g, x - 6 * u, y - 1 * u, 7 * u, '#c9ab7c', '#8a5a2c'); house(g, x + 6 * u, y - 2 * u, 7 * u, '#c9ab7c', '#8a5a2c'); house(g, x, y + 3 * u, 9 * u, '#d8bd8e', '#9a6430');
      return;
    }
    const stone = '#c9c3b4', dark = '#aaa392';
    if (level === 2) {
      house(g, x - 5 * u, y - 3 * u, 6 * u, '#e2d3ae', '#a5482e'); house(g, x + 4 * u, y - 4 * u, 6 * u, '#e2d3ae', '#a5482e');
      g.fillStyle = stone; g.fillRect(x - 11 * u, y - 3 * u, 22 * u, 5 * u); g.strokeStyle = 'rgba(0,0,0,0.5)'; g.lineWidth = 0.8; g.strokeRect(x - 11 * u, y - 3 * u, 22 * u, 5 * u);
      for (let k = -10; k <= 8; k += 4) g.fillRect(x + k * u, y - 4.6 * u, 2 * u, 1.8 * u);
      g.fillStyle = '#4a3622'; g.fillRect(x - 1.5 * u, y - 1.5 * u, 3 * u, 3.5 * u);
      tower(g, x - 11 * u, y + 2 * u, 4 * u, 9 * u, dark, col);
      tower(g, x + 11 * u, y + 2 * u, 4 * u, 9 * u, dark, null);
      return;
    }
    // 성: 가운데 높은 탑과 양옆의 탑.
    const big = level >= 4 ? 1.25 : 1;
    g.fillStyle = stone; g.fillRect(x - 12 * u * big, y - 4 * u, 24 * u * big, 6 * u); g.strokeStyle = 'rgba(0,0,0,0.5)'; g.lineWidth = 0.8; g.strokeRect(x - 12 * u * big, y - 4 * u, 24 * u * big, 6 * u);
    tower(g, x - 12 * u * big, y + 2 * u, 4.5 * u, 11 * u, dark, null);
    tower(g, x + 12 * u * big, y + 2 * u, 4.5 * u, 11 * u, dark, null);
    tower(g, x, y + 2 * u, 7 * u * big, 16 * u * big, stone, col);
    if (level >= 4) {
      tower(g, x - 6 * u * big, y + 2 * u, 3.5 * u, 13 * u, dark, col); tower(g, x + 6 * u * big, y + 2 * u, 3.5 * u, 13 * u, dark, col);
      g.fillStyle = '#e0b83a'; g.beginPath(); g.arc(x, y - 14 * u * big - 4 * u, 3 * u, Math.PI, 0); g.fill();
    }
    g.fillStyle = '#4a3622'; g.fillRect(x - 1.6 * u, y - 1.5 * u, 3.2 * u, 3.5 * u);
    if (era >= 4) { g.fillStyle = '#7a4a3a'; g.fillRect(x + 16 * u * big, y - 9 * u, 3 * u, 11 * u); g.fillStyle = '#8f6a5a'; g.fillRect(x + 14 * u * big, y - 3 * u, 9 * u, 5 * u); }
  }
  function resIcon(g, x, y, kind) {
    const r = cs * 0.42;
    g.fillStyle = 'rgba(255,253,245,0.92)'; g.strokeStyle = 'rgba(30,30,30,0.7)'; g.lineWidth = 1;
    g.beginPath(); g.arc(x, y, r, 0, 6.3); g.fill(); g.stroke();
    const s = r * 0.62;
    if (kind === 1) { g.fillStyle = '#7a5230'; g.beginPath(); g.ellipse(x - s * 0.1, y, s * 0.8, s * 0.45, 0, 0, 6.3); g.fill(); g.beginPath(); g.arc(x + s * 0.75, y - s * 0.45, s * 0.32, 0, 6.3); g.fill(); g.fillRect(x - s * 0.7, y + s * 0.2, s * 0.22, s * 0.7); g.fillRect(x + s * 0.3, y + s * 0.2, s * 0.22, s * 0.7); }
    else if (kind === 2 || kind === 3) { g.fillStyle = kind === 2 ? '#c8763a' : '#6f7780'; g.beginPath(); g.moveTo(x - s * 0.6, y - s * 0.4); g.lineTo(x + s * 0.6, y - s * 0.4); g.lineTo(x + s, y + s * 0.5); g.lineTo(x - s, y + s * 0.5); g.closePath(); g.fill(); g.fillStyle = 'rgba(255,255,255,0.4)'; g.fillRect(x - s * 0.5, y - s * 0.3, s, s * 0.2); }
    else if (kind === 4) { g.fillStyle = '#b3261e'; [[-0.4, 0.2], [0.4, 0.3], [0, -0.4]].forEach(([a, b]) => { g.beginPath(); g.arc(x + a * s, y + b * s, s * 0.42, 0, 6.3); g.fill(); }); g.strokeStyle = '#2f6b3c'; g.lineWidth = 1.2; g.beginPath(); g.moveTo(x, y - s * 0.8); g.lineTo(x + s * 0.5, y - s * 1.1); g.stroke(); }
    else if (kind === 5) { g.fillStyle = '#1c1c1c'; [[-0.45, 0.3], [0.45, 0.3], [0, -0.3]].forEach(([a, b]) => { g.beginPath(); g.arc(x + a * s, y + b * s, s * 0.5, 0, 6.3); g.fill(); }); }
    else if (kind === 6) { g.fillStyle = '#16161a'; g.beginPath(); g.moveTo(x, y - s); g.quadraticCurveTo(x + s, y + s * 0.3, x, y + s * 0.9); g.quadraticCurveTo(x - s, y + s * 0.3, x, y - s); g.fill(); }
    else if (kind === 7) { g.fillStyle = '#e7d21f'; g.beginPath(); g.arc(x, y, s, 0, 6.3); g.fill(); g.fillStyle = '#1c1c1c'; for (let k = 0; k < 3; k++) { const a = k * 2.094 - 1.571; g.beginPath(); g.moveTo(x, y); g.arc(x, y, s * 0.92, a - 0.5, a + 0.5); g.closePath(); g.fill(); } g.fillStyle = '#e7d21f'; g.beginPath(); g.arc(x, y, s * 0.25, 0, 6.3); g.fill(); }
  }
  function label(g, text, x, y, bold, col) {
    g.font = `${bold ? '600 ' : ''}${Math.max(10, Math.min(13, cs * 0.8)) | 0}px system-ui, "Segoe UI", "Malgun Gothic", sans-serif`;
    g.textAlign = 'center'; g.textBaseline = 'top'; g.lineJoin = 'round';
    g.strokeStyle = 'rgba(255,255,255,0.9)'; g.lineWidth = 3; g.strokeText(text, x, y);
    g.fillStyle = col || '#1b1b1b'; g.fillText(text, x, y);
  }

  function drawPlaces(g) {
    const era = S.eraIndex, seats = new Map(S.rivals.filter(v => v.seat >= 0).map(v => [v.seat, v]));
    const order = shape.provinces.map(p => p.id).sort((a, b) => shape.provinces[a].y - shape.provinces[b].y);
    order.forEach(id => {
      const o = S.lands[id][0], [x, y] = center(id), u = cs / 14;
      if (o >= 0) {
        if (id === shape.capital) town(g, x, y, S.stageIndex, era, US);
        else if (seats.has(id)) town(g, x, y, Math.min(4, 2 + (seats.get(id).strength > 1 ? 1 : 0) + (S.stageIndex >= 4 ? 1 : 0)), era, ownerColor(o));
        else {
          // 사는 사람이 많은 땅일수록 집이 많다(우리 땅은 코어가 센 백성 수, 남의 땅은 두 채).
          const n = o === 0 ? 1 + tier(id) : 2, roof = o === 0 ? '#8a5a2c' : ownerColor(o);
          const at = [[-4, 0], [3, 2], [-1, -4], [7, -2], [-8, 3]];
          for (let k = Math.min(n, at.length) - 1; k >= 0; k--) {
            const hx = x + at[k][0] * u, hy = y + at[k][1] * u;
            if (era >= 5) block(g, hx, hy, 5 * u, (8 + 3 * ((k + id) % 3)) * u, k % 2 ? '#a3adb9' : '#8f9aa8');
            else house(g, hx, hy, 6 * u, '#d8c49a', roof);
          }
        }
      }
      if (S.lands[id][1] > 0) resIcon(g, x + cs * 1.15, y - cs * 0.75, S.lands[id][1]);
    });
    // 부족 시절의 이웃 무리: 도성 둘레의 땅에 천막을 친다.
    const cap = shape.provinces[shape.capital];
    (S.bands || []).forEach((b, i) => {
      const [x, y] = center(cap.adj[i % cap.adj.length]), u = cs / 14;
      const col = b.allied ? '#7fb069' : b.hostility > 0.6 ? '#c8553d' : '#a8997e';
      tent(g, x - 4 * u, y, 7 * u, col); tent(g, x + 4 * u, y + 2 * u, 7 * u, col);
      label(g, b.name, x, y + 4 * u, false, b.hostility > 0.6 && !b.allied ? '#8c1f14' : '#3a3a3a');
    });
    order.forEach(id => {
      const o = S.lands[id][0], [x, y] = center(id), p = shape.provinces[id];
      if (id === shape.capital) label(g, p.name, x, y + cs * 0.45, true, '#123a85');
      else if (seats.has(id)) label(g, seats.get(id).name, x, y + cs * 0.45, true, '#2a2a2a');
      else if (o === 0) label(g, p.name, x, y + cs * 0.4, false, '#123a85');
    });
  }

  // ───────── 한 장면 ─────────
  function frame(now) {
    requestAnimationFrame(frame);
    const dt = Math.min(0.1, (now - lastT) / 1000); lastT = now;
    if (!shape || !S || !cv || shape.gameId !== S.gameId || S.lands.length !== shape.provinces.length) return;
    ensureBase();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.drawImage(base, 0, 0);
    ctx.setTransform(dpr, 0, 0, dpr, -vx0 * cs * dpr, -vy0 * cs * dpr);
    const W = cs * shape.w, Hh = cs * shape.h;
    // 철마다 빛이 다르다.
    const tint = [null, 'rgba(255,240,170,0.06)', 'rgba(214,120,40,0.10)', 'rgba(235,242,250,0.30)'][S.seasonIndex];
    if (tint) { ctx.fillStyle = tint; ctx.fillRect(0, 0, W, Hh); }

    // 주인이 바뀐 땅은 잠깐 번쩍인다.
    flashes = flashes.filter(f => now - f.t < 2200);
    flashes.forEach(f => {
      const a = 1 - (now - f.t) / 2200;
      ctx.fillStyle = f.col; ctx.globalAlpha = 0.55 * a * (0.6 + 0.4 * Math.sin(now / 90));
      cellsOf[f.prov].forEach(i => ctx.fillRect((i % shape.w) * cs, ((i / shape.w) | 0) * cs, cs, cs));
      ctx.globalAlpha = 1;
    });

    // 가리킨 땅의 테두리.
    hoverProv = -1; hoverSprite = null;
    if (mouse) {
      let best = 0.9;
      sprites.forEach(s => { if (!s.person) return; const d = Math.hypot(s.x - mouse.x, s.y - 0.45 - mouse.y); if (d < best) { best = d; hoverSprite = s; } });
      hoverProv = cellAt(Math.floor(mouse.x), Math.floor(mouse.y));
    }
    if (hoverProv >= 0) {
      ctx.strokeStyle = '#fff'; ctx.lineWidth = 2; ctx.beginPath();
      cellsOf[hoverProv].forEach(i => {
        const x = i % shape.w, y = (i / shape.w) | 0;
        if (cellAt(x + 1, y) !== hoverProv) { ctx.moveTo((x + 1) * cs, y * cs); ctx.lineTo((x + 1) * cs, (y + 1) * cs); }
        if (cellAt(x - 1, y) !== hoverProv) { ctx.moveTo(x * cs, y * cs); ctx.lineTo(x * cs, (y + 1) * cs); }
        if (cellAt(x, y + 1) !== hoverProv) { ctx.moveTo(x * cs, (y + 1) * cs); ctx.lineTo((x + 1) * cs, (y + 1) * cs); }
        if (cellAt(x, y - 1) !== hoverProv) { ctx.moveTo(x * cs, y * cs); ctx.lineTo((x + 1) * cs, y * cs); }
      });
      ctx.stroke();
    }

    // 굴뚝 연기.
    if (S.eraIndex >= 4 && S.stageIndex >= 1 && S.institutions.some(d => d.key === 'factories' && d.on)) {
      const [x, y] = center(shape.capital), u = cs / 14;
      for (let k = 0; k < 4; k++) {
        const ph = ((now / 2600) + k / 4) % 1;
        ctx.fillStyle = `rgba(70,70,70,${0.4 * (1 - ph)})`;
        ctx.beginPath(); ctx.arc(x + (S.eraIndex >= 5 ? 12 : 18) * u + ph * 9 * u, y - (12 + ph * 16) * u, (2 + ph * 4) * u, 0, 6.3); ctx.fill();
      }
    }

    const list = [...sprites.values()];
    list.forEach(s => moveSprite(s, dt));
    list.sort((a, b) => a.y - b.y).forEach(drawPerson);
    { const [x, y] = center(shape.capital); label(ctx, shape.provinces[shape.capital].name, x, y + cs * 0.45, true, '#123a85'); }

    // 전쟁: 도성과 적의 도성 사이에 칼이 부딪친다.
    wars = wars.filter(w => now - w.t < 2800);
    wars.forEach(w => {
      const [ax, ay] = center(shape.capital), [bx, by] = w.seat >= 0 ? center(w.seat) : [ax + cs * 2, ay - cs * 2];
      const x = (ax + bx) / 2, y = (ay + by) / 2, a = 1 - (now - w.t) / 2800, k = cs * 0.9 * (1 + 0.15 * Math.sin(now / 80));
      ctx.globalAlpha = Math.min(1, a * 2);
      ctx.strokeStyle = 'rgba(200,40,30,0.8)'; ctx.lineWidth = 2; ctx.setLineDash([4, 4]); ctx.beginPath(); ctx.moveTo(ax, ay); ctx.lineTo(bx, by); ctx.stroke(); ctx.setLineDash([]);
      ctx.fillStyle = '#fff'; ctx.beginPath(); ctx.arc(x, y, k * 1.1, 0, 6.3); ctx.fill(); ctx.strokeStyle = '#b3261e'; ctx.lineWidth = 2; ctx.stroke();
      ctx.strokeStyle = '#3a3a3a'; ctx.lineWidth = 2.4; ctx.lineCap = 'round'; ctx.beginPath();
      ctx.moveTo(x - k * 0.6, y + k * 0.6); ctx.lineTo(x + k * 0.6, y - k * 0.6); ctx.moveTo(x + k * 0.6, y + k * 0.6); ctx.lineTo(x - k * 0.6, y - k * 0.6); ctx.stroke();
      ctx.globalAlpha = 1;
    });

    // 큰일 알림.
    toasts = toasts.filter(t => now - t.t < 5200);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.font = '13px system-ui, "Segoe UI", "Malgun Gothic", sans-serif'; ctx.textBaseline = 'middle'; ctx.textAlign = 'left';
    toasts.forEach((t, i) => {
      const a = Math.min(1, (5200 - (now - t.t)) / 600, (now - t.t) / 200), max = cs * vw - 40;
      let text = `【${t.tag}】 ${t.text}`;
      while (ctx.measureText(text).width > max - 24 && text.length > 8) text = text.slice(0, -4) + '…';
      const tw = ctx.measureText(text).width + 24, x = (cs * vw - tw) / 2, y = 10 + i * 32;
      ctx.globalAlpha = a;
      ctx.fillStyle = 'rgba(20,20,20,0.82)'; ctx.beginPath(); ctx.roundRect ? ctx.roundRect(x, y, tw, 26, 6) : ctx.rect(x, y, tw, 26); ctx.fill();
      ctx.fillStyle = '#fff'; ctx.fillText(text, x + 12, y + 13.5);
      ctx.globalAlpha = 1;
    });

    showHover();
  }

  function showHover() {
    if (!mouse) return;
    if (hoverSprite) {
      const p = hoverSprite.person, rows = [];
      rows.push(['h', `${p.name} ${p.male ? '♂' : '♀'} · ${p.age}세`]);
      rows.push(['r', p.chief ? `${S.rulerTitle}` : p.thrall ? '남의 종' : '지위 ' + p.status]);
      const top = p.traits.map((v, i) => [v, S.traitNames[i]]).sort((a, b) => b[0] - a[0]).slice(0, 2).map(t => `${t[1]} ${t[0]}`).join(' · ');
      rows.push(['r', top]);
      if (p.outsider) rows.push(['r', '바깥에서 들어온 핏줄']);
      if (p.grudge) rows.push(['r', '원한: ' + p.grudge]);
      tipFn(rows, mouse.cx, mouse.cy);
      return;
    }
    if (hoverProv < 0) { tipFn(null); return; }
    const p = shape.provinces[hoverProv], [o, res] = S.lands[hoverProv];
    const who = o < 0 ? '주인 없는 땅' : o === 0 ? (hoverProv === shape.capital ? '우리 땅 · 도성' : '우리 땅') : ((S.rivals.find(v => v.id === o) || {}).name || '이웃 나라');
    const rows = [['h', `${p.name} · ${TERRAIN[p.terrain]}${p.coastal ? ' · 바닷가' : ''}`], ['r', who], ['r', `기름짐 ${p.fertility.toFixed(2)}`]];
    const lp = (S.landPop || [])[hoverProv] || 0;
    if (o === 0 && lp > 0) rows.push(['r', `사는 백성 약 ${Math.round(lp).toLocaleString('ko-KR')}명`]);
    if (res > 0) rows.push(['r', `나는 것: ${RES[res]}`]);
    tipFn(rows, mouse.cx, mouse.cy);
  }

  /** 지도 아래의 범례: 누구의 빛깔인가. */
  function legend() {
    if (!S) return [];
    const items = [[US, S.stageIndex === 0 ? '우리 무리' : '우리 나라']];
    S.rivals.forEach(v => items.push([ownerColor(v.id), `${v.name} (땅 ${v.lands})`]));
    return items;
  }

  return { init, setShape, update, notify, legend };
})();
