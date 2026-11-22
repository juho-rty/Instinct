// 오래 돌리기: 정해 둔 시간 동안 여러 설정·씨앗으로 시뮬을 돌려 로그를 쌓는다.
//   node tools/soak.mjs [시간(분)=180] [동시 실행=3] [햇수=500]
// 출력: logs/sim-3h/runs.csv (판마다 한 줄), logs/sim-3h/full/<설정>_s<씨앗>.txt.gz (판 전체 로그)
// 먼저 dotnet build src/Instinct.Runner -c Release 로 러너를 만들어 둔다.
import { spawn } from 'node:child_process';
import fs from 'node:fs'; import path from 'node:path'; import zlib from 'node:zlib'; import { fileURLToPath } from 'node:url';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const minutes = +(process.argv[2] ?? 180), par = +(process.argv[3] ?? 3), years = +(process.argv[4] ?? 500);
const rel = path.join(root, 'src/Instinct.Runner/bin/Release');
const dll = path.join(rel, fs.readdirSync(rel)[0], 'Instinct.Runner.dll');
const out = path.join(root, 'logs/sim-3h'); fs.mkdirSync(path.join(out, 'full'), { recursive: true });
const cfgs = [
  ['plain', []], ['auto', ['--auto']], ['player', ['--player']],
  ['easy', ['--player', '--difficulty', 'easy']], ['hard', ['--player', '--difficulty', 'hard']],
  ...['conquest', 'commerce', 'terror', 'tyranny', 'learning'].map(a => [a, ['--aim', a]]),
];
const csv = path.join(out, 'runs.csv');
if (!fs.existsSync(csv)) fs.writeFileSync(csv, 'cfg,seed,years,era,peak,collapses,realmPop,territory,knowledge,wins,living,peakPeople,births,falls,deaths,ms\n');
const end = Date.now() + minutes * 60000; let seed = 1, ci = 0, done = 0, running = 0;
const q = s => `"${String(s).replace(/"/g, "'")}"`;
function next() {
  if (Date.now() >= end) { if (running === 0) { console.log(`끝: ${done}판`); process.exit(0); } return; }
  const [name, extra] = cfgs[ci]; const s = seed;
  if (++ci === cfgs.length) { ci = 0; seed++; }
  running++; const t0 = Date.now();
  const p = spawn('C:/Program Files/dotnet/dotnet.exe', [dll, '--seed', s, '--years', years, ...extra]);
  const chunks = []; p.stdout.on('data', d => chunks.push(d)); p.stderr.on('data', d => chunks.push(d));
  p.on('close', code => {
    const txt = Buffer.concat(chunks).toString('utf8').split('\r').join('');
    fs.writeFileSync(path.join(out, 'full', `${name}_s${s}.txt.gz`), zlib.gzipSync(txt));
    const g = re => (txt.match(re) || [])[1] ?? '';
    const row = [name, s, g(/═ 씨앗 \d+, (\d+)년 경과/), g(/\n(\S+)\s\(최고/), g(/\(최고 (\S+)\)/), g(/몰락 (\d+)회/),
      g(/백성 ([\d,]+)명, 영토/).replace(/,/g, ''), g(/명, 영토 (\d+), 지식/), g(/영토 \d+, 지식 (\d+)/), q(g(/승리: (.*)/) || '없음'),
      g(/살아남은 사람 (\d+)명/), g(/\(최대 (\d+)명\)/), g(/태어난 사람 (\d+)명/), q(g(/\n몰락: (.*)/)), q(g(/죽음·이탈: (.*)/)), Date.now() - t0];
    if (code !== 0) row[3] = `EXIT${code}`;
    fs.appendFileSync(csv, row.join(',') + '\n');
    done++; running--;
    if (done % 50 === 0) console.log(`${new Date().toLocaleTimeString()} ${done}판 (씨앗 ${seed})`);
    next();
  });
}
for (let i = 0; i < par; i++) next();
