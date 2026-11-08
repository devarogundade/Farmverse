"""The NFT images wallets show: web/shared/public/nft/items/<id>.png, served by the market site at
https://marketplace.farmverse.world/nft/items/<id>.png (the items contract's base_uri + each token's `media`).

    python tools/nft_images.py            # every image
    python tools/nft_images.py --only wood,barn

One 512 × 512 card per item: the item, rendered from the market's 3D model (tools/blender/render_icons.py), or
its game art for packs, passes, deeds and boosts, on its kind's colour as on the market's cards. Golden crops
get the gold card. The Farmhand picture is one Genesis worker in its outfit (tools/blender/render_farmhand.py).
The art is the paid packs' and kit's, so the output folder is git-ignored. Needs Blender and Pillow.
"""

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
BLENDER = os.environ.get("BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
MODELS = os.path.join(ROOT, "web", "shared", "public", "models", "items")
UI = os.path.join(ROOT, "web", "shared", "public", "ui")
GAME_ART = os.path.join(ROOT, "Assets", "Farmverse", "Content", "UI", "Game", "Resources", "GameUi", "Art")
OUT = os.path.join(ROOT, "web", "shared", "public", "nft", "items")
SIZE = 512

# card colours by kind (top, bottom), after the market's item frames
CARD = {
    "good": ("#a64be8", "#5b1aa0"),
    "building": ("#e0954a", "#8c4a17"),
    "tractor": ("#3b9cf0", "#1556a8"),
    "land": ("#55c95f", "#1f7a3a"),
    "animal": ("#55c95f", "#1f7a3a"),
    "worker": ("#ffcf3a", "#d9800f"),
    "pack": ("#3b9cf0", "#1556a8"),
    "boost": ("#a64be8", "#5b1aa0"),
    "golden": ("#ffd54a", "#a3520a"),   # deeper at the bottom, so a pale crop still stands out
}
# flat art: id → (picture, kind). Packs and the deed as the market draws them; boosts as the game does.
FLAT = {
    "farm_pass": (os.path.join(UI, "Icon_Crown.webp"), "pack"),
    "builder_pass": (os.path.join(UI, "Icon_Hammer.webp"), "pack"),
    "starter_pack": (os.path.join(UI, "Icon_Chest.webp"), "pack"),
    "gems_100": (os.path.join(UI, "Icon_Gems.webp"), "pack"),
    "gems_500": (os.path.join(UI, "Icon_Gems.webp"), "pack"),
    "gems_2000": (os.path.join(UI, "Icon_Gems.webp"), "pack"),
    "deed": (os.path.join(UI, "Icon_Map.webp"), "land"),
    **{b: (os.path.join(GAME_ART, f"Isl_x_{b}.png"), "boost") for b in ("shield", "fert", "rush", "instant", "basket", "nightshift", "lunch")},
}
CROPS = ("wheat", "beans", "maize", "cabbage", "pumpkin", "sunflower", "tomato")
FARMHAND_OUTFIT = 3  # Genesis Farmhand #4: the shared "a Farmhand" picture


def model_kinds():
    """id → kind for every 3D item, from the market's own list (web/shared/src/nft/metadata.ts)."""
    src = open(os.path.join(ROOT, "web", "shared", "src", "nft", "metadata.ts"), encoding="utf-8").read()
    return dict(re.findall(r"^  (\w+): \{ type: '(\w+)'", src, re.M))


def hex_rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (1, 3, 5))


def card(kind):
    """A rounded card in the kind's colours: a vertical gradient and a soft light behind the item."""
    top, bottom = (hex_rgb(c) for c in CARD[kind])
    grad = Image.new("RGBA", (SIZE, SIZE))
    px = grad.load()
    for y in range(SIZE):
        t = y / (SIZE - 1)
        c = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        for x in range(SIZE):
            px[x, y] = (*c, 255)
    glow = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(glow).ellipse((SIZE * .14, SIZE * .12, SIZE * .86, SIZE * .84), fill=120)
    glow = glow.filter(ImageFilter.GaussianBlur(SIZE * .09))
    grad = Image.composite(Image.new("RGBA", (SIZE, SIZE), (255, 255, 255, 255)), grad, glow.point(lambda v: v * .45))
    mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), radius=int(SIZE * .09), fill=255)
    out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    out.paste(grad, (0, 0), mask)
    return out


def place(base, picture, fill=.78):
    """The picture centred on the card, as large as `fill` of it allows, keeping its shape."""
    pic = picture.convert("RGBA")
    box = pic.getbbox()
    if box:
        pic = pic.crop(box)
    scale = min(SIZE * fill / pic.width, SIZE * fill / pic.height)
    pic = pic.resize((max(1, round(pic.width * scale)), max(1, round(pic.height * scale))), Image.LANCZOS)
    base.alpha_composite(pic, ((SIZE - pic.width) // 2, (SIZE - pic.height) // 2 + round(SIZE * .02)))
    return base


def blender(script, *args):
    subprocess.run([BLENDER, "-b", "--factory-startup", "-P", os.path.join(ROOT, "tools", "blender", script), "--", *args],
                   check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def main():
    only = set(sys.argv[sys.argv.index("--only") + 1].split(",")) if "--only" in sys.argv else None
    want = lambda i: only is None or i in only
    os.makedirs(OUT, exist_ok=True)
    kinds = model_kinds()
    raw = tempfile.mkdtemp(prefix="fv-nft-")
    made = 0
    try:
        ids = [i for i in kinds if want(i) or (i in CROPS and want("golden_" + i))]
        if ids:
            blender("render_icons.py", MODELS, raw, "--only", ",".join(ids), "--size", "768")
        for i in ids:
            src = os.path.join(raw, i + ".png")
            if not os.path.exists(src):
                print("no render for", i)
                continue
            if want(i):
                place(card(kinds[i]), Image.open(src)).save(os.path.join(OUT, i + ".png"), optimize=True)
                made += 1
            if i in CROPS and want("golden_" + i):
                place(card("golden"), Image.open(src)).save(os.path.join(OUT, f"golden_{i}.png"), optimize=True)
                made += 1
        for i, (art, kind) in FLAT.items():
            if want(i) and os.path.exists(art):
                place(card(kind), Image.open(art), .62).save(os.path.join(OUT, i + ".png"), optimize=True)
                made += 1
        if want("farmhand"):
            outfit = json.load(open(os.path.join(ROOT, "chain", "deploy", "farmhands-genesis.json"), encoding="utf-8"))[FARMHAND_OUTFIT]["props"]["outfit"]
            fh = os.path.join(raw, "farmhand.png")
            blender("render_farmhand.py", fh, json.dumps(outfit), "--size", "768")
            place(card("worker"), Image.open(fh), .86).save(os.path.join(OUT, "farmhand.png"), optimize=True)
            made += 1
    finally:
        shutil.rmtree(raw, ignore_errors=True)
    print(f"{made} NFT images in {OUT}")


main()
