// Authored low-poly geometry, shared by the game and offline preview/validation.
const fs = require('fs'), path = require('path');
const bones = [{name:'body',parent:'',p:[0,0,0]}, {name:'torso',parent:'body',p:[0,0,0]},
 {name:'neck',parent:'torso',p:[0,1.75,0]}, {name:'crown',parent:'neck',p:[0,.8,0]},
 {name:'jaw',parent:'torso',p:[-.55,1.26,0]}, {name:'tail',parent:'torso',p:[.5,1.35,0]},
 {name:'footL',parent:'body',p:[-.19,.48,0]}, {name:'footR',parent:'body',p:[.19,.48,0]}];
const parts=[];
function mesh(name,bone,material,vertices,triangles){parts.push({name,bone,material,vertices:vertices.flat(),triangles:triangles.flat()});}
// Watertight ring loft with outward winding; ring coordinates describe y, x radius, z radius, center x/z.
function loft(name,bone,material,rings,n=8){
 let v=[],t=[]; rings.forEach(([y,rx,rz,cx=0,cz=0])=>{for(let i=0;i<n;i++){let a=i/n*Math.PI*2;v.push([cx+rx*Math.cos(a),y,cz+rz*Math.sin(a)]);}});
 for(let j=0;j<rings.length-1;j++)for(let i=0;i<n;i++){let a=j*n+i,b=j*n+(i+1)%n,c=(j+1)*n+i,d=(j+1)*n+(i+1)%n;t.push([a,c,b],[b,c,d]);}
 v.push([rings[0][3]||0,rings[0][0],rings[0][4]||0]);let lo=v.length-1;
 let r=rings[rings.length-1];v.push([r[3]||0,r[0],r[4]||0]);let hi=v.length-1;
 for(let i=0;i<n;i++){t.push([lo,i,(i+1)%n]);let a=(rings.length-1)*n+i,b=(rings.length-1)*n+(i+1)%n;t.push([hi,b,a]);}
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
function prism(name,bone,material,poly,depth){
 if(poly.reduce((sum,p,i)=>{let q=poly[(i+1)%poly.length];return sum+p[0]*q[1]-q[0]*p[1];},0)<0)poly=poly.slice().reverse();
 let v=poly.map(p=>[p[0],p[1],-depth]).concat(poly.map(p=>[p[0],p[1],depth])),t=[],n=poly.length;
 for(let i=1;i<n-1;i++)t.push([0,i+1,i],[n,n+i,n+i+1]);
 for(let i=0;i<n;i++){let j=(i+1)%n;t.push([i,j,n+i],[j,n+j,n+i]);}mesh(name,bone,material,v,t);
}
loft('bronze-shield-rim','torso',0,[[.55,.1,.1],[.73,.46,.2],[1.23,.48,.24],[1.8,.25,.15],[2.0,.1,.09]]);
prism('red-stone-chest','torso',1,[[-.07,.66],[.37,.81],[.395,1.23],[.19,1.73],[0,1.89],[-.19,1.73],[-.395,1.23],[-.37,.81]],.265);
// Front seam and ornamental veins remain geometry, no texture/asset dependencies.
tube('chest-tide-seam','torso',3,[0,.72,.28],[0,1.79,.28],.012,.01);
for(let i=0;i<5;i++)for(let s of [-1,1])tube('bronze-vein'+i+'-'+s,'torso',2,[s*.025,.88+i*.16,.275],[s*(.25-i*.022),.94+i*.16,.275],.006,.003,5);
loft('long-neck','neck',0,[[0,.16,.11],[.27,.065,.065],[.61,.047,.05],[.84,.1,.06]]);
loft('hollow-face','neck',4,[[.23,.03,.012,0,.065],[.3,.045,.014,0,.062],[.4,.025,.01,0,.058]],10);
loft('amber-face-core','neck',5,[[.245,.013,.009,0,.08],[.31,.021,.01,0,.08],[.38,.009,.009,0,.077]],8);
for(let i=0;i<7;i++){
 let side=(i-3)/3, bx=side*.42, by=.13+(.2*(1-Math.abs(side)));
 tube('crown-branch-'+i,'crown',0,[side*.035,0,0],[bx,by,0],.045,.027);
 tube('crown-tip-'+i,'crown',2,[bx,by,0],[bx+side*.045,by+.055,.007],.027,.021);
}
// Horizontal fish, with upper pointed mouth and a separate hinged lower jaw.
prism('fish-head','torso',0,[[-1.14,1.44],[-.72,1.58],[-.35,1.47],[-.31,1.23],[-.68,1.23],[-.89,1.31]],.12);
prism('fish-lower-jaw','jaw',0,[[-.59,.03],[-.36,-.15],[.1,-.05],[.16,.07]],.09);
for(let i=0;i<5;i++)prism('tooth-'+i,'jaw',2,[[-.49+i*.095,.025],[-.455+i*.095,.085],[-.425+i*.095,.025]],.018);
for(let side of [-1,1]){
 tube('fish-eye-dark-'+side,'torso',4,[-.72,1.46,side*.115],[-.72,1.46,side*.14],.052,.049,10);
 tube('fish-eye-gold-'+side,'torso',5,[-.72,1.46,side*.14],[-.72,1.46,side*.151],.024,.02,8);
}
prism('fish-tail-body','tail',0,[[-.17,.15],[.15,.08],[.37,.14],[.43,.29],[.39,-.24],[.17,-.1],[-.17,-.1]],.075);
prism('fish-tail-fin','tail',0,[[.31,.04],[.59,.36],[.5,.01],[.65,-.29],[.29,-.07]],.025);
tube('tail-upper-edge','tail',2,[.34,.045,.025],[.58,.34,.025],.012,.008);
for(let side of [-1,1]){
 const bone=side<0?'footL':'footR';
 tube('leg-'+bone,bone,0,[side*-.09,.12,0],[0,-.27,.025],.12,.09);
 for(let i=0;i<4;i++){
  let spread=(i-1.5)*.12;
  tube('root-palm-'+bone+i,bone,0,[0,-.25,.02],[side*.12+spread,-.39,.19],.075,.047);
  tube('root-finger-'+bone+i,bone,0,[side*.12+spread,-.39,.19],[side*.18+spread*1.35,-.42,.39-Math.abs(spread)*.4],.047,.029);
 }
 tube('heel-'+bone,bone,0,[0,-.26,0],[side*.13,-.42,-.25],.07,.028);
}
const data={version:1,bones,parts,materials:[[.19,.37,.32],[.34,.11,.085],[.49,.4,.22],[.08,.85,.88],[.018,.024,.021],[1,.52,.08]]};
const dest=path.join(__dirname,'../Assets');fs.mkdirSync(dest,{recursive:true});
fs.writeFileSync(path.join(dest,'model.json'),JSON.stringify(data));
console.log(`${parts.length} mesh parts; ${parts.reduce((n,p)=>n+p.triangles.length/3,0)} triangles`);
