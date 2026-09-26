// Authored geometry shared by Unity and the offline renderer.
const fs = require('fs'), path = require('path');
const bones = [{name:'body',parent:'',p:[0,0,0]}, {name:'torso',parent:'body',p:[0,0,0]},
 {name:'neck',parent:'torso',p:[0,1.75,0]}, {name:'crown',parent:'neck',p:[0,.8,0]},
 {name:'jaw',parent:'torso',p:[-.55,1.26,0]}, {name:'tail',parent:'torso',p:[.5,1.35,0]},
 {name:'footL',parent:'body',p:[-.19,.48,0]}, {name:'footR',parent:'body',p:[.19,.48,0]}];
const parts=[], offsets={};
for(const b of bones) offsets[b.name]=b.p.map((x,i)=>x+(offsets[b.parent]?.[i]||0));
function mesh(name,bone,material,vertices,triangles){
 // Select the dominant face plane; use rest-pose positions so animated bones retain their UVs.
 const uv=[];
 for(const tri of triangles){
  const [a,b,c]=tri.map(i=>vertices[i]),u=b.map((x,i)=>x-a[i]),v=c.map((x,i)=>x-a[i]);
  const n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]];
  const dominant=n.map(Math.abs).indexOf(Math.max(...n.map(Math.abs)));
  for(const i of tri){let p=vertices[i].map((x,k)=>x+offsets[bone][k]);uv.push(...(dominant===0?[p[2],p[1]]:dominant===1?[p[0],p[2]]:[p[0],p[1]]).map(x=>x*1.15));}
 }
 parts.push({name,bone,material,vertices:vertices.flat(),triangles:triangles.flat(),uv});
}
function loft(name,bone,material,rings,n=10,axis='y'){
 let v=[],t=[];
 for(const [y,rx,rz,cx=0,cz=0] of rings)for(let i=0;i<n;i++){
  let a=i/n*Math.PI*2;v.push([cx+rx*Math.cos(a),y,cz+rz*Math.sin(a)]);
 }
 for(let j=0;j<rings.length-1;j++)for(let i=0;i<n;i++){let a=j*n+i,b=j*n+(i+1)%n,c=(j+1)*n+i,d=(j+1)*n+(i+1)%n;t.push([a,c,b],[b,c,d]);}
 for(let end=0;end<2;end++){
  let r=rings[end?rings.length-1:0],center=v.length,base=end?(rings.length-1)*n:0;v.push([r[3]||0,r[0],r[4]||0]);
  for(let i=0;i<n;i++){let a=base+i,b=base+(i+1)%n;t.push(end?[center,b,a]:[center,a,b]);}
 }
 if(axis==='x')v=v.map(([x,y,z])=>[y,-x,z]);
 mesh(name,bone,material,v,t);
}
function tube(name,bone,material,a,b,r1,r2,n=7){
 let axis=b.map((x,i)=>x-a[i]),len=Math.hypot(...axis);axis=axis.map(x=>x/len);
 let ref=Math.abs(axis[1])<.9?[0,1,0]:[1,0,0];
 const cross=(x,y)=>[x[1]*y[2]-x[2]*y[1],x[2]*y[0]-x[0]*y[2],x[0]*y[1]-x[1]*y[0]];
 let u=cross(axis,ref),ul=Math.hypot(...u);u=u.map(x=>x/ul);let w=cross(axis,u),v=[],t=[];
 [a,b].forEach((p,j)=>{let r=j?r2:r1;for(let i=0;i<n;i++){let q=i/n*Math.PI*2;v.push(p.map((x,k)=>x+r*(u[k]*Math.cos(q)+w[k]*Math.sin(q))));}});
 for(let i=0;i<n;i++){let j=(i+1)%n;t.push([i,j,i+n],[j,j+n,i+n]);}
 v.push(a,b);for(let i=0;i<n;i++){let j=(i+1)%n;t.push([2*n,j,i],[2*n+1,n+i,n+j]);}
 mesh(name,bone,material,v,t);
}
// Convex beveled outline: four perimeter rings, closed by fan caps.
function bevel(name,bone,material,poly,depth,inset=.12,zOffset=0){
 if(poly.reduce((s,p,i)=>{let q=poly[(i+1)%poly.length];return s+p[0]*q[1]-q[0]*p[1];},0)<0)poly=poly.slice().reverse();
 let n=poly.length,c=poly.reduce((s,p)=>[s[0]+p[0]/n,s[1]+p[1]/n],[0,0]),v=[],t=[];
 for(const [z,k] of [[-depth,1-inset],[-depth*.62,1],[depth*.62,1],[depth,1-inset]])
  for(const p of poly)v.push([c[0]+(p[0]-c[0])*k,c[1]+(p[1]-c[1])*k,z+zOffset]);
 for(let r=0;r<3;r++)for(let i=0;i<n;i++){let a=r*n+i,b=r*n+(i+1)%n;t.push([a,b,a+n],[b,b+n,a+n]);}
 for(let end=0;end<2;end++){
  let center=v.length,base=end?3*n:0;v.push([c[0],c[1],(end?depth:-depth)+zOffset]);
  for(let i=0;i<n;i++){let a=base+i,b=base+(i+1)%n;t.push(end?[center,a,b]:[center,b,a]);}
 }
 mesh(name,bone,material,v,t);
}
const shield=[[0,2.02],[-.23,1.80],[-.355,1.43],[-.34,.91],[-.10,.73],[.10,.73],[.34,.91],[.355,1.43],[.23,1.80]];
bevel('bronze-shield-rim','torso',0,shield,.205,.10);
const stone=shield.map(([x,y])=>[x*.84,1.37+(y-1.37)*.91]);
bevel('red-stone-chest','torso',1,stone,.218,.07);
loft('long-neck','neck',0,[[0,.185,.116],[.22,.112,.108],[.44,.066,.070],[.65,.052,.058],[.82,.090,.063]],10);
// A genuine recessed cup: bronze annulus above the solid neck, dark inner wall and back.
const ringVertices=[],ringTriangles=[],segments=12;
for(const [radius,z] of [[1,.222],[1,.272],[.64,.277],[.64,.227]])for(let i=0;i<segments;i++){
 let a=i/segments*Math.PI*2;ringVertices.push([Math.cos(a)*.067*radius,.25+Math.sin(a)*.095*radius,z]);
}
for(let r=0;r<4;r++)for(let i=0;i<segments;i++){
 let a=r*segments+i,b=r*segments+(i+1)%segments,c=((r+1)%4)*segments+i,d=((r+1)%4)*segments+(i+1)%segments;
 ringTriangles.push([a,b,c],[b,d,c]);
}
mesh('neck-socket-rim','neck',0,ringVertices,ringTriangles);
loft('neck-socket-dark','neck',4,[[.186,.020,.009,0,.235],[.25,.045,.009,0,.235],[.313,.020,.009,0,.235]],12);
loft('lower-shield-boss','torso',0,[[.65,.026,.035,0,.207],[.73,.061,.058,0,.207],[.83,.038,.048,0,.207],[.87,.012,.017,0,.207]],8);
// Branch roots grow from different levels of the crown instead of a single star joint.
loft('crown-trunk','crown',0,[[-.04,.075,.060],[.11,.088,.054],[.24,.043,.037]],8);
const tips=[[-.42,.25],[-.33,.36],[-.20,.46],[0,.53],[.20,.46],[.33,.36],[.42,.25]];
for(let i=0;i<tips.length;i++){
 let [x,y]=tips[i],s=Math.sign(x),a=[s*.022,.035,0],b=[x*.48,y*.46,-.003],c=[x,y,0];
 tube('crown-root-'+i,'crown',0,a,b,.041,.034,7);
 tube('crown-branch-'+i,'crown',0,b,c,.034,.023,7);
 tube('crown-tip-'+i,'crown',2,c,[x+s*.014,y+.022,0],.023,.021,7);
}
bevel('fish-head','torso',0,[[-1.10,1.48],[-.81,1.55],[-.60,1.48],[-.32,1.44],[-.34,1.28],[-.72,1.31],[-.96,1.39]],.132,.18);
bevel('fish-lower-jaw','jaw',0,[[-.55,.083],[-.32,-.056],[-.11,-.067],[.18,.022],[.17,.106],[-.31,.106]],.099,.16);
for(let i=0;i<3;i++)bevel('tooth-'+i,'jaw',2,[[-.48+i*.085,.093],[-.446+i*.085,.131],[-.417+i*.085,.093]],.017,.12);
for(let side of [-1,1]){
 tube('fish-eye-dark-'+side,'torso',4,[-.83,1.456,side*.117],[-.83,1.456,side*.142],.038,.035,12);
 tube('fish-eye-gold-'+side,'torso',5,[-.83,1.456,side*.141],[-.83,1.456,side*.155],.022,.015,10);
}
loft('fish-tail-body','tail',0,[[-.20,.115,.112,0,0],[.05,.085,.090,0,0],[.32,.062,.065,0,0],[.44,.047,.048,0,0]],8,'x');
bevel('fish-tail-fin','tail',0,[[.35,.04],[.61,.34],[.53,.04],[.64,-.25],[.35,-.045]],.044,.15);
bevel('fish-dorsal-fin','tail',0,[[.06,.063],[.21,.23],[.20,.033]],.026,.15);
bevel('fish-ventral-fin','tail',0,[[.08,-.06],[.18,-.22],[.18,-.035]],.024,.15);
tube('tail-upper-edge','tail',2,[.39,.09,.035],[.60,.325,.018],.007,.006,5);
// Separate animated legs meet in a narrow trunk at rest, then flare into roots.
for(let side of [-1,1]){
 const bone=side<0?'footL':'footR';
 loft('leg-'+bone,bone,0,[[-.27,.074,.09,-side*.13,.02],[-.04,.057,.071,-side*.14,0],[.17,.071,.078,-side*.13,0],[.29,.096,.09,-side*.10,0]],8);
 for(let i=0;i<4;i++){
  let spread=(i-1.5)*.085,base=[-side*.13,-.23,.02],mid=[side*.015+spread,-.37,.14],tip=[side*.055+spread*1.65,-.42,.34-Math.abs(spread)*.5];
  tube('root-palm-'+bone+i,bone,0,base,mid,.06,.038,6);
  tube('root-finger-'+bone+i,bone,0,mid,tip,.038,.026,6);
  let end=tip.map((x,k)=>x+(k===2?.024:0));tube('root-tip-'+bone+i,bone,2,tip,end,.026,.023,6);
 }
 tube('heel-'+bone,bone,0,[-side*.09,-.24,0],[side*.06,-.42,-.22],.055,.023,6);
}
const data={version:2,bones,parts,materials:[[1,1,1],[1,1,1],[.60,.51,.35],[.08,.85,.88],[.022,.030,.027],[.82,.49,.16]],textures:['bronze.png','red-stone.png']};
const dest=path.join(__dirname,'../Assets');fs.mkdirSync(dest,{recursive:true});
fs.writeFileSync(path.join(dest,'model.json'),JSON.stringify(data));
console.log(`${parts.length} mesh parts; ${parts.reduce((n,p)=>n+p.triangles.length/3,0)} triangles`);
