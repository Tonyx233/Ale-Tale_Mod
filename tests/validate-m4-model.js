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
const roles = new Set([undefined, 'furniture', 'mag', 'scope']);
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
check(hi[1] - lo[1] > .3 && hi[1] - lo[1] < .36, 'height incl. ACOG and magazine');
check(tris < 4000, 'triangle budget ' + tris);
check(model.parts.filter(p => p.bone === 'mag').length >= 3, 'magazine parts animate with the mag bone');
check(model.parts.some(p => p.name === 'Fiber optic'), 'fiber optic part (emissive material in DLL)');
check(model.parts.some(p => p.name === 'Selector lever' && p.bone === 'selector'), 'selector lever rotates with fire mode');

for (const icon of ['m4-icon.png', 'ammo-icon.png']) {
  const png = fs.readFileSync(path.join(root, 'M4', 'Assets', icon));
  check(png.readUInt32BE(0) === 0x89504e47 && png.readUInt32BE(16) === 256 && png.readUInt32BE(20) === 256 && png[25] === 6, icon + ' is 256x256 RGBA like native icons');
}
console.log('PASS: ' + checks + ' M4 model checks; ' + model.parts.length + ' parts, ' + tris + ' triangles, length ' + length.toFixed(3) + ' m');
