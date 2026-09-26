const fs=require('fs'),path=require('path'),assert=require('assert');
const d=JSON.parse(fs.readFileSync(path.join(__dirname,'../Tidefork/Assets/model.json')));
const bones=new Set(),names=new Set();let triangles=0,checks=0;
function check(ok,msg){checks++;assert(ok,msg);}
for(const b of d.bones){check(!bones.has(b.name),'unique bone');check(!b.parent||bones.has(b.parent),'parent before child');check(b.p.length===3&&b.p.every(Number.isFinite),'finite bone');bones.add(b.name);}
for(const key of ['torso','neck','crown','jaw','tail','footL','footR'])check(bones.has(key),'animation bone '+key);
for(const p of d.parts){
 check(!names.has(p.name),'unique part');names.add(p.name);check(bones.has(p.bone),'bound bone');
 check(p.material>=0&&p.material<d.materials.length,'material');check(p.vertices.length%3===0&&p.vertices.every(Number.isFinite),'vertices');
 check(p.triangles.length%3===0,'triangle list');let volume=0,edges=new Map();
 for(let i=0;i<p.triangles.length;i+=3){
  let ids=p.triangles.slice(i,i+3);check(ids.every(j=>Number.isInteger(j)&&j>=0&&j<p.vertices.length/3),'index bounds');
  let [a,b,c]=ids.map(j=>p.vertices.slice(j*3,j*3+3));
  let u=b.map((v,j)=>v-a[j]),v=c.map((v,j)=>v-a[j]);
  let n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]];
  check(Math.hypot(...n)>1e-9,'nondegenerate '+p.name);
  volume+=(a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0]))/6;
  for(let j=0;j<3;j++){let edge=[ids[j],ids[(j+1)%3]].sort((a,b)=>a-b).join(',');edges.set(edge,(edges.get(edge)||0)+1);}
 }
 check([...edges.values()].every(n=>n===2),'closed mesh '+p.name);
 check(volume>0,'outward winding '+p.name+' volume '+volume);
 triangles+=p.triangles.length/3;
}
check(triangles<6000,'mesh budget');
console.log(`PASS: ${checks} geometry checks; ${d.parts.length} parts, ${triangles} triangles`);
