// Structural and geometric checks for M4/Assets/model.json (the mesh the DLL embeds).
const fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..');
const model = JSON.parse(fs.readFileSync(path.join(root, 'M4', 'Assets', 'model.json'), 'utf8'));
let checks = 0;
const check = (ok, name) => { if (!ok) throw new Error('FAIL: ' + name); checks++; };
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

const bones = new Map();
for (const b of model.bones) {
  check(!bones.has(b.name), 'unique bone ' + b.name);
  check(b.parent === '' || bones.has(b.parent), 'parent before child ' + b.name);
  const p = b.parent ? bones.get(b.parent) : [0, 0, 0];
  bones.set(b.name, [p[0] + b.p[0], p[1] + b.p[1], p[2] + b.p[2]]);
}
for (const name of ['rifle', 'mag', 'charge', 'trigger', 'selector']) check(bones.has(name), 'animated bone ' + name);

let tris = 0, lo = [1e9, 1e9, 1e9], hi = [-1e9, -1e9, -1e9];
const optics = ['scope-reddot', 'scope-holo', 'scope-acog', 'scope-brass', 'scope-sniper'];
const roles = new Set([undefined, 'furniture', 'mag', 'iron-up', 'iron-down', ...optics]);
for (const p of model.parts) {
  check(bones.has(p.bone), 'part bone ' + p.name);
  check(roles.has(p.role), 'known role ' + p.name);
  check(p.vertices.length % 3 === 0 && p.triangles.length % 3 === 0, 'buffer shape ' + p.name);
  check(p.color.length === 3 && p.color.every(c => c >= 0 && c <= 1), 'colour ' + p.name);
  check(String(p.s) === '1,1,1' && String(p.r) === '0,0,0' && String(p.p) === '0,0,0', 'identity part transform ' + p.name);
  const n = p.vertices.length / 3, V = i => [p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]];
  const origin = bones.get(p.bone);
  let volume = 0;
  for (let t = 0; t < p.triangles.length; t += 3) {
    const [a, b, c] = [p.triangles[t], p.triangles[t + 1], p.triangles[t + 2]];
    check(a < n && b < n && c < n && a >= 0 && b >= 0 && c >= 0, 'index range ' + p.name);
    const A = V(a), B = V(b), C = V(c), area = Math.hypot(...cross(sub(B, A), sub(C, A))) / 2;
    check(area > 1e-10, 'non-degenerate triangle ' + p.name);
    volume += dot(A, cross(B, C)) / 6;
    for (const v of [A, B, C]) for (let k = 0; k < 3; k++) { lo[k] = Math.min(lo[k], v[k] + origin[k]); hi[k] = Math.max(hi[k], v[k] + origin[k]); }
    tris++;
  }
  // Closed, outward-wound (right-hand) surfaces have positive signed volume; Unity reads them as front faces.
  check(volume > 0, 'outward winding / closed surface ' + p.name + ' volume=' + volume);
}
const length = hi[2] - lo[2];
check(length > .84 && length < .88, 'overall length ' + length.toFixed(3) + ' m (real M4 0.838 m extended)');
check(Math.abs(lo[2] + .256) < .002, 'butt at z=-0.256 matches M4Model.ModelButt');
check(Math.abs(hi[2] - .602) < .002, 'flash hider at z=0.602 (muzzle node at 0.612)');
check(hi[1] - lo[1] > .3 && hi[1] - lo[1] < .38, 'height incl. tallest optic and magazine');
check(tris < 6000, 'triangle budget ' + tris + ' (one optic visible at a time)');
check(model.parts.filter(p => p.bone === 'mag').length >= 3, 'magazine parts animate with the mag bone');
check(model.parts.some(p => p.name === 'Fiber optic'), 'fiber optic part (emissive material in DLL)');
check(model.parts.some(p => p.name === 'Selector lever' && p.bone === 'selector'), 'selector lever rotates with fire mode');

// Interchangeable optics: every role exists, and ADS sight points in M4Scopes.cs match the geometry.
const part = name => model.parts.find(p => p.name === name);
const bounds = p => { const v = p.vertices, b = [[1e9, -1e9], [1e9, -1e9], [1e9, -1e9]]; for (let i = 0; i < v.length; i++) { const k = i % 3; b[k][0] = Math.min(b[k][0], v[i]); b[k][1] = Math.max(b[k][1], v[i]); } return b; };
for (const role of [...optics, 'iron-up', 'iron-down']) check(model.parts.some(p => p.role === role), 'optic role present ' + role);
const scopes = fs.readFileSync(path.join(root, 'M4', 'M4Scopes.cs'), 'utf8');
const sight = kind => { const m = new RegExp('Kind = M4ScopeKind\\.' + kind + '\\b[^\\n]*?SightY = ([.\\d]+)f, SightZ = ([.\\d]+)f').exec(scopes); check(m, 'sight point declared for ' + kind); return [+m[1], +m[2]]; };
const post = bounds(part('Front post')), ringTop = bounds(part('BUIS ring top')), ringSide = bounds(part('BUIS ring side'));
const postTip = post[1][1], ringCentre = (ringTop[1][0] + bounds(part('BUIS leaf'))[1][1]) / 2;
const iron = sight('Iron');
check(Math.abs(postTip - iron[0]) < .0005 && Math.abs(ringCentre - iron[0]) < .0005, 'iron sight line: post tip ' + postTip + ', ring centre ' + ringCentre + ' = SightY ' + iron[0]);
check(Math.abs((ringTop[2][0] + ringTop[2][1]) / 2 - iron[1]) < .001, 'rear aperture at SightZ');
check(ringTop[1][0] - bounds(part('BUIS leaf'))[1][1] > .01, 'ghost ring aperture at least 10 mm');
const tube = bounds(part("Red dot body")), red = sight("RedDot");
check(Math.abs((tube[1][0] + tube[1][1]) / 2 - red[0]) < .001 && red[1] > tube[2][0] && red[1] < tube[2][1], 'red dot sight point on the tube axis');
const hood = bounds(part('Holo hood top')), base = bounds(part('Holo base')), holo = sight('Holo');
check(holo[0] > base[1][1] && holo[0] < hood[1][0] && holo[1] > hood[2][0] && holo[1] < hood[2][1], 'holo sight point inside the window');
check(model.parts.filter(p => p.name.startsWith('Red dot') && /lens/i.test(p.name)).length === 0, 'red dot tube is open (no opaque lens discs)');

for (const icon of ['m4-icon.png', 'ammo-icon.png', ...optics.map(r => r + '.png')]) {
  const png = fs.readFileSync(path.join(root, 'M4', 'Assets', icon));
  check(png.readUInt32BE(0) === 0x89504e47 && png.readUInt32BE(16) === 256 && png.readUInt32BE(20) === 256 && png[25] === 6, icon + ' is 256x256 RGBA like native icons');
}
console.log('PASS: ' + checks + ' M4 model checks; ' + model.parts.length + ' parts, ' + tris + ' triangles, length ' + length.toFixed(3) + ' m');
