import json, math, sys, pathlib
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = pathlib.Path(__file__).parent
ASSETS = HERE.parent / 'Assets'
model = json.load(open(ASSETS / 'model.json', encoding='utf-8'))

def load(name):
    global model
    model = json.load(open(ASSETS / name, encoding='utf-8'))
FONT = 'C:/Windows/Fonts/msjh.ttc'; FONT_B = 'C:/Windows/Fonts/msjhbd.ttc'
FDE = {'furniture': [.60, .51, .36], 'mag': [.56, .48, .34]}

SCOPES = ['iron', 'reddot', 'brass', 'sniper']
def visible(role, scope):
    # Mirrors M4Model.SetScope: one optic role, raised BUIS only without optic, folded BUIS except under the sniper mount.
    if role is None or not (role.startswith('scope-') or role.startswith('iron-')): return True
    if role == 'iron-up': return scope == 'iron'
    if role == 'iron-down': return scope not in ('iron', 'sniper')
    return role == 'scope-' + scope

def world_parts(colorway=None, hide=(), scope='iron', only=None):
    bones = {}
    for b in model['bones']:
        bones[b['name']] = np.array(b['p'], float) + (bones[b['parent']] if b['parent'] else 0)
    out = []
    for i, p in enumerate(model['parts']):
        if p.get('role') in hide or not visible(p.get('role'), scope): continue
        if only is not None and p.get('role') not in only: continue
        v = np.array(p['vertices'], float).reshape(-1, 3) * p['s'] + np.array(p['p']) + bones[p['bone']]
        v[:, 0] *= -1  # data is Unity left-handed (+X right); mirror into this right-handed rasteriser
        color = np.array(colorway.get(p.get('role'), p['color']) if colorway else p['color'], float)
        metal = p.get('role') not in ('furniture', 'mag') and p['name'] != 'Butt pad'
        out.append((i, v, np.array(p['triangles']).reshape(-1, 3)[:, ::-1], color, metal))
    return out

def look(eye, target, up=(0, 1, 0)):
    eye, target, up = map(lambda a: np.array(a, float), (eye, target, up))
    f = target - eye; f /= np.linalg.norm(f)
    r = np.cross(f, up); r /= np.linalg.norm(r)
    u = np.cross(r, f)
    return eye, np.stack([r, u, -f])

def render(size, eye, target, scale, persp=None, colorway=None, hide=(), light=(.35, .8, .55), offset=(0, 0), gain=1.0, scope='iron', only=None):
    """Rasterise with a z-buffer at 2x, outline part silhouettes, return RGBA."""
    W, H = size[0] * 2, size[1] * 2
    M = np.array([-1, 1, 1.])  # camera arguments are given in Unity space too
    eye, R = look(np.array(eye) * M, np.array(target) * M)
    rgb = np.zeros((H, W, 3)); depth = np.full((H, W), -np.inf); ids = np.full((H, W), -1)
    L = np.array(light, float); L /= np.linalg.norm(L)
    for pid, verts, tris, color, metal in world_parts(colorway, hide, scope, only):
        cam = (verts - eye) @ R.T
        if persp:
            zc = -cam[:, 2]
            sx = W / 2 + offset[0] * 2 + persp * 2 * cam[:, 0] / zc
            sy = H / 2 + offset[1] * 2 - persp * 2 * cam[:, 1] / zc
            dz = -zc
        else:
            sx = W / 2 + offset[0] * 2 + scale * 2 * cam[:, 0]
            sy = H / 2 + offset[1] * 2 - scale * 2 * cam[:, 1]
            dz = cam[:, 2]; zc = None
        for t in tris:
            if persp and (zc[t] < .03).any(): continue  # near-plane cull
            v = verts[t]; n = np.cross(v[1] - v[0], v[2] - v[0]); ln = np.linalg.norm(n)
            if ln < 1e-12: continue
            n /= ln
            view = eye - v.mean(axis=0); view /= np.linalg.norm(view)
            if n @ view <= 0: continue
            diff = max(0.0, n @ L); fill = .5 + .5 * max(0.0, n @ np.array([-.5, .3, -.4]))
            shade = color * (.34 + .78 * diff + .12 * fill)
            if metal:
                h = L + view; h /= np.linalg.norm(h)
                shade = shade + .35 * max(0.0, n @ h) ** 24
            shade = np.clip(shade * gain + .015, 0, 1)
            a, b, c = np.stack([sx[t], sy[t]], 1)
            lo = np.maximum([0, 0], np.floor(np.min([a, b, c], 0)).astype(int))
            hi = np.minimum([W - 1, H - 1], np.ceil(np.max([a, b, c], 0)).astype(int))
            if (hi < lo).any(): continue
            xx, yy = np.meshgrid(np.arange(lo[0], hi[0] + 1) + .5, np.arange(lo[1], hi[1] + 1) + .5)
            den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
            if abs(den) < 1e-9: continue
            w0 = ((b[1] - c[1]) * (xx - c[0]) + (c[0] - b[0]) * (yy - c[1])) / den
            w1 = ((c[1] - a[1]) * (xx - c[0]) + (a[0] - c[0]) * (yy - c[1])) / den
            w2 = 1 - w0 - w1
            z = w0 * dz[t[0]] + w1 * dz[t[1]] + w2 * dz[t[2]]
            reg = depth[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1]
            m = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6) & (z > reg)
            reg[m] = z[m]
            rgb[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1][m] = shade
            ids[lo[1]:hi[1] + 1, lo[0]:hi[0] + 1][m] = pid
    # Ink outline on part boundaries and silhouette.
    edge = np.zeros((H, W), bool)
    for dy, dx in ((0, 1), (1, 0), (1, 1)):
        a = ids[:H - dy, :W - dx]; b = ids[dy:, dx:]
        e = a != b
        edge[:H - dy, :W - dx] |= e; edge[dy:, dx:] |= e
    alpha = (ids >= 0).astype(float)
    ink = np.array([.05, .045, .05])
    sil = edge & (ids < 0)
    rgb[edge & (ids >= 0)] = rgb[edge & (ids >= 0)] * .35 + ink * .65
    rgb[sil] = ink; alpha[sil] = 1
    img = Image.fromarray(np.dstack([rgb * 255, alpha * 255]).astype(np.uint8), 'RGBA')
    return img.resize(size, Image.LANCZOS)

def shadow(sheet, box, strength=90):
    layer = Image.new('RGBA', sheet.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).ellipse(box, fill=(40, 30, 20, strength))
    sheet.alpha_composite(layer.filter(ImageFilter.GaussianBlur(18)))

def sheet(out):
    Wd, Hd = 2000, 1250
    bg = Image.new('RGBA', (Wd, Hd), (235, 226, 207, 255))
    grad = np.linspace(0, 1, Hd)[:, None, None]
    top, bot = np.array([240, 233, 216]), np.array([214, 202, 178])
    bg = Image.fromarray((top * (1 - grad) + bot * grad).repeat(Wd, 1).astype(np.uint8)).convert('RGBA')
    d = ImageDraw.Draw(bg)
    f_title = ImageFont.truetype(FONT_B, 54); f_sub = ImageFont.truetype(FONT, 26); f_lab = ImageFont.truetype(FONT_B, 24); f_small = ImageFont.truetype(FONT, 21)
    ink = (48, 38, 30)
    d.text((70, 52), 'M4A1 卡賓槍 · 概念模型', font=f_title, fill=ink)
    tris = sum(len(p['triangles']) // 3 for p in model['parts'])
    d.text((74, 124), f'Ale and Tale Tavern 模組 · 原創低多邊形 · {len(model["parts"])} 部件 / {tris:,} 三角面 · 實際 mesh 離線渲染，非遊戲截圖', font=f_sub, fill=(102, 88, 72))

    # Hero three-quarter (right side, muzzle towards viewer-left).
    centre = np.array([0, .0, .17])
    hero = render((1180, 620), centre + np.array([.95, .42, -.62]), centre, 1140)
    shadow(bg, (170, 700, 1170, 770))
    bg.alpha_composite(hero, (40, 150))
    d.text((80, 745), '主視角 · 機械瞄具 / 30 發 STANAG 彈匣 / 伸縮槍托', font=f_lab, fill=ink)

    # Right side profile, orthographic, with dimension line.
    prof = render((1180, 430), centre + np.array([2, 0, -.0]), centre + np.array([0, -.045, 0]), 1060)
    bg.alpha_composite(prof, (40, 790))
    px = lambda z: 40 + 590 + (z - .17) * 1060  # muzzle points right in right-side view
    y = 1200
    d.line((px(.602), y, px(-.256), y), fill=ink, width=3)
    for z in (.602, -.256): d.line((px(z), y - 12, px(z), y + 12), fill=ink, width=3)
    d.text((px(.17) - 180, y + 12), '全長 0.84 m（槍托拉出）· 槍管 368 mm', font=f_small, fill=ink)
    d.text((80, 800), '右側 · 拋殼口 / 前推輔助 / 導氣管', font=f_lab, fill=ink)

    # Right column: first-person mockup and FDE colourway.
    panel = Image.new('RGBA', (700, 470), (42, 50, 46, 255))
    pd = ImageDraw.Draw(panel)
    for i in range(470): pd.line((0, i, 700, i), fill=(int(70 - i * .05), int(88 - i * .06), int(82 - i * .07), 255))
    pd.rectangle((0, 300, 700, 470), fill=(58, 68, 50, 255))
    fp = render((700, 470), np.array([-.085, .15, -.13]), np.array([-.07, .09, 1.0]), 0, persp=400)
    panel.alpha_composite(fp)
    pd = ImageDraw.Draw(panel)
    pd.ellipse((345, 230, 355, 240), outline=(255, 255, 255, 200), width=2)
    bg.alpha_composite(panel, (1250, 170))
    d.rectangle((1250, 170, 1950, 640), outline=ink, width=3)
    d.text((1262, 648), '第一人稱持槍示意（手部沿用原生火槍動畫骨架）', font=f_small, fill=ink)

    fde = render((700, 360), centre + np.array([.95, .42, -.62]), centre, 700, colorway=FDE)
    shadow(bg, (1330, 1040, 1880, 1080), 70)
    bg.alpha_composite(fde, (1250, 720))
    d.text((1262, 700), '配色 B · 沙色 FDE 護木／槍托／彈匣', font=f_lab, fill=ink)
    bg.convert('RGB').save(out, quality=95)

def turntable_frames(n, out_dir):
    out_dir.mkdir(exist_ok=True)
    centre = np.array([0, .0, .17])
    for i in range(n):
        a = i * 2 * math.pi / n
        eye = centre + np.array([math.cos(a) * 1.1, .38, math.sin(a) * 1.1])
        render((900, 520), eye, centre, 1000).save(out_dir / f'frame{i:02d}.png')

def glow(img, radius=3, color=(236, 228, 208), strength=150):
    a = img.getchannel('A').filter(ImageFilter.MaxFilter(radius * 2 + 1)).filter(ImageFilter.GaussianBlur(radius / 2))
    halo = Image.new('RGBA', img.size, color + (0,)); halo.putalpha(a.point(lambda v: v * strength // 255))
    halo.alpha_composite(img); return halo

def fit_icon(img, angle, box=236, size=256):
    img = img.crop(img.getbbox()).rotate(angle, resample=Image.BICUBIC, expand=True)
    img = img.crop(img.getbbox())
    k = box / max(img.size); img = img.resize((max(1, round(img.width * k)), max(1, round(img.height * k))), Image.LANCZOS)
    out = Image.new('RGBA', (size, size), (0, 0, 0, 0))
    out.alpha_composite(img, ((size - img.width) // 2, (size - img.height) // 2))
    return out

def icons():
    load('model.json')
    side = render((1800, 800), np.array([2.0, .02, .17]), np.array([0, -.04, .17]), 1900, gain=1.45)
    glow(fit_icon(side, 34, 228)).save(ASSETS / 'm4-icon.png')
    load('ammo-icon.json')
    c = np.array([.01, .05, 0])
    can = render((900, 900), c + np.array([.55, .42, .5]), c, 3600)
    fit_icon(can, 0, 220).save(ASSETS / 'ammo-icon.png')
    load('model.json')

SCOPE_NAMES = {'iron': '不裝（機械瞄具，出廠）', 'reddot': '紅點', 'brass': '黃銅鏡 3×/6×', 'sniper': '狙擊鏡 3×/6×/9×'}
# Sight points and eye distances (M4 metres) from M4/M4Scopes.cs; eye = world eye distance / 0.68 fp scale.
ADS = {'iron': (.091, .021, .06), 'reddot': (.086, .08, .15)}

def scope_sheet(out):
    load('model.json')
    Wd, Hd = 1800, 130 + (len(SCOPES) + 1) // 2 * 350
    grad = np.linspace(0, 1, Hd)[:, None, None]
    bg = Image.fromarray((np.array([240, 233, 216]) * (1 - grad) + np.array([214, 202, 178]) * grad).repeat(Wd, 1).astype(np.uint8)).convert('RGBA')
    d = ImageDraw.Draw(bg); ink = (48, 38, 30)
    f_title = ImageFont.truetype(FONT_B, 46); f_lab = ImageFont.truetype(FONT_B, 26)
    d.text((60, 40), 'M4A1 可換式瞄準鏡', font=f_title, fill=ink)
    centre = np.array([0, .03, .17])
    for i, sc in enumerate(SCOPES):
        x, y = 30 + (i % 2) * 885, 120 + (i // 2) * 350
        img = render((860, 320), centre + np.array([2.0, .25, .0]), centre, 1150, scope=sc)
        bg.alpha_composite(img, (x, y + 10))
        d.text((x + 30, y), SCOPE_NAMES[sc], font=f_lab, fill=ink)
    bg.convert('RGB').save(out, quality=92)

def ads_views(out):
    load('model.json')
    tiles = []
    f_lab = ImageFont.truetype(FONT_B, 22)
    for sc, (y, z, eye) in ADS.items():
        eye_m = eye / .68
        cam = np.array([0, y, z - eye_m])
        fov = 70 / (1.3 if sc == 'iron' else 1.5)
        focal = 300 / np.tan(np.radians(fov / 2))
        img = render((600, 600), cam, cam + np.array([0, 0, 1]), 0, persp=focal / 2, scope=sc)
        tile = Image.new('RGBA', (600, 600), (150, 170, 180, 255)); tile.alpha_composite(img)
        td = ImageDraw.Draw(tile)
        td.line((290, 300, 310, 300), fill=(255, 0, 0, 255)); td.line((300, 290, 300, 310), fill=(255, 0, 0, 255))
        td.text((12, 10), sc + '  (red cross = screen centre)', font=f_lab, fill=(20, 20, 20))
        tiles.append(tile)
    sheet = Image.new('RGBA', (600 * len(tiles), 600)); [sheet.alpha_composite(t, (i * 600, 0)) for i, t in enumerate(tiles)]
    sheet.convert('RGB').save(out, quality=92)

def scope_icons():
    load('model.json')
    for sc in SCOPES[1:]:
        c = {'reddot': [0, .08, .08], 'brass': [0, .08, .1], 'sniper': [0, .09, .1]}[sc]
        c = np.array(c)
        view = np.array([.55, .35, -.45])
        img = render((900, 900), c + view, c, 3000, scope=sc, only={'scope-' + sc}, gain=1.35)
        glow(fit_icon(img, 0, 220)).save(ASSETS / ('scope-' + sc + '.png'))

if __name__ == '__main__':
    what = sys.argv[1] if len(sys.argv) > 1 else 'sheet'
    if what == 'sheet': sheet(HERE / 'm4-concept.png')
    elif what == 'icons': icons(); scope_icons()
    elif what == 'scopes': scope_sheet(HERE.parent / 'Assets' / 'm4-scopes.jpg')
    elif what == 'ads': ads_views(HERE.parent / 'Assets' / 'm4-ads.jpg')
    elif what == 'hero': render((1400, 760), np.array([.95, .42, -.45]), np.array([0, 0, .17]), 1350).save(HERE / 'm4-hero.png')
