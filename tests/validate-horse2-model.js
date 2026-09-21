const fs=require('fs'),path=require('path'),assert=require('assert');
const root=path.resolve(__dirname,'..'),assets=path.join(root,'Horse/Assets');
const m=JSON.parse(fs.readFileSync(path.join(assets,'model2.json'))),old=JSON.parse(fs.readFileSync(path.join(assets,'model.json')));
const names=new Set();
for(const b of m.bones){assert(!names.has(b.name));assert(!b.parent||names.has(b.parent));assert(b.p.length===3&&b.p.every(Number.isFinite));names.add(b.name);}
for(const p of m.parts){assert(names.has(p.bone));for(const k of ['p','s','r','color'])assert(p[k].length===3&&p[k].every(Number.isFinite));assert(p.s.every(n=>n>0));}
assert(m.bones.length===25 && m.parts.filter(p=>p.name==='Leather saddle').length===5);
for(let i=0;i<10;i++)assert(names.has('leg'+i)&&names.has('shin'+i));
assert(!names.has('leg10'));
// Head, neck, original legs and the first two saddles stay exactly as authored.
for(let i=0;i<old.parts.length;i++)if(!['Sculpted torso','Saddle blanket'].includes(old.parts[i].name))assert.deepStrictEqual(m.parts[i],old.parts[i]);
const saddles=m.parts.filter(p=>p.name==='Leather saddle');
for(let i=0;i<5;i++){
 const z=saddles[i].vertices.filter((_,j)=>j%3===2);
 assert(z.some(v=>Math.abs(v-(.2-i*.68))<1e-6),'Saddle matches rider offset '+i);
}
assert(fs.readFileSync(path.join(assets,'preview2.html'),'utf8').includes(JSON.stringify(m,null,2)));
console.log('PASS: five saddles, ten legs, ordered bones, seat alignment, unchanged original pieces, preview data equality');
