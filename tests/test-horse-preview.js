const fs=require('fs'),vm=require('vm'),assert=require('assert');
const html=fs.readFileSync(process.argv[2],'utf8'),script=html.match(/<script>([\s\S]*?)<\/script>/)[1];
let draws=0;const gl=new Proxy({}, {get:(_,key)=>{
 if(key==='getShaderParameter'||key==='getProgramParameter')return ()=>true;
 if(key==='bufferData')return (_,data)=>assert([...data].every(Number.isFinite),'finite animated vertex data');
 if(key==='drawArrays')return (_,start,count)=>{assert(start===0&&count===Number(process.argv[3]||7752)*3);draws++;};
 if(key===key.toUpperCase())return 1;
 return ()=>({});
}});
const canvas={getContext:()=>gl},stats={};const context=vm.createContext({document:{getElementById:id=>id==='c'?canvas:stats},innerWidth:1200,innerHeight:900,performance:{now:()=>0},requestAnimationFrame:()=>{}});
vm.runInContext(script,context);
for(const speed of [0,2,8]){vm.runInContext(`target=${speed}`,context);for(let i=0;i<15;i++)vm.runInContext(`frame(${(draws+1)*50})`,context);}
assert(draws===45&&stats.textContent.includes('奔跑'));
console.log('PASS: preview JS initializes; 45 idle/walk/run frames contain finite geometry and expected draw count (GPU/browser not tested)');
