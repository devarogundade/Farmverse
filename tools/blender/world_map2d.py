"""A clean 2D map of the world (PNG) from world_layout and the blockout's block list:
coast, beaches, river, hills, roads, railway, every section with its name and plot count,
the town's buildings and a legend. Plain Python + Pillow, no Blender needed.

    python world_map2d.py <world_blocks.json> <out.png>
"""
import json
import math
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, __file__.rsplit("/", 1)[0])
import world_layout as L  # noqa: E402

S = 2400
SCALE = 5.2          # pixels per metre
CX, CY = S / 2 - 40, S / 2 + 20


def px(x, y):
    return (CX + x * SCALE, CY - y * SCALE)


def font(size, bold=False):
    for name in ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf" % ("-Bold" if bold else "")):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            continue
    return ImageFont.load_default()


def poly(r_off=0.0):
    pts = []
    for k in range(360):
        a = math.radians(k)
        r = L.coast_radius(a) - 14 + r_off
        pts.append(px(math.cos(a) * r, math.sin(a) * r))
    return pts


def main(blocks_path, out):
    data = json.load(open(blocks_path))
    cats = data["categories"]
    img = Image.new("RGB", (S, S), (120, 190, 235))
    d = ImageDraw.Draw(img)
    d.polygon(poly(10), fill=(235, 220, 170))
    d.polygon(poly(-6), fill=(140, 200, 100))
    for hx, hy, hh, hr in L.HILLS:
        for k in range(5, 0, -1):
            r = hr * k / 5 * 0.9
            g = int(130 - k * 6)
            d.ellipse([px(hx - r, hy + r), px(hx + r, hy - r)], fill=(g - 30, g + 30, g - 50))
    d.line([px(*p) for p in L.RIVER], fill=(70, 150, 230), width=int(L.RIVER_WIDTH * SCALE))
    for road in L.ROADS + L.TOWN_STREETS:
        d.line([px(*p) for p in road], fill=(255, 255, 255), width=int(7.6 * SCALE))
        d.line([px(*p) for p in road], fill=(90, 92, 100), width=int(6.2 * SCALE))
    d.line([px(*p) for p in L.TRACKS], fill=(120, 80, 50), width=int(3.4 * SCALE))
    d.line([px(*p) for p in L.TRACKS], fill=(200, 200, 200), width=4)
    f_small, f_mid, f_big = font(18), font(22, True), font(30, True)
    labels = []
    for b in data["blocks"]:
        col = tuple(int(c * 255) for c in cats[b["cat"]]["colour"])
        x, y, w, h, rot = b["x"], b["y"], b["w"], b["d"], math.radians(b["rot"])
        if b["shape"] == "hex":
            r = w / 2 * 1.15
            pts = [px(x + math.cos(math.radians(30 + 60 * k)) * r, y + math.sin(math.radians(30 + 60 * k)) * r) for k in range(6)]
            d.polygon(pts, fill=col, outline=(80, 90, 60))
            continue
        if b["shape"] == "cyl":
            r = w / 2
            d.ellipse([px(x - r, y + r), px(x + r, y - r)], fill=col, outline=(40, 40, 40))
        else:
            corners = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
            pts = [px(x + cx * math.cos(rot) - cy * math.sin(rot), y + cx * math.sin(rot) + cy * math.cos(rot)) for cx, cy in corners]
            d.polygon(pts, fill=col, outline=(40, 40, 40) if not b["id"].startswith("plot.") else (90, 100, 70))
        if b.get("enter"):
            X, Y = px(x, y)
            d.ellipse([X - 11, Y - 11, X + 11, Y + 11], fill=(255, 255, 255), outline=(20, 20, 20), width=3)
            d.rectangle([X - 4, Y - 6, X + 4, Y + 7], fill=(150, 90, 40))
        if b["label"] and b["label"] not in ("House", "Boat", "Pier", "Forest", "Beach") and not b["id"].startswith("gate."):
            labels.append((b, f_small))
    for b, f in labels:
        x, y = px(b["x"], b["y"])
        d.text((x, y + b["d"] * SCALE / 2 + 4), b["label"], fill=(20, 20, 25), font=f, anchor="ma", stroke_width=3, stroke_fill=(255, 255, 255))
    # section names with plot counts
    for z in L.ZONES:
        x, y = px(*z["at"])
        rr = (z["radius"] or 18) * SCALE
        if z["radius"]:
            d.ellipse([x - rr, y - rr, x + rr, y + rr], outline=(255, 255, 255), width=4)
        title = f"{z['name']}"
        sub = f"{z['plots']} plots · {z['open']} open" + ("" if z["kind"] in ("homestead", "crops", "animals", "machines", "trees", "shops") else " · later")
        d.text((x, y - rr - 52), title, fill=(15, 15, 20), font=f_big, anchor="ma", stroke_width=5, stroke_fill=(255, 255, 255))
        d.text((x, y - rr - 18), sub, fill=(60, 60, 70), font=f_mid, anchor="ma", stroke_width=4, stroke_fill=(255, 255, 255))
    for name, (x, y) in (("TOWN", L.TOWN["centre"]), ("SEAPORT", (L.PORT["centre"][0] + 14, L.PORT["centre"][1] - 40))):
        X, Y = px(x, y)
        d.text((X, Y - 250 if name == "TOWN" else Y), name, fill=(15, 15, 20), font=font(44, True), anchor="mm", stroke_width=6, stroke_fill=(255, 255, 255))
    # legend
    lx, ly = 40, 40
    d.rectangle([lx - 20, ly - 20, lx + 430, ly + 34 * len(cats) + 104], fill=(255, 255, 255), outline=(40, 40, 40), width=2)
    d.text((lx, ly), "Farmverse world - blockout", fill=(0, 0, 0), font=font(28, True))
    for k, (cid, c) in enumerate(cats.items()):
        y = ly + 50 + k * 34
        d.rectangle([lx, y, lx + 26, y + 26], fill=tuple(int(v * 255) for v in c["colour"]), outline=(40, 40, 40))
        d.text((lx + 40, y + 2), c["name"], fill=(0, 0, 0), font=font(22))
    y = ly + 50 + len(cats) * 34
    d.ellipse([lx + 2, y + 2, lx + 24, y + 24], fill=(255, 255, 255), outline=(20, 20, 20), width=3)
    d.text((lx + 40, y + 2), "Building you can enter", fill=(0, 0, 0), font=font(22))
    d.text((S - 60, S - 60), "N ↑   1 square = 1 plot (crop plot 7 m)", fill=(0, 0, 0), font=font(24, True), anchor="rd")
    img.save(out)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
