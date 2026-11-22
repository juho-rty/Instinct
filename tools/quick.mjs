// 빠른 균형 점검: 씨앗 여러 개 × 설정 여러 개를 돌려 승리·몰락·멸종·죽음 원인·시대 진입을 요약한다.
//   node tools/quick.mjs [씨앗 수=30] [햇수=500] [설정=player,auto,learning,conquest,tyranny,terror,commerce,hard,plain]
// 먼저 dotnet build src/Instinct.Runner -c Release.
import { spawn } from 'node:child_process';
import fs from 'node:fs'; import path from 'node:path'; import os from 'node:os'; import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const seeds = +(process.argv[2] ?? 30), years = +(process.argv[3] ?? 500);
const all = { plain: [], auto: ['--auto'], player: ['--player'], easy: ['--player', '--difficulty', 'easy'], hard: ['--player', '--difficulty', 'hard'],
  conquest: ['--aim', 'conquest'], commerce: ['--aim', 'commerce'], terror: ['--aim', 'terror'], tyranny: ['--aim', 'tyranny'], learning: ['--aim', 'learning'] };
const names = (process.argv[4] ?? 'player,auto,learning,conquest,tyranny,terror,commerce,hard,plain').split(',');
const rel = path.join(root, 'src/Instinct.Runner/bin/Release');
const dll = path.join(rel, fs.readdirSync(rel)[0], 'Instinct.Runner.dll');
const jobs = []; for (const n of names) for (let s = 1; s <= seeds; s++) jobs.push([n, s]);
const res = {}; let ji = 0, live = 0;
const run = ([n, s]) => new Promise(ok => {
  const p = spawn('C:/Program Files/dotnet/dotnet.exe', [dll, '--seed', s, '--years', years, ...all[n]]);
  const ch = []; p.stdout.on('data', d => ch.push(d));
  p.on('close', () => {
    const t = Buffer.concat(ch).toString('utf8').split('\r').join('');
    const end = t.slice(t.lastIndexOf('═ 씨앗'));
    const wins = [...(end.match(/승리: (.*)/)?.[1] ?? '').matchAll(/([^,(]+)\((\d+)년\)/g)].map(m => [m[1].trim(), +m[2]]);
    const falls = [...(end.match(/\n몰락: (.*)/)?.[1] ?? '').matchAll(/(\d+)년 ([^,]+)/g)].map(m => m[2].trim());
    const deaths = Object.fromEntries([...(end.match(/죽음·이탈: (.*)/)?.[1] ?? '').matchAll(/(\S+) (\d+)/g)].map(m => [m[1], +m[2]]));
    const era = {}; for (const m of t.matchAll(/\[(\d+)년[^\]]*\] 【시대】 (\S+?)가 열렸다/g)) era[m[2]] ??= +m[1];
    const extinct = /아무도 남지 않았다/.test(t);
    (res[n] ??= []).push({ s, wins, falls, deaths, era, extinct, top: end.match(/\n(\S+)\s\(최고/)?.[1] ?? '?' });
    ok();
  });
});
await new Promise(done => {
  const next = () => { if (ji >= jobs.length) { if (live === 0) done(); return; } live++; run(jobs[ji++]).then(() => { live--; next(); }); };
  for (let i = 0; i < Math.max(2, os.cpus().length - 2); i++) next();
});
const med = a => { if (!a.length) return '-'; a = [...a].sort((x, y) => x - y); return a[a.length >> 1]; };
for (const n of names) {
  const r = res[n], N = r.length, wc = {}, wy = [], fc = {}, dc = {}, eras = {};
  for (const x of r) {
    for (const [w, y] of x.wins) { wc[w] = (wc[w] ?? 0) + 1; }
    if (x.wins.length) wy.push(Math.min(...x.wins.map(w => w[1])));
    for (const f of x.falls) fc[f] = (fc[f] ?? 0) + 1;
    for (const [k, v] of Object.entries(x.deaths)) dc[k] = (dc[k] ?? 0) + v;
    for (const [k, v] of Object.entries(x.era)) (eras[k] ??= []).push(v);
  }
  const dt = Object.values(dc).reduce((a, b) => a + b, 0) || 1;
  console.log(`\n[${n}] ${N}판 · 승리 ${r.filter(x => x.wins.length).length}판(첫 승리 중앙 ${med(wy)}년) · 멸종 ${r.filter(x => x.extinct).length} · 몰락 ${(r.reduce((a, x) => a + x.falls.length, 0) / N).toFixed(1)}/판`);
  console.log('  승리: ' + Object.entries(wc).map(([k, v]) => `${k} ${Math.round(100 * v / N)}%`).join(', '));
  console.log('  몰락: ' + Object.entries(fc).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k} ${(v / N).toFixed(2)}`).join(', '));
  console.log('  죽음: ' + Object.entries(dc).sort((a, b) => b[1] - a[1]).slice(0, 8).map(([k, v]) => `${k} ${Math.round(100 * v / dt)}%`).join(', '));
  console.log('  시대: ' + Object.entries(eras).map(([k, v]) => `${k} ${med(v)}(${v.length})`).join(', '));
}
