// Original vector artwork for Instinct. Run: node tools/make-art.mjs
// No downloaded art or external dependencies. SVG sources remain editable.
import fs from 'node:fs';
import path from 'node:path';
const root = new URL('../game/assets/', import.meta.url).pathname.replace(/^\/([A-Z]:)/i,'$1');
const ink='#172b2d', gold='#e9b75d';
const poly=(p,f,s=ink,w=1.5)=>`<polygon points="${p}" fill="${f}" stroke="${s}" stroke-width="${w}" stroke-linejoin="round"/>`;
const line=(p,c=ink,w=2)=>`<path d="${p}" fill="none" stroke="${c}" stroke-width="${w}" stroke-linecap="round" stroke-linejoin="round"/>`;
const ellipse=(x,y,rx,ry,f)=>`<ellipse cx="${x}" cy="${y}" rx="${rx}" ry="${ry}" fill="${f.slice(0,7)}" fill-opacity="${f.length===9?parseInt(f.slice(7),16)/255:1}"/>`;
const rect=(x,y,w,h,f)=>`<rect x="${x}" y="${y}" width="${w}" height="${h}" rx="1" fill="${f}"/>`;
const group=(x,y,s,b)=>`<g transform="translate(${x} ${y}) scale(${s})">${b}</g>`;
const shadow=ellipse(48,82,39,10,'#10252155');
function save(name,body,w=96,h=96){const file=path.join(root,name+'.svg');fs.mkdirSync(path.dirname(file),{recursive:true});fs.writeFileSync(file,`<svg xmlns="http://www.w3.org/2000/svg" width="${w*3}" height="${h*3}" viewBox="0 0 ${w} ${h}">${body}</svg>`);}
function hut(roof='#b68542',wall='#d8bd83'){
 return shadow+poly('18,49 48,36 77,50 77,77 47,90 18,75',wall)+poly('48,53 77,50 77,77 47,90','#8c714c')+poly('9,51 42,15 87,43 49,65',roof)+poly('42,15 87,43 78,53 49,65','#735035')+line('M 18,48 L 43,22 M 29,54 L 48,27 M 39,59 L 55,32 M 51,59 L 62,36','#f2cc80',1.5)+poly('28,64 39,69 39,84 28,79','#283532')+poly('56,65 68,60 68,69 56,74','#354742')+line('M 48,66 L 48,86','#e6c792',2);
}
function tower(){return shadow+line('M 26,84 L 33,38 M 70,84 L 63,38 M 29,70 L 66,52 M 31,52 L 68,70','#735334',5)+poly('24,36 48,26 73,37 73,49 48,60 24,48','#c9a467')+poly('48,46 73,37 73,49 48,60','#87623c')+poly('17,35 46,9 79,33 49,45',gold)+poly('46,9 79,33 49,45','#98683c')+line('M 43,63 L 43,85 M 52,60 L 52,83 M 43,69 L 52,66 M 43,76 L 52,73','#e3c38d',2);}
function logs(){let b=shadow;for(let i=0;i<5;i++){let x=15+(i%3)*19,y=60-Math.floor(i/3)*17;b+=poly(`${x},${y} ${x+28},${y-18} ${x+41},${y-11} ${x+13},${y+8}`,'#795439')+ellipse(x+7,y+3,9,7,'#e3bc78')+ellipse(x+7,y+3,4,3,'#9e713e')+line(`M ${x+17},${y-2} L ${x+32},${y-12}`,'#b98b52',2);}return b;}
function rocks(){return shadow+poly('11,72 18,50 36,44 49,60 42,83 22,86','#899fa7')+poly('18,50 36,44 49,60 28,63','#cee0d8')+poly('42,75 48,39 67,26 85,49 80,79 60,88','#607d8c')+poly('48,39 67,26 85,49 62,55','#c5d6d2')+poly('62,55 85,49 80,79 60,88','#496574');}
function sacks(){let b='';for(const [x,y] of [[22,80],[36,84],[29,71]])b+=poly(`${x-9},${y} ${x-7},${y-12} ${x-3},${y-15} ${x+3},${y-15} ${x+7},${y-12} ${x+9},${y}`,'#e4cf9c')+line(`M ${x-4},${y-12} L ${x+4},${y-12}`,'#8a6a3a',2);return b+ellipse(70,82,9,5,'#9c3c4a')+ellipse(66,80,4,3,'#e66c76');}
function wheat(){let b='';for(let x of [29,48,67]){b+=line(`M ${x},84 Q ${x-6},57 ${x},15`,'#c18f3e',3);for(let y of [27,40,53])b+=poly(`${x},${y+8} ${x-13},${y-1} ${x-12},${y-10} ${x-2},${y-4}`,gold)+poly(`${x},${y+5} ${x+13},${y-4} ${x+12},${y-13} ${x+2},${y-7}`,'#ffdb7d');}return b;}
const berries=poly('22,55 12,35 36,25 49,36 60,18 82,25 72,52','#47875b')+[ [31,54],[53,49],[68,64],[41,73]].map(([x,y])=>ellipse(x,y,13,13,'#8e314c')+ellipse(x-3,y-4,7,6,'#e66c76')+ellipse(x-5,y-6,2,2,'#ffd1ae')).join('');
const fish=poly('9,48 31,29 58,29 73,45 88,29 87,68 71,54 56,69 30,65','#52adb3')+poly('13,49 34,48 59,53 69,51 56,66 30,62','#c3ece1')+poly('38,30 51,14 57,29','#328195')+ellipse(27,43,4,4,ink)+ellipse(26,42,1,1,'#fff6d9');
const deer=line('M 35,37 L 22,22 L 23,6 M 23,22 L 10,16 M 61,37 L 74,22 L 73,6 M 74,22 L 86,16',gold,5)+poly('25,36 10,31 22,48 34,51 38,74 48,84 58,74 63,51 76,47 86,31 69,36 59,31 37,31','#ba804a')+poly('40,44 48,39 57,44 55,73 48,79 42,72','#f2cb8c')+ellipse(36,51,3,3,ink)+ellipse(61,51,3,3,ink)+ellipse(48,74,5,4,ink);
const people=ellipse(36,29,12,13,'#e9c49a')+ellipse(66,37,10,11,'#d7a975')+poly('15,79 18,53 35,46 53,54 56,82','#6cb9c5')+poly('56,57 67,50 81,60 85,82 60,84','#cf9f53')+line('M 25,56 L 23,74 M 44,56 L 46,75','#b9e5db',3);
const icons={food:wheat(),farm:wheat(),wood:logs(),stone:rocks(),gather:berries,fish,hunt:deer,pop:people,people,build:line('M 23,83 L 63,29','#d8b77c',10)+poly('42,27 58,11 86,33 70,51','#b7d3d2'),guard:poly('48,9 79,22 75,57 48,86 21,57 17,22','#6b9da4')+poly('48,18 69,28 66,53 48,74','#e8bd6a'),scout:poly('17,80 37,18 55,39 76,20 80,75','#d8be80')+line('M 37,20 L 38,73 M 55,40 L 57,80','#92764a',3),tech:line('M 38,78 L 58,78 M 40,86 L 56,86',gold,5)+`<path d="M32 57 C4 20 38 3 51 9 C83 11 86 41 64 58 L60 69 H36Z" fill="#ecb64e" stroke="${ink}" stroke-width="3"/>`+line('M 47,61 L 40,36 L 57,36 L 51,61','#fff0ad',4),law:poly('9,24 46,16 48,80 10,86','#dfc697')+poly('48,80 87,85 87,24 51,16','#b19769')+line('M 21,35 L 37,32 M 21,47 L 37,44 M 61,33 L 77,37 M 61,45 L 77,49','#65563c',3)};
// UI 아이콘(color_*)은 tools/make-icons.mjs가 만든다.
for(const [k,b] of Object.entries({Game:deer,Berries:berries,Fish:fish,Stone:rocks(),Fertile:wheat()}))save('features/'+k,b);
const field=shadow+poly('7,55 53,31 91,56 46,86','#6d5034')+[0,1,2,3].map(i=>line(`M ${18+i*11},${54+i*6} L ${51+i*9},${38+i*5}`,'#ccad58',4)).join('');
const fence=shadow+Array.from({length:8},(_,i)=>poly(`${9+i*10},76 ${9+i*10},39 ${13+i*10},30 ${17+i*10},39 ${17+i*10},76`,i%2?'#b58c53':'#d0a566')).join('')+line('M 9,59 L 87,59 M 9,70 L 87,70','#68482f',4);
const shrine=shadow+poly('17,76 48,61 81,77 48,91','#526c73')+poly('31,67 30,25 47,12 65,26 64,69 48,78','#9eada5')+poly('47,12 65,26 64,69 48,78','#5f7a81')+ellipse(46,41,7,10,gold)+line('M 46,28 L 46,54 M 37,41 L 55,41','#fff0b0',2);
const weir=shadow+poly('8,65 49,44 88,65 48,87','#488b9a')+Array.from({length:6},(_,i)=>line(`M ${15+i*12},${72-Math.abs(i-2)*5} L ${15+i*12},${41-Math.abs(i-2)*5}`,'#c09a65',4)).join('')+line('M 13,55 L 46,69 L 83,51 M 13,46 L 46,60 L 83,42','#e7d4a2',2);
const structures={Hut:hut(),Lodge:hut('#8a634a')+group(25,0,.45,deer),Weir:weir,Field:field,Granary:hut('#d2ae58','#dfd0a3')+group(36,45,.35,wheat()),Lumber:logs()+group(1,0,.5,icons.build),Quarry:rocks()+group(48,0,.45,icons.build),Palisade:fence,WarHut:hut('#9f5345')+group(32,35,.45,icons.guard),Tower:tower(),Shrine:shrine,Storehouse:hut('#8f6a3f','#c7a173')+sacks()};
for(const [k,b] of Object.entries(structures))save('structures/'+k,b);
save('decor/tree',shadow+line('M 48,83 L 46,41','#725236',7)+poly('15,54 10,35 25,14 46,6 69,15 84,40 74,58 48,68 26,64','#35694e')+poly('15,35 27,15 46,7 59,14 56,38 32,49','#76a35f')+poly('32,49 56,38 79,31 84,40 74,58 48,68','#43845b')+poly('24,25 40,13 51,18 43,30','#a6c379','none'));
save('decor/pine',shadow+line('M 48,85 L 48,32','#856441',6)+poly('9,72 25,47 18,48 35,26 31,27 48,3 66,28 62,26 79,49 70,47 87,72 48,85','#286158')+poly('48,3 48,77 9,72 25,47 18,48 35,26 31,27','#4b9271')+line('M 23,65 L 45,70 M 30,44 L 45,49 M 39,25 L 45,29','#94bd87',2));
save('decor/peak',shadow+poly('3,83 38,15 53,5 93,82 61,92','#7f9291')+poly('53,5 93,82 61,92 46,51','#4c6670')+poly('38,15 53,5 68,34 56,29 48,40 43,28 29,34','#f1edcf')+line('M 32,44 L 17,77 M 58,48 L 76,79','#a9bab0',2));
save('decor/hill',shadow+poly('4,78 20,52 42,38 62,43 93,78 50,90','#789868')+poly('42,38 62,43 93,78 50,90 60,69','#536f58')+line('M 17,70 L 36,52 L 46,49','#b3bd7b',2));
for(let i=0;i<6;i++)save('town/house_'+i,hut(['#c39a50','#b9774d','#687f7b','#507e87','#a06550','#798f9a'][i]));
for(let i=0;i<5;i++){let b=ellipse(96,143,88,24,'#14292955');if(i>0)b+=group(4,65,1.25,fence);b+=group(65,28,1.0,i>1?tower():hut())+group(10,65,.9,hut())+group(95,68,.9,hut('#7b8880'))+group(52,91,.9,hut('#d1aa61'));b+=line('M 102,54 L 102,5','#e2c38b',3)+poly('104,6 136,13 130,26 104,20','#69c2cf');save('town/capital_'+i,b,192,180);}
for(const [k,b] of Object.entries({granary:structures.Granary,market:hut('#bc6754'),temple:shrine,library:hut('#668d99'),barracks:structures.WarHut,castle:tower(),mine:rocks(),harbor:weir,university:hut('#697e9b')+group(30,-8,.6,tower()),factory:hut('#7c858a')+rect(62,5,10,35,'#8a6957'),site:logs()}))save('buildings/'+k,b);
function person(frame,soldier=false){const d=[-5,0,5,0][frame];return ellipse(46,89,22,5,'#14292955')+line(`M 39,67 L ${34+d},87 M 51,67 L ${56-d},87`,'#433f38',6)+poly('32,39 54,39 61,67 28,67',soldier?'#567f91':'#c6a467')+line(`M 32,43 L ${24-d},62 M 55,43 L ${64+d},60`,'#dcb98d',5)+ellipse(43,25,12,14,'#ddba8a')+poly('30,23 32,12 47,9 55,19 53,25 43,19','#4b4238')+ellipse(50,26,2,2,ink)+(soldier?line('M 73,82 L 73,14','#ccad72',3)+poly('68,17 73,3 78,17','#d4e2d8'):'');}
for(const soldier of [false,true])save('units/'+(soldier?'soldier':'walker'),[0,1,2,3].map(i=>group(i*96,0,1,person(i,soldier))).join(''),384,96);
const flame=poly('47,6 70,33 64,41 79,54 76,74 49,89 22,75 15,55 30,30 32,48','#e8874b')+poly('48,36 62,62 49,80 34,64','#ffe4a0');
const pot=ellipse(48,75,28,12,'#98613e')+`<path d="M29 23 H67 L62 40 Q91 77 60 85 H36 Q6 77 34 40Z" fill="#c99362" stroke="${ink}" stroke-width="3"/>`+ellipse(48,23,19,6,'#654d39')+line('M 24,63 L 72,63 M 27,73 L 69,73',gold,4);
const coin=ellipse(48,49,33,34,'#b27b34')+ellipse(44,45,29,30,'#edc76c')+ellipse(44,45,23,24,'#bd913e')+line('M 44,26 L 44,64 M 56,32 L 34,32 L 34,45 L 54,45 L 54,57 L 32,57','#ffe6a1',5);
const wheel=ellipse(48,48,37,37,'#bd995b')+ellipse(48,48,29,29,'#243c42')+Array.from({length:8},(_,i)=>{let a=i*Math.PI/4;return line(`M48,48 L${48+Math.cos(a)*30},${48+Math.sin(a)*30}`,gold,4)}).join('')+ellipse(48,48,8,8,'#edcc87');
const blade=poly('18,77 64,17 83,9 79,30 30,79','#b4d5d7')+poly('18,77 29,65 37,74 26,87','#ad7a43')+line('M 18,61 L 44,82',gold,5);
const ship=poly('13,68 83,68 68,87 30,87','#b98855')+line('M 49,69 L 49,8',gold,4)+poly('44,15 17,60 44,60','#e8d8ae')+poly('55,23 78,61 55,61','#7bc0c7');
const crown=poly('12,29 31,47 47,16 63,47 83,29 76,77 20,77',gold)+line('M 23,64 L 73,64','#fff0b0',4)+ellipse(47,56,5,7,'#70bec7');
const skull=ellipse(48,40,28,29,'#cbd1b7')+rect(32,52,33,24,'#cbd1b7')+ellipse(36,42,8,9,'#273e42')+ellipse(60,42,8,9,'#273e42')+poly('48,49 43,59 53,59','#273e42')+line('M 39,67 L 39,75 M 48,67 L 48,75 M 57,67 L 57,75','#6c8079',3);
const cross=poly('36,12 60,12 60,36 84,36 84,60 60,60 60,84 36,84 36,60 12,60 12,36 36,36','#90cbb8');
const chip=rect(24,24,48,48,'#74b6ba')+rect(33,33,30,30,'#244b56')+[16,32,48,64,80].map(x=>line(`M ${x},12 L ${x},24 M ${x},72 L ${x},84 M 12,${x} L 24,${x} M72,${x} L84,${x}`,gold,3)).join('');
const atom=Array.from({length:3},(_,i)=>`<ellipse cx="48" cy="48" rx="39" ry="14" fill="none" stroke="#89c6bc" stroke-width="3" transform="rotate(${i*60} 48 48)"/>`).join('')+ellipse(48,48,9,9,gold);
const factory=poly('12,80 12,41 31,26 31,41 50,26 50,41 70,26 70,81','#95a9a7')+rect(69,11,12,70,'#68818a')+rect(22,54,12,15,'#e7bc66')+rect(46,54,12,15,'#e7bc66');
const tech={fire:flame,farming:wheat(),herding:deer,pottery:pot,counting:coin,bronze:blade,script:icons.law,wheel,iron:blade,currency:coin,plow:icons.farm,masonry:tower(),crafts:icons.build,theology:icons.law,press:icons.law,gunpowder:flame,navigation:ship,banking:coin,steam:factory,rail:wheel,medicine:cross,electricity:icons.tech,engine:factory,broadcast:atom,fission:atom,computing:chip,ecology:berries};
// (UI 아이콘은 make-icons.mjs)
console.log('Original SVG assets generated.');
