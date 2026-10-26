"""Generates a tileable painted cinder-block texture set (1.6 m x 1.6 m: 4 blocks x 8 rows)."""
import os
import sys

import numpy as np
from PIL import Image, ImageFilter

OUT = sys.argv[1]
SIZE, BLOCKS_X, ROWS = 2048, 4, 8
MORTAR = 0.012 / 1.6 * SIZE  # 12 mm joints, in pixels
rng = np.random.default_rng(7)


def tileable_noise(scale, octaves=4):
    total = np.zeros((SIZE, SIZE))
    amp = 1.0
    for o in range(octaves):
        cells = scale * 2 ** o
        grid = rng.random((cells, cells))
        img = Image.fromarray((grid * 255).astype(np.uint8)).resize((SIZE, SIZE), Image.BICUBIC)
        # wrap-around blur keeps the noise tileable
        arr = np.asarray(img, dtype=float) / 255.0
        total += arr * amp
        amp *= 0.5
    total -= total.min()
    return total / total.max()


yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(float)
row_h = SIZE / ROWS
block_w = SIZE / BLOCKS_X
row = np.floor(yy / row_h)
offset = (row % 2) * block_w / 2
bx = (xx + offset) % block_w
by = yy % row_h
dist = np.minimum(np.minimum(bx, block_w - bx), np.minimum(by, row_h - by))
groove = np.clip((dist - MORTAR / 2) / (MORTAR * 0.8), 0, 1)  # 0 in joint, 1 on block face

block_id = (np.floor((xx + offset) / block_w) + row * 13) % 97
tone = (block_id * 0.6180339) % 1.0
pores = tileable_noise(64, 3)
blotch = tileable_noise(6, 4)

height = groove * 0.8 + pores * 0.12 + blotch * 0.08
paint = np.array([0.80, 0.80, 0.78])
color = paint[None, None, :] * (0.92 + 0.05 * tone[..., None] + 0.06 * (blotch[..., None] - 0.5))
color = color * (0.82 + 0.18 * groove[..., None]) - 0.06 * (1 - pores[..., None]) * 0.3
Image.fromarray((np.clip(color, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "painted_block_diff.png"))

gy, gx = np.gradient(height)
strength = 6.0
n = np.stack([-gx * strength, gy * strength, np.ones_like(gx)], axis=-1)
n /= np.linalg.norm(n, axis=-1, keepdims=True)
Image.fromarray(((n * 0.5 + 0.5) * 255).astype(np.uint8)).filter(ImageFilter.SMOOTH).save(os.path.join(OUT, "painted_block_nor_gl.png"))

rough = 0.72 + 0.15 * (1 - groove) + 0.08 * pores
Image.fromarray((np.clip(rough, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "painted_block_rough.png"))
ao = 0.75 + 0.25 * groove
Image.fromarray((np.clip(ao, 0, 1) * 255).astype(np.uint8)).save(os.path.join(OUT, "painted_block_ao.png"))
print("written to", OUT)
