"""Deterministic, tileable PBR base colors for the authored low-poly model."""
from pathlib import Path
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parents[1] / 'Assets'
size = 512
rng = np.random.default_rng(47940)

def noise(cells):
    grid = rng.random((cells, cells))
    at = np.arange(size) * cells / size
    i = at.astype(int)
    f = at - i
    f = f * f * (3 - 2 * f)
    a = grid[i[:, None] % cells, i[None, :] % cells]
    b = grid[i[:, None] % cells, (i[None, :] + 1) % cells]
    c = grid[(i[:, None] + 1) % cells, i[None, :] % cells]
    d = grid[(i[:, None] + 1) % cells, (i[None, :] + 1) % cells]
    return (a*(1-f)[None, :] + b*f[None, :])*(1-f)[:, None] + (c*(1-f)[None, :] + d*f[None, :])*f[:, None]

coarse, medium, fine = noise(8), noise(25), noise(90)
patina = coarse*.55 + medium*.32 + fine*.13
rust = np.clip(1-np.abs(patina-.46)/.035,0,1)*.72
bronze = np.array([126, 153, 148]) + ((coarse-.5)*31+(fine-.5)*19)[:,:,None]
bronze = bronze*(1-rust[:,:,None])+np.array([109,88,62])*rust[:,:,None]
Image.fromarray(np.clip(bronze,0,255).astype('uint8')).save(root/'bronze.png')

stone = np.array([124, 72, 54]) + ((coarse-.5)*33+(medium-.5)*20+(fine-.5)*12)[:, :, None]
# Periodic distorted Voronoi boundaries create fine interconnected mineral veins.
yy, xx = np.mgrid[0:size, 0:size] / size
xx = (xx + .017*np.sin(yy*19*np.pi) + (medium-.5)*.035) % 1
yy = (yy + .015*np.sin(xx*23*np.pi) + (fine-.5)*.016) % 1
nearest = np.full((size,size), np.inf)
second = nearest.copy()
for px,py in rng.random((58,2)):
    dx = np.abs(xx-px); dy = np.abs(yy-py)
    dist = np.minimum(dx,1-dx)**2 + np.minimum(dy,1-dy)**2
    second = np.minimum(second, np.maximum(nearest,dist))
    nearest = np.minimum(nearest,dist)
veins = np.clip(1-(np.sqrt(second)-np.sqrt(nearest))/.0028,0,1)*.73
stone = stone*(1-veins[:,:,None])+np.array([194,175,148])*veins[:,:,None]
Image.fromarray(np.clip(stone,0,255).astype('uint8')).save(root/'red-stone.png')
print('Generated two 512 x 512 seamless material textures')
