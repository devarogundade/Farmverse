"""Bring the game's UI kit to the websites: sprites, fonts and the four colour themes.

    python tools/web/sync_game_ui.py

Reads  Assets/Farmverse/Content/UI/Game/Resources/GameUi (the same art the Unity UI uses)
Writes web/shared/public/ui/*.webp              sprites (WebP, alpha kept)
       web/shared/public/ui/themes/<id>/*.webp  the theme's tinted copies of the blue chrome
       web/shared/public/fonts/*.ttf            Lilita One (display) and Nunito ExtraBold (body), both OFL
       web/shared/src/tokens.css                colour + sprite tokens per theme (generated, do not edit)

Theme colours use the rule in GameTheme.Tint: blues (hue 180-240°, saturation > 20 %) are moved to
the theme's hue, saturation scaled and lightness shifted; gold, green and red stay the same.
"""

import colorsys
import os
import shutil

from PIL import Image

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(REPO, "Assets", "Farmverse", "Content", "UI", "Game", "Resources", "GameUi")
OUT = os.path.join(REPO, "web", "shared", "public")
TOKENS = os.path.join(REPO, "web", "shared", "src", "tokens.css")

SPRITES = """
Home_Bg Home_LoadingBg Home_Logo Home_Npc
Button01_195_Green Button01_195_Yellow Button01_195_Purple Button01_195_Gray Button01_195_BlueGray Button02_Yellow
ItemFrame01_Single_Yellow ItemFrame01_Single_Green ItemFrame01_Single_Blue ItemFrame01_Single_Purple ItemFrame01_Single_Brown
CardFrame06_Bg_Yellow CardFrame06_Bg_Purple CardFrame05_Bg_Green CardFrame06_Glow
ListFrame03_Single_Inner_Green Label_Ribbon_Single_Orange Label_Trapezoid_Single_Green Label_Trapezoid_Single_Orange
Label_Trapezoid_Single_Purple ResourceBar_Bg ResourceBar_Icon_Gold ResourceBar_Icon_Gem
Icon_PictoIcon_Search Icon_PictoIcon_Bell Icon_PictoIcon_Setting Icon_PictoIcon_Home Icon_PictoIcon_Cloth
Icon_PictoIcon_User Icon_PictoIcon_Close Icon_PictoIcon_Next01 Icon_PictoIcon_Back Icon_PictoIcon_Add01
Icon_Shop Icon_MenuIcon02_Inventory Icon_Crown Icon_Star Icon_Heart Icon_Compass Icon_Map Icon_Golds Icon_Gems
Icon_Hammer Icon_Friends Icon_Quest Icon_Trophy Icon_Gift Icon_Chest Icon_Clover Icon_Shovel Icon_Megaphone
Isl_p_worker Isl_p_farmer Isl_p_builder Isl_p_baker
Isl_b_barn Isl_b_greenhouse Isl_b_farmhouse Isl_b_windmill Isl_b_chicken_coop Isl_b_grain_silos Isl_b_field
Isl_g_wheat Isl_g_milk Isl_g_egg Isl_g_flour Isl_g_tomato
Paint_crop_wheat Paint_animal_cow Paint_animal_chicken Paint_animal_sheep Paint_good_flour Paint_good_bread
Paint_prod_egg Paint_prod_milk Paint_mach_mill Paint_plot_field Paint_tool_watering_can Paint_tool_shovel
Paint_seed_wheat Paint_fac_farmhouse_l1 Paint_fac_chicken_coop_l1 Paint_land_hex
""".split()

# The blue chrome each theme replaces (Resources/GameUi/Themes/<id>/).
THEMED = [
    "Button01_195_Blue", "Button02_Blue", "Button_Square03_Navy", "CardFrame06_Bg_Blue", "ItemFrame03_Single_Navy",
    "ListFrame03_Single_Bg_Blue", "ListFrame03_Single_Bg_Sky", "ListFrame03_Single_Inner_Blue",
    "PanelFrame02_Round_Single_Navy", "Popup01_Single_Navy", "Popup03_Single", "Popup_FullWidth03_Single_Navy",
]
THEMES = {  # GameTheme.Params: hue, saturation multiplier, lightness shift
    "orchard": (170, 0.85, -0.03),
    "meadow": (122, 0.78, -0.02),
    "harvest": (27, 0.72, -0.04),
    "mix": (27, 0.72, -0.04),
}
DEFAULT = "orchard"

# The design's colours (the most used ones in GameUi/*.cs). Blues get tinted per theme.
BLUES = {
    "deep": "#041634", "night": "#061a40", "bg": "#0b1d3a", "panel": "#08284f", "panel-2": "#0b2a52",
    "raised": "#0e3366", "line": "#123f7c", "accent-dark": "#0d5aa4", "accent": "#1676d1", "accent-2": "#27a9ff",
    "sky": "#5fc0ff", "sky-2": "#9fd0ff", "sky-3": "#bfe6ff", "text": "#dff1ff",
}
FIXED = {
    "gold": "#ffd23a", "gold-2": "#ffe36a", "gold-deep": "#f2a600", "orange": "#ffb15c", "green": "#7dff6a",
    "red": "#ff3a52", "pink": "#ff7a8c", "ink": "#170d06", "brown": "#2b160a",
}


def tint(hex_, theme):
    r, g, b = (int(hex_[i:i + 2], 16) / 255 for i in (1, 3, 5))
    h, l, s = colorsys.rgb_to_hls(r, g, b)
    if not (180 < h * 360 < 240 and s > 0.2):
        return hex_
    th, ts, tl = THEMES[theme]
    r, g, b = colorsys.hls_to_rgb(th / 360, min(1, max(0, l + tl)), min(1, max(0, s * ts)))
    return "#%02x%02x%02x" % tuple(round(c * 255) for c in (r, g, b))


def to_webp(src, dst, max_side=None):
    im = Image.open(src)
    if max_side and max(im.size) > max_side:
        im.thumbnail((max_side, max_side), Image.LANCZOS)
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    im.save(dst, "WEBP", quality=88, method=6)


def main():
    ui = os.path.join(OUT, "ui")
    for name in SPRITES:
        big = name in ("Home_Bg", "Home_LoadingBg", "Home_Logo")
        to_webp(os.path.join(SRC, "Art", name + ".png"), os.path.join(ui, name + ".webp"), 2048 if big else 512)
    for theme in THEMES:
        for name in THEMED:
            to_webp(os.path.join(SRC, "Themes", theme, name + ".png"), os.path.join(ui, "themes", theme, name + ".webp"))

    fonts = os.path.join(OUT, "fonts")
    os.makedirs(fonts, exist_ok=True)
    for f in ("LilitaOne.ttf", "NunitoExtraBold.ttf"):
        shutil.copy2(os.path.join(SRC, f), os.path.join(fonts, f))

    def block(theme):
        lines = [f"  --{k}: {tint(v, theme)};" for k, v in BLUES.items()]
        lines += [f"  --{k}: {v};" for k, v in FIXED.items()]
        lines += [f'  --ui-{n}: url("/ui/themes/{theme}/{n}.webp");' for n in THEMED]
        return "\n".join(lines)

    css = ["/* Generated by tools/web/sync_game_ui.py from the game's GameTheme — do not edit. */"]
    css.append(f":root,\n[data-theme='{DEFAULT}'] {{\n{block(DEFAULT)}\n}}")
    for theme in THEMES:
        if theme != DEFAULT:
            css.append(f"[data-theme='{theme}'] {{\n{block(theme)}\n}}")
    with open(TOKENS, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n\n".join(css) + "\n")
    print(f"{len(SPRITES)} sprites, {len(THEMED)}x{len(THEMES)} themed, tokens -> {os.path.relpath(TOKENS, REPO)}")


if __name__ == "__main__":
    main()
