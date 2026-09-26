const fs = require('fs'), path = require('path');
// M4A1 carbine concept mesh. +Z is muzzle, +Y up, +X right; metres (real M4 ~0.84 m).
// Same Definition format as Horse/Assets/model.json (bones + parts with authored topology).
const bones = [], parts = [];
const W = 1.18; // chunky width factor to match the game's stylised props
const C = {
  receiver: [.25, .262, .28], metal: [.17, .172, .18], rail: [.3, .31, .325], port: [.06, .06, .065],
  furniture: [.205, .2, .198], mag: [.34, .345, .35], rubber: [.1, .1, .105],
  scope: [.22, .228, .24], brass: [.72, .52, .2]
};
const bone = (name, parent, p) => bones.push({ name, parent, p });
function mesh(name, b, color, role) { const p = { name, bone: b, p: [0, 0, 0], s: [1, 1, 1], r: [0, 0, 0], color, vertices: [], triangles: [] }; if (role) p.role = role; parts.push(p); return p; }
function vertex(p, v) { p.vertices.push(...v); return p.vertices.length / 3 - 1; }
const V = (p, i) => [p.vertices[i * 3], p.vertices[i * 3 + 1], p.vertices[i * 3 + 2]];
const sub = (a, b) => a.map((v, i) => v - b[i]), dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
// Right-hand outward winding (same convention as the horse data; Unity reads it as front faces).
function tri(p, a, b, c, want) {
  if (want) { const n = cross(sub(V(p, b), V(p, a)), sub(V(p, c), V(p, a))); if (dot(n, want) < 0) { p.triangles.push(a, c, b); return; } }
  p.triangles.push(a, b, c);
}
// Loft rings [x, y, z, rx, ry] along Z (horse convention, verified outward).
function loft(name, b, color, rings, sides = 12, role, phase = 0) {
  const p = mesh(name, b, color, role);
  for (const [x, y, z, rx, ry] of rings) for (let j = 0; j < sides; j++) { const a = phase + j * 2 * Math.PI / sides; vertex(p, [x + rx * Math.cos(a), y + ry * Math.sin(a), z]); }
  for (let k = 0; k < rings.length - 1; k++) for (let j = 0; j < sides; j++) {
    const a = k * sides + j, bb = k * sides + (j + 1) % sides, c = bb + sides, d = a + sides;
    const dir = rings[k + 1][2] >= rings[k][2] ? 1 : -1;
    if (dir > 0) { p.triangles.push(a, bb, c, a, c, d); } else { p.triangles.push(a, c, bb, a, d, c); }
  }
  const first = vertex(p, rings[0].slice(0, 3)), last = vertex(p, rings[rings.length - 1].slice(0, 3)), off = (rings.length - 1) * sides;
  const back = rings[0][2] <= rings[rings.length - 1][2] ? [0, 0, -1] : [0, 0, 1];
  for (let j = 0; j < sides; j++) { tri(p, first, j, (j + 1) % sides, back); tri(p, last, off + j, off + (j + 1) % sides, back.map(v => -v)); }
  return p;
}
// Hollow tube along Z: rings [x, y, z, outer, inner]; outer faces out, bore faces the axis, annular ends.
function pipe(name, b, color, rings, sides = 16, role) {
  const p = mesh(name, b, color, role), n = rings.length;
  for (const [x, y, z, ro] of rings) for (let j = 0; j < sides; j++) { const a = j * 2 * Math.PI / sides; vertex(p, [x + ro * Math.cos(a), y + ro * Math.sin(a), z]); }
  for (const [x, y, z, ro, ri] of rings) for (let j = 0; j < sides; j++) { const a = j * 2 * Math.PI / sides; vertex(p, [x + ri * Math.cos(a), y + ri * Math.sin(a), z]); }
  const O = (k, j) => k * sides + (j % sides), I = (k, j) => (n + k) * sides + (j % sides);
  for (let k = 0; k < n - 1; k++) for (let j = 0; j < sides; j++) {
    const a = Math.PI * 2 * (j + .5) / sides, [x, y] = rings[k];
    const out = [Math.cos(a), Math.sin(a), 0], inward = [-out[0], -out[1], 0];
    tri(p, O(k, j), O(k, j + 1), O(k + 1, j + 1), out); tri(p, O(k, j), O(k + 1, j + 1), O(k + 1, j), out);
    tri(p, I(k, j), I(k, j + 1), I(k + 1, j + 1), inward); tri(p, I(k, j), I(k + 1, j + 1), I(k + 1, j), inward);
  }
  for (const [k, dir] of [[0, -1], [n - 1, 1]]) for (let j = 0; j < sides; j++) {
    tri(p, O(k, j), O(k, j + 1), I(k, j + 1), [0, 0, dir]); tri(p, O(k, j), I(k, j + 1), I(k, j), [0, 0, dir]);
  }
  return p;
}
// Cylinder along an arbitrary axis ('x' or 'y') for turrets, pins and nuts.
function cyl(name, b, color, c, axis, r, len, sides = 10, role) {
  const p = mesh(name, b, color, role), h = len / 2, ends = [-h, h];
  for (const e of ends) for (let j = 0; j < sides; j++) {
    const a = j * 2 * Math.PI / sides, u = r * Math.cos(a), v = r * Math.sin(a);
    vertex(p, axis === 'x' ? [c[0] + e, c[1] + u, c[2] + v] : [c[0] + u, c[1] + e, c[2] + v]);
  }
  const ax = axis === 'x' ? [1, 0, 0] : [0, 1, 0];
  for (let j = 0; j < sides; j++) {
    const a = j, bb = (j + 1) % sides, c2 = bb + sides, d = a + sides;
    const mid = V(p, a).map((v, i) => (v + V(p, c2)[i]) / 2), out = sub(mid, c).map((v, i) => v - ax[i] * dot(sub(mid, c), ax));
    tri(p, a, bb, c2, out); tri(p, a, c2, d, out);
  }
  const s0 = vertex(p, c.map((v, i) => v - ax[i] * h)), s1 = vertex(p, c.map((v, i) => v + ax[i] * h));
  for (let j = 0; j < sides; j++) { tri(p, s0, j, (j + 1) % sides, ax.map(v => -v)); tri(p, s1, sides + j, sides + (j + 1) % sides, ax); }
  return p;
}
// Ear-clip a simple 2D polygon (CCW) into triangles.
function earclip(pts) {
  const area = pts.reduce((s, q, i) => { const n = pts[(i + 1) % pts.length]; return s + q[0] * n[1] - n[0] * q[1]; }, 0);
  let idx = pts.map((_, i) => i); if (area < 0) idx.reverse();
  const out = [], crs = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
  const inside = (q, a, b, c) => crs(a, b, q) > 1e-12 && crs(b, c, q) > 1e-12 && crs(c, a, q) > 1e-12;
  let guard = 0;
  while (idx.length > 3 && guard++ < 5000) {
    let clipped = false;
    for (let i = 0; i < idx.length; i++) {
      const ia = idx[(i + idx.length - 1) % idx.length], ib = idx[i], ic = idx[(i + 1) % idx.length];
      const a = pts[ia], b = pts[ib], c = pts[ic];
      if (crs(a, b, c) <= 1e-12) continue;
      if (idx.some(j => j !== ia && j !== ib && j !== ic && inside(pts[j], a, b, c))) continue;
      out.push([ia, ib, ic]); idx.splice(i, 1); clipped = true; break;
    }
    if (!clipped) break;
  }
  if (idx.length === 3) out.push(idx.slice());
  return out;
}
// Side-profile prism: profile is [z, y] points, extruded across X from x0 to x1.
function prism(name, b, color, profile, x0, x1, role) {
  const p = mesh(name, b, color, role), n = profile.length;
  for (const x of [x0, x1]) for (const [z, y] of profile) vertex(p, [x, y, z]);
  const area = profile.reduce((s, q, i) => { const m = profile[(i + 1) % n]; return s + q[0] * m[1] - m[0] * q[1]; }, 0), sgn = area >= 0 ? 1 : -1;
  for (let i = 0; i < n; i++) {
    const j = (i + 1) % n, [z0, y0] = profile[i], [z1, y1] = profile[j];
    const out = [0, -(z1 - z0) * sgn, (y1 - y0) * sgn]; // outward edge normal in the Z/Y plane
    tri(p, i, j, n + j, out); tri(p, i, n + j, n + i, out);
  }
  for (const [a, bb, c] of earclip(profile.map(([z, y]) => [z, y]))) { tri(p, a, bb, c, [-1, 0, 0]); tri(p, n + a, n + bb, n + c, [1, 0, 0]); }
  return p;
}
const box = (name, b, color, [cx, cy, cz], [w, h, d], role) =>
  prism(name, b, color, [[cz - d / 2, cy - h / 2], [cz + d / 2, cy - h / 2], [cz + d / 2, cy + h / 2], [cz - d / 2, cy + h / 2]], cx - w / 2, cx + w / 2, role);

bone('rifle', '', [0, 0, 0]);
bone('mag', 'rifle', [0, -.03, .13]);
bone('charge', 'rifle', [0, .031, 0]);
bone('trigger', 'rifle', [0, -.036, .058]);
bone('selector', 'rifle', [-.0135 * W - .004, -.018, .035]);

const bore = .02, rw = .0145 * W, lw = .0135 * W;
// Upper receiver with flat-top Picatinny rail.
prism('Upper receiver', 'rifle', C.receiver, [[0, 0], [.178, 0], [.178, .034], [.012, .036], [0, .03]], -rw, rw);
prism('Brass deflector', 'rifle', C.receiver, [[.045, .012], [.066, .012], [.058, .03], [.048, .03]], rw - .001, rw + .006);
box('Ejection port', 'rifle', C.port, [rw + .0006, .018, .1], [.002, .018, .058]);
box('Dust cover', 'rifle', C.metal, [rw + .0012, .0095, .1], [.0015, .006, .062]);
cyl('Forward assist', 'rifle', C.metal, [rw + .005, .027, .03], 'x', .0065, .014, 8);
box('Rail base', 'rifle', C.rail, [0, .04, .09], [.022 * W, .008, .174]);
for (let z = .01; z < .175; z += .0104) box('Rail ridge', 'rifle', C.rail, [0, .0465, z + .002], [.024 * W, .005, .0052]);
// Charging handle (animated during reload).
prism('Charging handle', 'charge', C.metal, [[-.018, -.004], [.012, -.004], [.012, .004], [-.012, .006], [-.018, .004]], -.006, .006);
box('Charging latch', 'charge', C.metal, [0, 0, -.015], [.05 * W, .007, .008]);

// Lower receiver: buffer tower, trigger guard area, flared magwell, front pivot.
prism('Lower receiver', 'rifle', C.receiver, [
  [-.018, -.002], [-.018, -.026], [-.004, -.036], [.098, -.036], [.098, -.06], [.168, -.062], [.172, -.034],
  [.19, -.02], [.19, -.003], [.182, 0], [0, 0], [-.004, .016], [-.018, .016]], -lw, lw);
prism('Magwell flare', 'rifle', C.receiver, [[.095, -.056], [.171, -.058], [.172, -.066], [.094, -.064]], -lw - .002, lw + .002);
box('Trigger guard', 'rifle', C.receiver, [0, -.055, .0665], [.008, .006, .063]);
box('Trigger guard rear', 'rifle', C.receiver, [0, -.045, .038], [.008, .018, .006]);
prism('Trigger', 'trigger', C.metal, [[-.004, 0], [.004, 0], [.006, -.01], [.002, -.016], [-.001, -.014], [.001, -.008]], -.0025, .0025);
cyl('Selector', 'rifle', C.metal, [-lw - .002, -.018, .035], 'x', .0055, .004, 8);
box('Selector lever', 'selector', C.metal, [0, .005, .001], [.003, .012, .004]); // rotates with fire mode
cyl('Mag release', 'rifle', C.metal, [lw + .002, -.022, .09], 'x', .0045, .004, 8);
box('Bolt catch', 'rifle', C.metal, [-lw - .002, -.012, .095], [.003, .016, .006]);
cyl('Takedown pin', 'rifle', C.metal, [0, -.005, .012], 'x', .003, lw * 2 + .003, 6);
cyl('Pivot pin', 'rifle', C.metal, [0, -.012, .18], 'x', .003, lw * 2 + .003, 6);

// A2 pistol grip with finger swell.
prism('Pistol grip', 'rifle', C.furniture, [
  [.036, -.036], [.03, -.052], [.024, -.058], [.022, -.066], [.014, -.086], [.004, -.114], [-.001, -.122],
  [-.028, -.122], [-.03, -.114], [-.02, -.08], [-.01, -.05], [-.004, -.036]], -.0135 * W, .0135 * W, 'furniture');

// Buffer tube + collapsible M4 stock (mid position).
loft('Castle nut', 'rifle', C.metal, [[0, .012, -.018, .019, .019], [0, .012, -.029, .019, .019]], 8);
loft('Buffer tube', 'rifle', C.metal, [[0, .012, -.029, .0155, .0155], [0, .012, -.205, .0155, .0155]], 12);
prism('Stock slide', 'rifle', C.furniture, [[-.092, -.009], [-.092, .03], [-.105, .036], [-.236, .038], [-.236, -.009]], -.021 * W, .021 * W, 'furniture');
prism('Stock web', 'rifle', C.furniture, [[-.118, -.008], [-.236, -.008], [-.236, -.078], [-.226, -.08], [-.19, -.058], [-.148, -.03]], -.0135 * W, .0135 * W, 'furniture');
prism('Butt plate', 'rifle', C.furniture, [[-.234, .04], [-.247, .044], [-.247, -.078], [-.234, -.08]], -.022 * W, .022 * W, 'furniture');
prism('Butt pad', 'rifle', C.rubber, [[-.247, .044], [-.255, .042], [-.256, -.076], [-.247, -.078]], -.022 * W, .022 * W);
box('Stock lever', 'rifle', C.metal, [0, -.016, -.13], [.012, .008, .04]);
box('Sling slot', 'rifle', C.port, [0, -.055, -.222], [.0135 * W * 2 + .001, .012, .006]);

// Delta ring, ribbed M4 handguard with heat-shield vents.
loft('Delta ring', 'rifle', C.metal, [[0, bore, .178, .032, .032], [0, bore, .192, .034, .034], [0, bore, .198, .03, .03]], 14);
const guard = [];
for (let i = 0; i <= 10; i++) { const z = .198 + i * .0165, r = (i % 2 ? .0295 : .0315) * 1.02; guard.push([0, bore, z, r * W, r]); }
guard.push([0, bore, .372, .027 * W, .027]);
loft('Handguard', 'rifle', C.furniture, guard, 16, 'furniture', Math.PI / 16);
for (const side of [-1, 1]) for (let i = 0; i < 5; i++) for (const dy of [-.009, .007])
  box('Vent', 'rifle', C.port, [side * .032 * W, bore + dy, .22 + i * .03], [.004, .005, .012]);
// Barrel, A-frame front sight base, A2 birdcage.
loft('Barrel', 'rifle', C.metal, [[0, bore, .37, .0105, .0105], [0, bore, .45, .0105, .0105], [0, bore, .455, .0092, .0092], [0, bore, .548, .0092, .0092]], 10);
loft('Front sight collar', 'rifle', C.metal, [[0, bore, .372, .0165, .0165], [0, bore, .404, .0165, .0165]], 10);
prism('Front sight A-frame', 'rifle', C.metal, [[.374, bore + .01], [.402, bore + .01], [.394, bore + .05], [.392, bore + .058], [.384, bore + .058], [.382, bore + .05]], -.005, .005);
for (const side of [-1, 1]) prism('Sight ear', 'rifle', C.metal, [[.382, bore + .045], [.394, bore + .045], [.393, bore + .07], [.383, bore + .07]], side > 0 ? .0045 : -.0075, side > 0 ? .0075 : -.0045);
box('Front post', 'rifle', C.metal, [0, bore + .064, .388], [.003, .014, .003]);
prism('Bayonet lug', 'rifle', C.metal, [[.378, bore - .012], [.398, bore - .012], [.398, bore - .024], [.382, bore - .024]], -.004, .004);
loft('Flash hider', 'rifle', C.metal, [[0, bore, .546, .0118, .0118], [0, bore, .598, .0118, .0118], [0, bore, .602, .0105, .0105]], 12);
for (let i = 0; i < 4; i++) { const a = Math.PI / 2 + (i - 1.5) * .6; box('Hider slot', 'rifle', C.port, [Math.cos(a) * .0114, bore + Math.sin(a) * .0114, .574], [.0045, .0045, .03]); }

// STANAG 30-round magazine with forward curve (own bone for reload animation).
const back = [], front = [];
for (let i = 0; i <= 8; i++) { const t = i / 8, y = -.012 - .172 * t, bend = .048 * Math.pow(t, 1.7); back.push([-.034 + bend, y]); front.push([.034 + bend, y]); }
prism('Magazine', 'mag', C.mag, [...back, ...front.reverse()], -.0125 * W, .0125 * W, 'mag');
for (let i = 1; i < 7; i++) { const t = i / 8, y = -.012 - .172 * t, bend = .048 * Math.pow(t, 1.7); box('Mag rib', 'mag', C.mag, [0, y, bend], [.0125 * W * 2 + .002, .004, .05], 'mag'); }
prism('Floor plate', 'mag', C.metal, [[.01, -.18], [.087, -.18], [.087, -.192], [.01, -.192]], -.0145 * W, .0145 * W, 'mag');

// Interchangeable optics (0.15.0). One role is visible at a time; M4Model.SetScope picks it.
// Sight points used for aligned ADS live in M4/M4Scopes.cs (SightY / SightZ) and must match these.
const scopeBrass = [.72, .52, .2], coating = [.5, .2, .2], glassDark = [.05, .08, .1];
// Rear flip-up BUIS, ghost-ring aperture 12 mm: centre (0, .091, .021) lines up with the front post tip (.091).
box('BUIS base', 'rifle', C.metal, [0, .0525, .02], [.024 * W, .007, .03]);
box('BUIS leaf', 'rifle', C.metal, [0, .0705, .021], [.017, .029, .004], 'iron-up');
box('BUIS ring top', 'rifle', C.metal, [0, .09825, .021], [.017, .0025, .004], 'iron-up');
for (const side of [-1, 1]) {
  box('BUIS ring side', 'rifle', C.metal, [side * .00725, .091, .021], [.0025, .012, .004], 'iron-up');
  box('BUIS ear', 'rifle', C.metal, [side * .0115, .078, .021], [.003, .044, .012], 'iron-up');
}
box('BUIS folded leaf', 'rifle', C.metal, [0, .0585, .028], [.02, .005, .03], 'iron-down');
// Red dot on a riser (tube sight). Sight point = tube axis (0, .086, .08).
prism('Red dot riser', 'rifle', C.scope, [[.055, .049], [.105, .049], [.1, .068], [.06, .068]], -.012 * W, .012 * W, 'scope-reddot');
box('Red dot saddle', 'rifle', C.scope, [0, .0725, .08], [.018 * W, .01, .03], 'scope-reddot');
pipe('Red dot body', 'rifle', C.scope, [[0, .086, .05, .0165, .0135], [0, .086, .056, .0175, .0145], [0, .086, .104, .0175, .0145], [0, .086, .108, .019, .0158], [0, .086, .114, .019, .0158]], 16, 'scope-reddot');
pipe('Red dot coating', 'rifle', coating, [[0, .086, .1125, .0158, .0142], [0, .086, .1135, .0158, .0142]], 16, 'scope-reddot');
cyl('Red dot elevation', 'rifle', C.scope, [0, .086 + .0215, .08], 'y', .0065, .008, 10, 'scope-reddot');
cyl('Red dot windage', 'rifle', C.scope, [.0215, .086, .08], 'x', .0065, .008, 10, 'scope-reddot');
cyl('Red dot lever', 'rifle', C.metal, [.016 * W, .058, .08], 'x', .005, .008, 8, 'scope-reddot');
// Brass rifle scope on picatinny rings (magnified 3x / 6x).
for (const z of [.04, .14]) {
  box('Brass ring clamp', 'rifle', C.metal, [0, .056, z], [.026 * W, .014, .014], 'scope-brass');
  box('Brass ring post', 'rifle', C.metal, [0, .066, z], [.012, .022, .01], 'scope-brass');
  loft('Brass ring', 'rifle', C.metal, [[0, .085, z - .006, .0175, .0175], [0, .085, z + .006, .0175, .0175]], 14, 'scope-brass');
}
loft('Brass tube', 'rifle', scopeBrass, [[0, .085, -.005, .0135, .0135], [0, .085, .17, .0135, .0135], [0, .085, .2, .021, .021], [0, .085, .235, .021, .021]], 14, 'scope-brass');
loft('Brass eyepiece', 'rifle', scopeBrass, [[0, .085, -.03, .018, .018], [0, .085, -.005, .018, .018], [0, .085, .005, .0135, .0135]], 14, 'scope-brass');
cyl('Brass dial', 'rifle', scopeBrass, [0, .085 + .0175, .09], 'y', .007, .008, 10, 'scope-brass');
loft('Brass front lens', 'rifle', [.1, .2, .22], [[0, .085, .2352, .018, .018], [0, .085, .236, .018, .018]], 14, 'scope-brass');
loft('Brass rear lens', 'rifle', glassDark, [[0, .085, -.0308, .015, .015], [0, .085, -.03, .015, .015]], 14, 'scope-brass');
// Long-range scope on a cantilever mount (3x / 6x / 9x). The mount covers the BUIS, so none is drawn.
prism('Sniper mount', 'rifle', C.scope, [[.005, .049], [.16, .049], [.155, .062], [.01, .062]], -.012 * W, .012 * W, 'scope-sniper');
for (const z of [.03, .13]) {
  box('Sniper ring post', 'rifle', C.scope, [0, .068, z], [.014, .016, .012], 'scope-sniper');
  loft('Sniper ring', 'rifle', C.scope, [[0, .092, z - .008, .0185, .0185], [0, .092, z + .008, .0185, .0185]], 14, 'scope-sniper');
}
loft('Sniper tube', 'rifle', C.scope, [[0, .092, -.02, .015, .015], [0, .092, .2, .015, .015], [0, .092, .225, .026, .026], [0, .092, .275, .026, .026]], 16, 'scope-sniper');
loft('Sniper eyepiece', 'rifle', C.scope, [[0, .092, -.075, .02, .02], [0, .092, -.035, .02, .02], [0, .092, -.02, .015, .015]], 16, 'scope-sniper');
box('Sniper throw lever', 'rifle', C.metal, [.022, .092, -.05], [.012, .006, .01], 'scope-sniper');
cyl('Sniper elevation', 'rifle', C.scope, [0, .092 + .022, .09], 'y', .01, .014, 12, 'scope-sniper');
cyl('Sniper windage', 'rifle', C.scope, [.022, .092, .09], 'x', .01, .014, 12, 'scope-sniper');
cyl('Sniper parallax', 'rifle', C.scope, [-.02, .092, .1], 'x', .008, .01, 12, 'scope-sniper');
loft('Sniper front lens', 'rifle', [.12, .28, .3], [[0, .092, .2752, .023, .023], [0, .092, .276, .023, .023]], 16, 'scope-sniper');
loft('Sniper rear lens', 'rifle', glassDark, [[0, .092, -.0758, .016, .016], [0, .092, -.075, .016, .016]], 16, 'scope-sniper');

const tris = parts.reduce((s, p) => s + p.triangles.length / 3, 0);
fs.writeFileSync(path.join(__dirname, '..', 'Assets', 'model.json'), JSON.stringify({ bones, parts }));
console.log('parts', parts.length, 'triangles', tris);

// Inventory icon prop for 5.56 rounds: olive ammo can with loose cartridges (icon rendering only).
bones.length = 0; parts.length = 0;
bone('rifle', '', [0, 0, 0]);
const olive = [.29, .34, .2], oliveDark = [.2, .24, .14], stencil = [.86, .74, .3], brassC = [.8, .6, .24], copper = [.72, .38, .2];
box('Can body', 'rifle', olive, [0, .045, 0], [.075, .09, .15]);
box('Can lid', 'rifle', oliveDark, [0, .094, 0], [.08, .012, .155]);
box('Can latch', 'rifle', oliveDark, [0, .075, .079], [.03, .03, .008]);
box('Can handle', 'rifle', oliveDark, [0, .104, 0], [.016, .008, .07]);
box('Stencil band', 'rifle', stencil, [.0377, .05, 0], [.001, .014, .12]);
// Cartridges are authored along Z, then rotated upright or laid on the lid.
function upright(x, z, h) {
  const c = loft('Case', 'rifle', brassC, [[0, 0, 0, .0048, .0048], [0, 0, h * .72, .0048, .0048], [0, 0, h * .8, .0032, .0032]], 10);
  const t = loft('Bullet', 'rifle', copper, [[0, 0, h * .8, .0029, .0029], [0, 0, h * .93, .0022, .0022], [0, 0, h, .0006, .0006]], 10);
  for (const p of [c, t]) { const v = p.vertices; for (let i = 0; i < v.length; i += 3) { const px = v[i], py = v[i + 1], pz = v[i + 2]; v[i] = px + x; v[i + 1] = pz; v[i + 2] = -py + z; } }
}
function lying(x, y, z, h, yaw) {
  const c = loft('Case', 'rifle', brassC, [[0, 0, 0, .0048, .0048], [0, 0, h * .72, .0048, .0048], [0, 0, h * .8, .0032, .0032]], 10);
  const t = loft('Bullet', 'rifle', copper, [[0, 0, h * .8, .0029, .0029], [0, 0, h * .93, .0022, .0022], [0, 0, h, .0006, .0006]], 10);
  const cs = Math.cos(yaw), sn = Math.sin(yaw);
  for (const p of [c, t]) { const v = p.vertices; for (let i = 0; i < v.length; i += 3) { const px = v[i], py = v[i + 1], pz = v[i + 2]; v[i] = x + px * cs + pz * sn; v[i + 1] = y + py; v[i + 2] = z - px * sn + pz * cs; } }
}
for (const z of [-.04, -.012, .016, .044]) upright(.056, z, .057);
lying(-.01, .105, -.03, .057, .5); lying(.012, .105, .0, .057, -.3);
fs.writeFileSync(path.join(__dirname, '..', 'Assets', 'ammo-icon.json'), JSON.stringify({ bones, parts }));
