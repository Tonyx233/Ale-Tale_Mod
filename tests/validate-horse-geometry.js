const fs=require('fs'),assert=require('assert');
const m=JSON.parse(fs.readFileSync(process.argv[2]));let faces=0;
for(const p of m.parts){
 assert(p.vertices.length%3===0&&p.vertices.every(Number.isFinite),p.name+' vertices');
 assert(p.triangles.length%3===0,p.name+' indices');
 const edges=new Map();let volume=0;
 for(let i=0;i<p.triangles.length;i+=3){
  const ids=p.triangles.slice(i,i+3);assert(ids.every(n=>Number.isInteger(n)&&n>=0&&n<p.vertices.length/3));
  const [a,b,c]=ids.map(n=>p.vertices.slice(n*3,n*3+3));
  const u=b.map((n,k)=>n-a[k]),v=c.map((n,k)=>n-a[k]);
  const cross=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]];
  assert(Math.hypot(...cross)>1e-10,p.name+' degenerate triangle');
  volume+=a[0]*(b[1]*c[2]-b[2]*c[1])+a[1]*(b[2]*c[0]-b[0]*c[2])+a[2]*(b[0]*c[1]-b[1]*c[0]);
  for(let k=0;k<3;k++){const x=ids[k],y=ids[(k+1)%3],key=[Math.min(x,y),Math.max(x,y)].join(',');const e=edges.get(key)||[0,0];e[0]++;e[1]+=x<y?1:-1;edges.set(key,e);}
  faces++;
 }
 assert([...edges.values()].every(e=>e[0]===2&&e[1]===0),p.name+' non-manifold or inconsistent winding');
 assert(volume>0,p.name+' inward winding: '+volume);
}
assert(m.parts.filter(p=>p.name==='Leather saddle').length===2);
// Hoof sole stays on the existing ground plane; skeletal anchors stay compatible.
for(let i=0;i<4;i++){const foot=m.parts.find(p=>p.name==='Hoof'&&p.bone==='shin'+i);const bottom=Math.min(...foot.vertices.filter((_,k)=>k%3===1))+1.3-.51;assert(Math.abs(bottom-.025)<1e-6);}
console.log(`PASS: ${faces} non-degenerate triangles, closed oriented meshes, two saddles, four grounded hooves`);
