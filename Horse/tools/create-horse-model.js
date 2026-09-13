const fs = require('fs'), path = require('path');
// Authored lofts and swept tubes: +Z is forward, dimensions are metres.
const bones = [], parts = [];
const coat=[.64,.285,.12], hair=[.22,.085,.055], leather=[.19,.085,.05], cream=[.87,.79,.64], gold=[.72,.49,.19], hoof=[.105,.075,.08];
const bone=(name,parent,p)=>bones.push({name,parent,p});
function mesh(name,bone,color) { const p={name,bone,p:[0,0,0],s:[1,1,1],r:[0,0,0],color,vertices:[],triangles:[]};parts.push(p);return p; }
function vertex(p,v){p.vertices.push(...v);return p.vertices.length/3-1;}
function triangle(p,a,b,c){p.triangles.push(a,b,c);}
// Rings are [centre x,y,z, radius x, radius second axis]. Closed outward surfaces.
function loft(name,bone,color,rings,axis='y',sides=12){
 const p=mesh(name,bone,color);
 for(const [x,y,z,rx,ry] of rings)for(let j=0;j<sides;j++){
  const a=j*2*Math.PI/sides;
  vertex(p,axis==='y'?[x+rx*Math.cos(a),y,z+ry*Math.sin(a)]:[x+rx*Math.cos(a),y+ry*Math.sin(a),z]);
 }
 const flip=axis==='y';const tri=(a,b,c)=>flip?triangle(p,a,c,b):triangle(p,a,b,c);
 for(let k=0;k<rings.length-1;k++)for(let j=0;j<sides;j++){
  const a=k*sides+j,b=k*sides+(j+1)%sides,c=b+sides,d=a+sides;
  tri(a,b,c);tri(a,c,d);
 }
 const first=vertex(p,rings[0].slice(0,3)),last=vertex(p,rings[rings.length-1].slice(0,3)),offset=(rings.length-1)*sides;
 for(let j=0;j<sides;j++){tri(first,(j+1)%sides,j);tri(last,offset+j,offset+(j+1)%sides);}
 return p;
}
function oval(name,bone,color,c,s){
 const rings=[];for(let i=0;i<=8;i++){const a=-Math.PI/2+Math.PI*i/8;const radius=Math.max(.015,Math.cos(a));rings.push([c[0],c[1]+Math.sin(a)*s[1]/2,c[2],radius*s[0]/2,radius*s[2]/2]);}
 return loft(name,bone,color,rings);
}
function tube(name,bone,color,points,radius=.018,closed=false){
 const p=mesh(name,bone,color),N=points.length,sides=6;
 for(let k=0;k<N;k++){
  const prev=points[closed?(k+N-1)%N:Math.max(0,k-1)],next=points[closed?(k+1)%N:Math.min(N-1,k+1)];
  let t=next.map((v,i)=>v-prev[i]),len=Math.hypot(...t);t=t.map(v=>v/len);
  let u=Math.abs(t[1])<.9?[-t[2],0,t[0]]:[0,t[2],-t[1]];len=Math.hypot(...u);u=u.map(v=>v/len);
  const v=[t[1]*u[2]-t[2]*u[1],t[2]*u[0]-t[0]*u[2],t[0]*u[1]-t[1]*u[0]];
  for(let j=0;j<sides;j++){const a=j*2*Math.PI/sides;vertex(p,points[k].map((x,i)=>x+radius*(u[i]*Math.cos(a)+v[i]*Math.sin(a))));}
 }
 for(let k=0;k<(closed?N:N-1);k++)for(let j=0;j<sides;j++){
  const a=k*sides+j,b=k*sides+(j+1)%sides,c=((k+1)%N)*sides+(j+1)%sides,d=((k+1)%N)*sides+j;
  triangle(p,a,b,c);triangle(p,a,c,d);
 }
 if(!closed){const a=vertex(p,points[0]),b=vertex(p,points[N-1]);for(let j=0;j<sides;j++){triangle(p,a,(j+1)%sides,j);triangle(p,b,(N-1)*sides+j,(N-1)*sides+(j+1)%sides);}}
 return p;
}
bone('body','',[0,0,0]);bone('neck','body',[0,1.64,.65]);
for(const side of [-1,1])bone(side<0?'earL':'earR','neck',[side*.16,.98,.38]);
bone('tail','body',[0,1.62,-1.22]);
for(let i=0;i<4;i++){bone('leg'+i,'body',[i%2===0?-.30:.30,1.30,i<2?.59:-.87]);bone('shin'+i,'leg'+i,[0,-.51,0]);}
loft('Sculpted torso','body',coat,[
 [0,1.43,-1.28,.09,.12],[0,1.44,-1.13,.32,.34],[0,1.44,-.88,.44,.44],
 [0,1.43,-.52,.425,.435],[0,1.43,-.12,.44,.45],[0,1.45,.3,.41,.43],
 [0,1.46,.59,.365,.45],[0,1.48,.79,.26,.36],[0,1.50,.89,.08,.15]],'z',16);
loft('Arched neck','neck',coat,[
 [0,-.23,-.015,.31,.34],[0,-.03,.035,.32,.34],[0,.20,.105,.275,.31],
 [0,.43,.18,.22,.26],[0,.67,.24,.185,.225],[0,.86,.31,.19,.20],[0,.99,.39,.135,.13]],'y',14);
loft('Head and tapered muzzle','neck',coat,[
 [0,.84,.28,.11,.15],[0,.86,.40,.215,.24],[0,.82,.60,.225,.235],
 [0,.75,.80,.17,.185],[0,.65,1.00,.16,.145],[0,.61,1.14,.175,.125]],'z',12);
loft('Soft dark muzzle','neck',[.24,.13,.13],[[0,.62,1.03,.17,.135],[0,.605,1.13,.195,.145],[0,.60,1.23,.17,.12],[0,.60,1.26,.13,.085]],'z');
// Raised marking follows the top of the face rather than floating above it.
loft('Blaze','neck',cream,[[0,1.092,.45,.038,.005],[0,1.058,.60,.056,.006],[0,.944,.8,.045,.006],[0,.806,1,.048,.006],[0,.751,1.10,.065,.006]],'z',8);
for(const side of [-1,1]){
 const ear=side<0?'earL':'earR';
 loft('Pointed ear',ear,coat,[[0,-.02,0,.065,.07],[side*.012,.09,.005,.085,.064],[side*.028,.23,.008,.057,.04],[side*.036,.36,.006,.008,.009]],'y',8);
 loft('Ear inner',ear,[.38,.16,.12],[[0,.04,.055,.035,.012],[side*.014,.14,.052,.047,.013],[side*.03,.29,.028,.007,.006]],'y',8);
 oval('Eye socket','neck',[.39,.16,.085],[side*.205,.87,.61],[.055,.17,.185]);
 oval('Eye','neck',[.027,.021,.029],[side*.23,.88,.625],[.07,.125,.132]);
 oval('Eye glint','neck',[.98,.94,.86],[side*.267,.91,.652],[.014,.03,.03]);
 oval('Nostril','neck',[.045,.028,.034],[side*.172,.64,1.19],[.026,.075,.068]);
 tube('Bridle cheek','neck',leather,[[side*.19,1.0,.39],[side*.24,.79,.56],[side*.19,.57,1.07]],.022);
 const ring=[];for(let i=0;i<18;i++){const a=i*Math.PI/9;ring.push([side*.212,.59+Math.cos(a)*.06,1.065+Math.sin(a)*.06]);}tube('Brass bit ring','neck',gold,ring,.013,true);
 tube('Hanging rein','neck',leather,[[side*.215,.55,1.06],[side*.28,.28,.91],[side*.33,.08,.70],[side*.35,.025,.47],[side*.27,.12,.12]],.012);
}
const noseband=[];for(let i=0;i<24;i++){const a=i*Math.PI/12;noseband.push([Math.cos(a)*.183,.65+Math.sin(a)*.153,1.015]);}tube('Noseband','neck',leather,noseband,.023,true);
// Overlapping curved locks taper toward the bottom, with warm brown variation.
for(let i=0;i<7;i++){
 const y=.02+i*.135,z=-.26+i*.067;
 loft('Mane lock','neck',hair.map((v,k)=>v+(k===0?i*.007:i*.002)),[
  [.14,y-.20,z-.11,.008,.01],[.19,y-.10,z-.095,.075,.095],[.15,y+.045,z,.13,.12],[.02,y+.17,z+.055,.095,.10]],'y',8);
}
for(let i=0;i<3;i++)loft('Forelock','neck',hair,[[.12-i*.11,.82+i*.035,.78,.008,.014],[.11-i*.09,.98,.62,.09,.09],[.07-i*.045,1.065,.42,.095,.13]],'y',8);
for(let i=0;i<5;i++){
 const x=(i-2)*.065;
 loft('Tail lock','tail',hair.map(v=>v+i*.005),[[x*1.4,-1.05+(i%2)*.14,-.38,.008,.012],[x*1.7,-.70,-.40,.075,.10],[x,-.35,-.32,.10,.15],[x*.5,-.02,-.06,.09,.11],[0,.07,0,.04,.06]],'y',8);
}
for(let i=0;i<4;i++){
 const front=i<2;
 loft('Upper leg','leg'+i,coat,[[0,-.54,0,.092,.10],[0,-.40,front?0:-.075,.11,.13],[0,-.18,front?0:-.06,.145,.19],[0,.08,0,.18,.23]],'y');
 loft('Lower leg','shin'+i,coat,[[0,-.57,.025,.065,.075],[0,-.44,.005,.072,.075],[0,-.23,front?0:-.035,.055,.065],[0,-.08,0,.072,.085],[0,.035,0,.093,.095]],'y',10);
 if(i===1||i===2)loft('White sock','shin'+i,cream,[[0,-.59,.03,.073,.082],[0,-.50,.015,.077,.082],[0,-.43,.005,.074,.079]],'y',10);
 loft('Hoof','shin'+i,hoof,[[0,-.765,.065,.115,.15],[0,-.72,.065,.118,.148],[0,-.61,.035,.09,.105],[0,-.575,.027,.067,.079]],'y',10);
}
// Draped saddlecloth cross-section; two seats remain at their existing anchors.
function drape(name,color,z0,z1,thickness){
 const p=mesh(name,'body',color),cross=[[-.46,1.43],[-.49,1.58],[-.40,1.79],[-.23,1.89],[0,1.92],[.23,1.89],[.40,1.79],[.49,1.58],[.46,1.43]];
 for(let layer=0;layer<2;layer++)for(const z of [z0,z1])for(const [x,y] of cross)vertex(p,[x,y+layer*thickness,z]);
 const n=cross.length;for(let i=0;i<n-1;i++){
  triangle(p,i,i+1,n+i+1);triangle(p,i,n+i+1,n+i);
  triangle(p,2*n+i,3*n+i+1,2*n+i+1);triangle(p,2*n+i,3*n+i,3*n+i+1);
  for(const row of [0,n]){const a=row+i,b=a+1; if(row===0){triangle(p,a,2*n+a,b);triangle(p,b,2*n+a,2*n+b);}else{triangle(p,a,b,2*n+a);triangle(p,b,2*n+b,2*n+a);}}
 }
 for(const i of [0,n-1]){const a=i,b=n+i,c=3*n+i,d=2*n+i;if(i===0){triangle(p,a,b,c);triangle(p,a,c,d);}else{triangle(p,a,c,b);triangle(p,a,d,c);}}
 return p;
}
drape('Saddle blanket',cream,-.86,.55,.028);
for(const z of [.20,-.48]){
 loft('Leather saddle','body',leather,[[0,2.015,z-.285,.25,.07],[0,1.98,z-.19,.32,.065],[0,1.96,z,.32,.045],[0,1.99,z+.22,.26,.065],[0,2.015,z+.29,.20,.06]],'z');
 const rim=[];for(let i=0;i<=12;i++){const a=Math.PI*i/12;rim.push([Math.cos(a)*.28,1.96+Math.sin(a)*.10,z-.25]);}tube('Saddle rim','body',[.34,.17,.09],rim,.038);
 for(const side of [-1,1]){
  oval('Saddle flap','body',leather,[side*.505,1.64,z],[.09,.43,.43]);
  tube('Stirrup strap','body',leather,[[side*.36,1.88,z],[side*.51,1.57,z],[side*.51,1.28,z]],.025);
  tube('Stirrup frame','body',gold,[[side*.51,1.29,z],[side*.55,1.15,z-.10],[side*.55,1.07,z-.10],[side*.55,1.07,z+.10],[side*.55,1.15,z+.10]],.017,true);
 }
}
const out=process.argv[2]||path.resolve(__dirname,'../Assets');fs.mkdirSync(out,{recursive:true});
const json=JSON.stringify({bones,parts},null,2);fs.writeFileSync(path.join(out,'model.json'),json);
fs.writeFileSync(path.join(out,'preview.html'),fs.readFileSync(path.join(__dirname,'horse-preview-template.html'),'utf8').replace('__MODEL__',json));
console.log(`${bones.length} joints, ${parts.length} parts, ${parts.reduce((n,p)=>n+p.triangles.length/3,0)} triangles`);
