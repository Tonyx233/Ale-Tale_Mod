const fs=require('fs'),path=require('path'),assert=require('assert');
const root=process.argv[2]||path.resolve(__dirname,'..');
const m=JSON.parse(fs.readFileSync(path.join(root,'Horse/Assets/model.json')));
const names=new Set();for(const b of m.bones){assert(!names.has(b.name));assert(!b.parent||names.has(b.parent));assert(b.p.length===3&&b.p.every(Number.isFinite));names.add(b.name)}
for(const p of m.parts){assert(names.has(p.bone));for(const k of ['p','s','r','color'])assert(p[k].length===3&&p[k].every(Number.isFinite));assert(p.s.every(n=>n>0));assert(p.color.every(n=>n>=0&&n<=1))}
assert(m.parts.filter(p=>p.name==='Leather saddle').length===2);
for(let i=0;i<4;i++)assert(names.has('leg'+i)&&names.has('shin'+i));
const preview=fs.readFileSync(path.join(root,'Horse/Assets/preview.html'),'utf8').replace(/\r\n/g,'\n');
assert(preview.includes(JSON.stringify(m,null,2)),'Preview must contain exactly the shipped model');
console.log(`PASS: ${names.size} model joints, ${m.parts.length} mesh parts, two saddles, preview data equality`);
