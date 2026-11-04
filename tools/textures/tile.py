"""Make a generated texture image tile seamlessly, and derive a normal map.
python tile.py <in.png> <out_dir> <name> [normal_strength] [saturation]
Writes <name>_albedo.png (1024, seamless), <name>_normal.png (tangent space, wraps), <name>_tiled3x3.jpg (check).
Seam removal: blend the image with itself rolled by half, weighted by a smooth window that is 1 in the
middle and 0 at the borders, so the borders come from the image's own interior (which wraps cleanly)."""
import sys, os
import numpy as np
from PIL import Image, ImageEnhance

src, out, name = sys.argv[1], sys.argv[2], sys.argv[3]
strength = float(sys.argv[4]) if len(sys.argv) > 4 else 2.0
sat = float(sys.argv[5]) if len(sys.argv) > 5 else 1.0
os.makedirs(out, exist_ok=True)
im = Image.open(src).convert("RGB")
W0, H0 = im.size
m = int(min(W0, H0) * 0.02)  # generated images often carry a faint frame line
im = im.crop((m, m, W0 - m, H0 - m)).resize((1024, 1024), Image.LANCZOS)
if sat != 1.0:
    im = ImageEnhance.Color(im).enhance(sat)
a = np.asarray(im).astype(np.float32) / 255.0
n = a.shape[0]
b = np.roll(np.roll(a, n // 2, 0), n // 2, 1)
t = np.linspace(0, 1, n)
w1 = np.clip(np.minimum(t, 1 - t) / 0.22, 0, 1)        # 0 at the border, 1 after 22% in
w1 = w1 * w1 * (3 - 2 * w1)                               # smoothstep
w = np.outer(w1, w1)[..., None]
# add a little noise to the weight so the blend seam is not a straight cross
rng = np.random.default_rng(1)
noise = rng.random((n // 32, n // 32)).astype(np.float32)
noise = np.asarray(Image.fromarray((noise * 255).astype(np.uint8)).resize((n, n), Image.BICUBIC)).astype(np.float32) / 255.0
w = np.clip(w + (noise[..., None] - 0.5) * 0.35 * (w * (1 - w)) * 4, 0, 1)
c = a * w + b * (1 - w)
# keep contrast: the blend averages detail away in the mixed zone; restore local variance a bit
blur = np.asarray(Image.fromarray((c * 255).astype(np.uint8)).filter(__import__("PIL.ImageFilter", fromlist=["x"]).GaussianBlur(6))).astype(np.float32) / 255
mix = (w * (1 - w) * 4)
c = np.clip(c + (c - blur) * 0.6 * mix, 0, 1)
alb = Image.fromarray((c * 255 + 0.5).astype(np.uint8))
alb.save(os.path.join(out, name + "_albedo.png"))
# normal map from luminance, wrapping at the edges
h = (c[..., 0] * 0.3 + c[..., 1] * 0.59 + c[..., 2] * 0.11)
hb = np.asarray(Image.fromarray((h * 255).astype(np.uint8)).filter(__import__("PIL.ImageFilter", fromlist=["x"]).GaussianBlur(1.2))).astype(np.float32) / 255
dx = (np.roll(hb, -1, 1) - np.roll(hb, 1, 1)) * strength
dy = (np.roll(hb, -1, 0) - np.roll(hb, 1, 0)) * strength
nz = np.ones_like(dx)
nv = np.stack([-dx, dy, nz], -1)
nv /= np.linalg.norm(nv, axis=-1, keepdims=True)
Image.fromarray(((nv * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)).save(os.path.join(out, name + "_normal.png"))
# 3x3 check at half size
small = alb.resize((341, 341))
sheet = Image.new("RGB", (1023, 1023))
for i in range(3):
    for j in range(3):
        sheet.paste(small, (i * 341, j * 341))
sheet.save(os.path.join(out, name + "_tiled3x3.jpg"), quality=85)
print("TILE", name)
