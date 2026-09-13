import json, math, sys
import numpy as np
from PIL import Image, ImageDraw
m=json.load(open(sys.argv[1],encoding='utf-8-sig'))
W,H=1600,1050
image=Image.new('RGB',(W,H),'#e3dfd4');draw=ImageDraw.Draw(image)
def rot(r):
 x,y,z=np.radians(r);cx,sx=math.cos(x),math.sin(x);cy,sy=math.cos(y),math.sin(y);cz,sz=math.cos(z),math.sin(z)
 return np.array([[cz,-sz,0],[sz,cz,0],[0,0,1]])@np.array([[cy,0,sy],[0,1,0],[-sy,0,cy]])@np.array([[1,0,0],[0,cx,-sx],[0,sx,cx]])
bones={}
for b in m['bones']:bones[b['name']]=np.array(b['p'])+(bones[b['parent']] if b['parent'] else 0)
for centre,yaw,scale,label in [(420,-40,230,'THREE-QUARTER'),(1170,-90,205,'SIDE')]:
 camera=rot([-8,yaw,0]);triangles=[]
 draw.ellipse((centre-260,865,centre+260,910),fill='#c9c4b8')
 # Depth-buffered rasterization avoids painter-order errors at intersecting parts.
 depth=np.full((H,W),-np.inf);pixels=np.array(image)
 for p in m['parts']:
  verts=np.array(p['vertices']).reshape(-1,3)*p['s'];verts=verts@rot(p['r']).T+np.array(p['p'])+bones[p['bone']]
  for ids in np.array(p['triangles']).reshape(-1,3):
   v=verts[ids];normal=np.cross(v[1]-v[0],v[2]-v[0]);normal/=np.linalg.norm(normal)
   q=(v-np.array([0,1.5,0]))@camera.T
   if (normal@camera.T)[2]<=0:continue
   light=.66+.34*max(0,np.dot(normal,np.array([-.4,.8,.45])))
   color=tuple(int(min(255,c*255*light+8)) for c in p['color'])
   pts=np.array([(centre+a*scale,510-b*scale) for a,b,c in q]);lo=np.maximum([0,0],np.floor(pts.min(axis=0)).astype(int));hi=np.minimum([W-1,H-1],np.ceil(pts.max(axis=0)).astype(int))
   if (hi<lo).any():continue
   xx,yy=np.meshgrid(np.arange(lo[0],hi[0]+1)+.5,np.arange(lo[1],hi[1]+1)+.5)
   a,b,c=pts;den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
   if abs(den)<1e-9:continue
   u=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den;v=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den;w=1-u-v
   z=u*q[0,2]+v*q[1,2]+w*q[2,2];region=depth[lo[1]:hi[1]+1,lo[0]:hi[0]+1];mask=(u>=0)&(v>=0)&(w>=0)&(z>region)
   region[mask]=z[mask];pixels[lo[1]:hi[1]+1,lo[0]:hi[0]+1][mask]=color
 image=Image.fromarray(pixels);draw=ImageDraw.Draw(image)
 draw.text((centre-65,960),label,fill='#51483f')
draw.text((40,30),'ACTUAL MESH / OFFLINE LIGHTING / NOT AN IN-GAME SCREENSHOT',fill='#51483f')
image.save(sys.argv[2])
