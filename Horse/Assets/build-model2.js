// Derive Horse 2 without modifying the original authored model or preview.
const fs=require('fs'),path=require('path');
const original=JSON.parse(fs.readFileSync(path.join(__dirname,'model.json'),'utf8'));
const clone=x=>JSON.parse(JSON.stringify(x)), model=clone(original), spacing=.68, extension=spacing*3;
const move=(part,z)=>{for(let i=2;i<part.vertices.length;i+=3)part.vertices[i]+=z;};
const torso=model.parts.find(p=>p.name==='Sculpted torso');
for(let i=2;i<torso.vertices.length;i+=3)if(torso.vertices[i]<=-.52)torso.vertices[i]-=extension;
const blanket=model.parts.find(p=>p.name==='Saddle blanket');
for(let i=2;i<blanket.vertices.length;i+=3)if(blanket.vertices[i]<0)blanket.vertices[i]-=extension;
model.bones.find(b=>b.name==='tail').p[2]-=extension;
const saddles=original.parts.map((p,i)=>p.name==='Leather saddle'?i:-1).filter(i=>i>=0);
const rearSaddle=original.parts.slice(saddles[1]);
for(let extra=1;extra<=3;extra++) {
  for(const source of rearSaddle){const p=clone(source);move(p,-spacing*extra);model.parts.push(p);}
  for(let side=0;side<2;side++) {
    const sourceLeg=2+side, leg=2+extra*2+side;
    const upper=clone(original.bones.find(b=>b.name==='leg'+sourceLeg));
    upper.name='leg'+leg; upper.p[2]-=spacing*extra;
    const shin=clone(original.bones.find(b=>b.name==='shin'+sourceLeg));
    shin.name='shin'+leg;shin.parent=upper.name;model.bones.push(upper,shin);
    for(const source of original.parts.filter(p=>p.bone==='leg'+sourceLeg||p.bone==='shin'+sourceLeg)) {
      const p=clone(source);p.bone=p.bone.replace(String(sourceLeg),String(leg));model.parts.push(p);
    }
  }
}
const json=JSON.stringify(model,null,2);
fs.writeFileSync(path.join(__dirname,'model2.json'),json+'\n');
let preview=fs.readFileSync(path.join(__dirname,'preview.html'),'utf8').replace(/\r\n/g,'\n');
const start=preview.indexOf('const model=')+'const model='.length, end=preview.indexOf(';\nconst c=',start);
if(end<0)throw Error('Preview model boundary missing');
preview=preview.slice(0,start)+json+preview.slice(end);
preview=preview.replaceAll('雙人馬','牛馬2 · 五座十腿').replace('前座駕駛 / 後座乘客','一位駕駛 / 四位乘客').replace('yaw=-.7,pitch=-.12,zoom=240','yaw=-1.05,pitch=-.12,zoom=Math.min(160,innerWidth/6)')
 .replace('position-vec3(0.,1.5,0.)','position-vec3(0.,1.5,-1.02)')
 .replace('for(let i=0;i<4;i++){let walk=[0,Math.PI,Math.PI*1.5,Math.PI*.5],gallop=[0,.6,Math.PI,Math.PI+.6],a=phase+walk[i]*(1-run)+gallop[i]*run;', 'for(let i=0;i<10;i++){let a=phase+Math.floor(i/2)*Math.PI*.4+(i%2)*(Math.PI*(1-run)+.6*run);');
preview=preview.replace('</style>', '#label{color:#30483b;right:20px;left:20px;top:18px}#label span{color:#496551}h2{font-size:20px}footer{flex-wrap:wrap;padding:10px 16px;gap:8px}button{padding:8px 12px}canvas{height:78vh}</style>')
 .replace('c.height=innerHeight*.88','c.height=innerHeight*.78');
fs.writeFileSync(path.join(__dirname,'preview2.html'),preview);
console.log('Horse 2: '+model.bones.length+' joints, '+model.parts.length+' parts, '+model.parts.reduce((s,p)=>s+p.triangles.length/3,0)+' triangles, 5 saddles, 10 legs.');
