const fs=require('fs'), path=require('path');
const bones=[],parts=[];
const bone=(name,parent,p)=>bones.push({name,parent,p});
const brown=[.43,.22,.105],dark=[.13,.075,.045],tan=[.67,.40,.20],black=[.065,.055,.05],red=[.36,.055,.045],gold=[.73,.51,.18],white=[.86,.81,.68];
const part=(name,bone,p,s,color=brown,r=[0,0,0])=>parts.push({name,bone,p,s,color,r});
bone('body','',[0,0,0]);
part('Barrel','body',[0,1.43,-.12],[.88,.91,1.94]);
part('Chest','body',[0,1.43,.61],[.78,.98,.82],tan);
part('Rump','body',[0,1.45,-.89],[.88,.86,.91]);
bone('neck','body',[0,1.64,.65]);
part('Arched neck','neck',[0,.39,.21],[.56,1.19,.69],brown,[-24,0,0]);
part('Throat','neck',[0,.63,.43],[.4,.69,.49],tan,[-24,0,0]);
part('Head','neck',[0,.98,.55],[.46,.53,.71],brown,[23,0,0]);
part('Long muzzle','neck',[0,.83,.89],[.38,.42,.68],tan,[23,0,0]);
part('Nose','neck',[0,.74,1.13],[.39,.28,.24],dark);
part('Blaze','neck',[0,1.075,.82],[.12,.035,.39],white,[23,0,0]);
for(const side of [-1,1]) {
 part('Eye','neck',[side*.214,1.04,.72],[.075,.08,.09],black);
 part('Eye glint','neck',[side*.247,1.054,.739],[.012,.022,.025],white);
 part('Nostril','neck',[side*.169,.77,1.204],[.045,.07,.035],black);
 bone(side<0?'earL':'earR','neck',[side*.16,1.19,.4]);
 part('Ear',side<0?'earL':'earR',[0,.14,0],[.14,.38,.14]);
 part('Ear inner',side<0?'earL':'earR',[0,.16,.057],[.075,.22,.035],tan);
 part('Bridle cheek','neck',[side*.239,.95,.62],[.035,.12,.66],dark,[23,0,0]);
}
for(let i=0;i<7;i++)part('Mane','neck',[0,.09+i*.15,-.07+i*.035],[.20,.27,.23],black,[-24,0,0]);
bone('tail','body',[0,1.62,-1.22]);
part('Tail root','tail',[0,-.16,-.17],[.25,.5,.27],dark,[-25,0,0]);
part('Tail fall','tail',[0,-.53,-.27],[.29,.66,.32],black,[-8,0,0]);
for(let i=0;i<4;i++){
 const side=i%2===0?-1:1,front=i<2;
 bone('leg'+i,'body',[side*.30,1.30,front?.59:-.87]);
 part('Upper leg','leg'+i,[0,-.245,0],[.27,.59,.31],brown);
 bone('shin'+i,'leg'+i,[0,-.51,0]);
 part('Lower leg','shin'+i,[0,-.27,0],[.145,.58,.18],tan);
 part('Sock','shin'+i,[0,-.49,.015],[.16,.20,.19],i===0||i===3?white:dark);
 part('Hoof','shin'+i,[0,-.655,.045],[.22,.22,.29],black);
}
part('Saddle blanket','body',[0,1.82,-.24],[1.02,.13,1.31],red);
part('Girth','body',[0,1.4,.12],[.94,.9,.12],dark);
for(const z of [.20,-.48]){
 part('Leather saddle','body',[0,1.87,z],[.71,.18,.59],dark);
 part('Saddle rim','body',[0,1.98,z-.25],[.70,.22,.14],tan);
 for(const side of [-1,1]){
  part('Stirrup strap','body',[side*.45,1.52,z],[.055,.59,.09],dark);
  part('Stirrup tread','body',[side*.47,1.20,z],[.18,.05,.24],gold);
 }
}
// Shorten the neck to horse proportions while preserving the articulated head.
for(const p of parts) if(p.bone==='neck'){p.p[1]*=.82;p.s[1]*=.82;}
for(const b of bones) if(b.parent==='neck')b.p[1]*=.82;
const out=process.argv[2];fs.mkdirSync(out,{recursive:true});fs.writeFileSync(path.join(out,'model.json'),JSON.stringify({bones,parts},null,2));
console.log(`${bones.length} joints, ${parts.length} mesh parts`);
