"""Offline orthographic geometry QA; renders the actual JSON, not concept art."""
import json
import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import numpy as np

root = Path(__file__).resolve().parents[1]
data = json.loads((root / 'Assets/model.json').read_text())
offsets = {}
for bone in data['bones']:
    parent = offsets.get(bone['parent'], [0, 0, 0])
    offsets[bone['name']] = [parent[i] + bone['p'][i] for i in range(3)]
scale = 2
im = Image.new('RGB', (1440 * scale, 850 * scale), '#101a25')
draw = ImageDraw.Draw(im)
font = ImageFont.truetype('C:/Windows/Fonts/msjh.ttc', 24 * scale)
small = ImageFont.truetype('C:/Windows/Fonts/msjh.ttc', 17 * scale)
draw.text((40*scale, 25*scale), '十魚架 · 實際低面數模型', font=font, fill='#ead6af')
for view, angle in enumerate([0, -35, 90]):
    faces = []
    canvas=np.array(im)
    depths=np.full((im.height, im.width), -np.inf, dtype=np.float32)
    yaw = math.radians(angle)
    def project(v):
        x, y, z = v
        x, z = x*math.cos(yaw)+z*math.sin(yaw), -x*math.sin(yaw)+z*math.cos(yaw)
        y, z = y*.985-z*.174, y*.174+z*.985
        return (int((240+view*480+x*195)*scale), int((740-y*210)*scale), z)
    for part in data['parts']:
        raw = part['vertices']; off = offsets[part['bone']]
        vv = [[raw[i+k]+off[k] for k in range(3)] for i in range(0,len(raw),3)]
        for at in range(0,len(part['triangles']),3):
            pts=[vv[i] for i in part['triangles'][at:at+3]]
            a,b,c=pts
            u=[b[i]-a[i] for i in range(3)]; v=[c[i]-a[i] for i in range(3)]
            n=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
            length=math.sqrt(sum(x*x for x in n)) or 1
            light=.5+.5*max(0,sum(n[i]/length*[-.3,.7,.65][i] for i in range(3)))
            rgb=data['materials'][part['material']]
            if part['material'] in [3,5]: light=1
            color=tuple(min(255,int(c*255*light)) for c in rgb)
            p=[project(v) for v in pts]
            faces.append((p,color))
    for points,color in faces:
        (ax,ay,az),(bx,by,bz),(cx,cy,cz)=points
        lo_x=max(0,min(ax,bx,cx)); hi_x=min(im.width-1,max(ax,bx,cx))
        lo_y=max(0,min(ay,by,cy)); hi_y=min(im.height-1,max(ay,by,cy))
        det=(by-cy)*(ax-cx)+(cx-bx)*(ay-cy)
        if abs(det)<1 or lo_x>hi_x or lo_y>hi_y: continue
        yy,xx=np.mgrid[lo_y:hi_y+1,lo_x:hi_x+1]
        u=((by-cy)*(xx-cx)+(cx-bx)*(yy-cy))/det
        v=((cy-ay)*(xx-cx)+(ax-cx)*(yy-cy))/det
        w=1-u-v
        z=u*az+v*bz+w*cz
        tile=depths[lo_y:hi_y+1,lo_x:hi_x+1]
        mask=(u>=0)&(v>=0)&(w>=0)&(z>tile)
        tile[mask]=z[mask]
        canvas[lo_y:hi_y+1,lo_x:hi_x+1][mask]=color
    im=Image.fromarray(canvas); draw=ImageDraw.Draw(im)
    draw.text(((170+view*480)*scale,780*scale), ['正面','斜側面','側面'][view],font=small,fill='#9bc4ca')
im.resize((1440,850),Image.Resampling.LANCZOS).save(root/'Assets/model-preview.png')
