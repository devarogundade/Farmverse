"""The world as a blockout: every element of the map as a labelled box with its footprint and
height, coloured by category, so the layout can be judged (and later filled with real models,
one block at a time). Each block has a stable id that the final model will replace.

    python world_blocks.py <out folder>      -> WorldBlockout.blend, world_blocks.json, map renders
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import world_layout as L  # noqa: E402

CATEGORIES = {   # colour, legend name
    "home": ((0.98, 0.80, 0.30), "Homestead"),
    "farm": ((0.95, 0.70, 0.20), "Farm fields"),
    "animals": ((0.85, 0.45, 0.25), "Animal ranch"),
    "factory": ((0.55, 0.55, 0.62), "Factories"),
    "orchard": ((0.55, 0.80, 0.30), "Orchard"),
    "shops": ((0.95, 0.40, 0.55), "Shops and market"),
    "homes": ((0.95, 0.88, 0.70), "Houses"),
    "civic": ((0.40, 0.60, 0.95), "Public buildings"),
    "leisure": ((0.70, 0.45, 0.90), "Leisure"),
    "transport": ((0.30, 0.30, 0.34), "Transport"),
    "port": ((0.20, 0.70, 0.80), "Seaport and fishery"),
    "mine": ((0.50, 0.38, 0.28), "Mine"),
    "zoo": ((0.40, 0.75, 0.55), "Wildlife park"),
    "landmark": ((0.95, 0.30, 0.25), "Landmarks"),
    "nature": ((0.20, 0.50, 0.25), "Forest and nature"),
    "plot_open": ((0.55, 0.85, 0.35), "Plot (open)"),
    "plot_locked": ((0.80, 0.80, 0.72), "Plot (locked)"),
}

ZONE_CAT = {"homestead": "home", "crops": "farm", "animals": "animals", "machines": "factory", "trees": "orchard",
            "shops": "shops", "fishing": "port", "mining": "mine", "zoo": "zoo", "greenhouse": "farm"}


def B(bid, label, cat, x, y, w, d, h, rot=0.0, shape="box"):
    return {"id": bid, "label": label, "cat": cat, "x": round(x, 2), "y": round(y, 2), "w": w, "d": d, "h": h, "rot": rot, "shape": shape}


def rotp(cx, cy, dx, dy, deg):
    a = math.radians(deg)
    return cx + dx * math.cos(a) - dy * math.sin(a), cy + dx * math.sin(a) + dy * math.cos(a)


# buildings you can walk into (each opens a room diorama, see world_interiors)
ENTERABLE = {"home.farmhouse", "home.warehouse", "ranch.barn", "ranch.coop", "civic.town_hall", "civic.community",
             "port.office", "leisure.cafe", "mine.entrance", "fishery.market", "civic.school", "civic.clinic"}


def blocks():
    out = []
    # ---- sections: their plots, gate and the buildings that belong to them --------------------------------
    for z in L.ZONES:
        cat = ZONE_CAT[z["kind"]]
        for i, (x, y, w, d) in enumerate(z["rects"]):
            out.append(B(f"plot.{z['id']}.{i + 1}", "", "plot_open" if i < z["open"] else "plot_locked", x, y, w, d, 0.25))
        if z["id"] != "market":
            gx, gy = L.gate_of(z, (0.0, 0.0) if z["id"] not in ("farm", "fishery", "zoo") else
                               {"farm": (-100.0, -10.0), "fishery": (-38.0, -104.0), "zoo": (110.0, 88.0)}[z["id"]])
            ang = math.degrees(math.atan2(gy - z["at"][1], gx - z["at"][0])) + 90
            out.append(B(f"gate.{z['id']}", z["name"], cat, gx, gy, 6.0, 0.8, 4.5, rot=ang))
    zx = {z["id"]: z["at"] for z in L.ZONES}

    def around(zid, items, dist, start=0.0):
        cx, cy = zx[zid]
        dist = next(z["extent"] for z in L.ZONES if z["id"] == zid) + 5.0
        for k, (bid, label, w, d, h) in enumerate(items):
            a = math.radians(start + k * 360.0 / len(items))
            out.append(B(f"{zid}.{bid}", label, ZONE_CAT[next(z['kind'] for z in L.ZONES if z['id'] == zid)],
                         cx + math.cos(a) * dist, cy + math.sin(a) * dist, w, d, h, rot=math.degrees(a) + 90))

    hp = L.ZONES[0]["rects"]
    out.append(B("home.farmhouse", "Farmhouse (enter)", "home", hp[0][0], hp[0][1], 7.0, 6.0, 6.0))
    out.append(B("home.warehouse", "Warehouse (enter)", "home", hp[1][0], hp[1][1], 7.0, 6.0, 6.5))
    around("farm", [("barn", "Farm barn", 8, 6, 6), ("silo", "Grain silos", 5, 5, 9), ("shed", "Tractor shed", 6, 5, 4),
                    ("hay", "Hay store", 5, 4, 3), ("pond", "Irrigation pond", 7, 5, 0.3)], 18.0, 40)
    around("ranch", [("barn", "Cattle barn", 10, 7, 6), ("coop", "Chicken coop", 5, 4, 3), ("stable", "Stable", 8, 5, 4),
                     ("sheep", "Sheep shed", 6, 5, 3.5), ("pigsty", "Pigsty", 5, 4, 2.5), ("vet", "Animal shelter / vet", 7, 5, 4),
                     ("trough", "Water troughs", 6, 2, 1)], 20.0, 20)
    around("factories", [("bakery", "Bakery factory", 12, 8, 7), ("dairy", "Dairy plant", 12, 8, 7), ("sugar", "Sugar mill", 10, 8, 8),
                         ("textile", "Textile mill", 12, 8, 7), ("feed", "Feed mill", 9, 7, 9), ("lumber", "Lumber yard", 12, 9, 4),
                         ("depot", "Truck depot", 12, 8, 3)], 21.0, 10)
    around("orchard", [("packing", "Packing shed", 7, 5, 4), ("bees", "Bee hives", 4, 3, 1.5), ("press", "Cider press", 5, 4, 4)], 14.0, 200)
    around("greenhouses", [("glass1", "Glasshouse", 10, 5, 4), ("glass2", "Glasshouse", 10, 5, 4), ("potting", "Potting shed", 5, 4, 3)], 12.0, 120)
    around("fishery", [("market", "Fish market", 8, 6, 4), ("smokehouse", "Smokehouse", 5, 4, 4), ("nets", "Net drying racks", 6, 3, 2)], 10.0, 30)
    around("mine", [("entrance", "Mine entrance", 8, 5, 7), ("ore", "Ore shed", 7, 5, 5), ("rails", "Cart rails", 14, 2, 0.5), ("crusher", "Stone crusher", 6, 6, 8)], 13.0, 250)
    around("zoo", [("entry", "Zoo entrance", 8, 4, 5), ("big", "Big animal enclosure", 12, 10, 2), ("birds", "Aviary", 8, 8, 7),
                   ("pond", "Penguin pond", 8, 6, 1), ("cafe", "Zoo cafe", 5, 4, 3.5)], 16.0, 210)

    # ---- the town: market square with shops all round, public buildings, houses ------------------------
    tx, ty = L.TOWN["centre"]
    out.append(B("town.square", "Market square", "shops", tx, ty + 2.0, 26.0, 18.0, 0.15))
    for k, (bid, label) in enumerate((("bakery", "Bakery"), ("grocer", "Grocer"), ("butcher", "Butcher"), ("fish", "Fishmonger"),
                                      ("tailor", "Tailor"), ("florist", "Florist"), ("toys", "Toy shop"), ("hardware", "Hardware"))):
        side = k // 4
        x = tx - 12.0 + (k % 4) * 8.0
        out.append(B(f"shop.{bid}", label, "shops", x, ty - 14.0 if side == 0 else ty + 30.0, 6.5, 5.5, 6.5, rot=0 if side == 0 else 180))
    for bid, label, x, y, w, d, h, rot in (
            ("town_hall", "Town hall", tx, ty + 18.0, 14, 8, 12, 0), ("school", "School", tx + 30.0, ty + 36.0, 12, 7, 6, 180),
            ("clinic", "Clinic", tx + 34.0, ty + 12.0, 9, 6, 6, 90), ("fire", "Fire station", tx - 30.0, ty - 26.0, 10, 7, 7, 0),
            ("police", "Police station", tx - 30.0, ty - 10.0, 9, 6, 6, 0), ("library", "Library", tx - 30.0, ty + 8.0, 9, 6, 6, 0),
            ("community", "Community hall (teams)", tx - 14.0, ty + 36.0, 10, 7, 6, 180), ("post", "Post office", tx + 14.0, ty + 36.0, 8, 6, 5, 180),
            ("bank", "Bank", tx + 34.0, ty - 4.0, 8, 6, 7, 90), ("chapel", "Chapel", tx + 42.0, ty + 44.0, 6, 11, 12, 200)):
        out.append(B(f"civic.{bid}", label, "civic", x, y, w, d, h, rot=rot))
    for bid, label, x, y, w, d, h in (("cafe", "Cafe", tx - 18.0, ty + 12.0, 6, 5, 4), ("hotel", "Hotel", tx + 30.0, ty - 22.0, 12, 8, 10),
                                      ("cinema", "Cinema", tx + 14.0, ty - 28.0, 12, 8, 7), ("park", "Town park", tx + 20.0, ty + 2.0, 16, 12, 0.4)):
        out.append(B(f"leisure.{bid}", label, "leisure", x, y, w, d, h))
    k = 0
    for x in range(int(tx - 20), int(tx + 40), 10):
        out.append(B(f"house.s{k}", "House", "homes", x, ty - 40.0, 6, 5, 5)); k += 1
    for y in range(int(ty - 24), int(ty + 32), 11):
        out.append(B(f"house.e{k}", "House", "homes", tx + 48.0, y, 6, 5, 5, rot=90)); k += 1
        out.append(B(f"house.w{k}", "House", "homes", tx - 44.0, y + 4.0, 6, 5, 5, rot=90)); k += 1

    # ---- seaport, transport, landmarks, leisure out of town ---------------------------------------------
    px, py = L.PORT["centre"]
    for bid, label, x, y, w, d, h, rot in (
            ("pier1", "Pier", px, py - 10.0, 4, 30, 1, 0), ("pier2", "Pier", px + 22.0, py - 8.0, 4, 30, 1, 8),
            ("warehouse1", "Port warehouse", px - 22.0, py + 10.0, 16, 10, 8, 0), ("warehouse2", "Port warehouse", px + 30.0, py + 12.0, 16, 10, 8, 0),
            ("crane", "Harbour crane", px + 12.0, py, 5, 5, 15, 0), ("office", "Harbour office (orders)", px - 2.0, py + 16.0, 8, 6, 6, 180),
            ("ferry", "Ferry dock", px + 44.0, py - 6.0, 10, 14, 1, 0)):
        out.append(B(f"port.{bid}", label, "port", x, y, w, d, h, rot=rot))
    for i, (bx, by) in enumerate(((px - 5, py - 30), (px + 6, py - 38), (px + 17, py - 28), (px - 40, py - 55), (px + 60, py - 70))):
        out.append(B(f"port.boat{i + 1}", "Boat", "port", bx, by, 3, 8, 2.5, rot=10 * i))
    sx, sy = L.STATION["centre"]
    out.append(B("transport.station", "Railway station", "transport", sx, sy - 6.5, 22, 9, 6, rot=0))
    out.append(B("transport.train", "Train", "transport", sx - 22.0, sy + 3.0, 22, 3, 4, rot=-6))
    out.append(B("transport.helipad", "Helipad", "transport", *L.HELIPAD["centre"], 14, 14, 0.4, shape="cyl"))
    out.append(B("transport.bridge", "Stone bridge", "transport", -89.0, -11.0, 18, 7, 1.5, rot=-6))
    out.append(B("transport.bus", "Bus stop", "transport", 70.0, 4.0, 5, 2, 3))
    out.append(B("landmark.lighthouse", "Lighthouse", "landmark", *L.LIGHTHOUSE["centre"], 5, 5, 18, shape="cyl"))
    out.append(B("landmark.windmill", "Windmill", "landmark", *L.WINDMILL["centre"], 6, 6, 12, shape="cyl"))
    out.append(B("landmark.watertower", "Water tower", "landmark", 22.0, 30.0, 5, 5, 13, shape="cyl"))
    fx, fy = L.FAIRGROUND["centre"]
    out.append(B("leisure.fair", "Fairground (ferris wheel)", "leisure", fx, fy, 24, 18, 14))
    for fxx, fyy, fr, what, turn in L.FLAT_SPOTS:
        label = {"sports_field": "Sports field", "farmland_wheat": "Wheat field", "farmland_sunflower": "Sunflower field",
                 "farmland_cabbage": "Cabbage field"}.get(what, what)
        cat = "leisure" if what == "sports_field" else "farm"
        out.append(B("country." + what, label, cat, fxx, fyy, fr * 1.4, fr * 0.9, 0.5, rot=turn))
    for i, deg in enumerate((-60.0, -125.0, 15.0, 160.0)):
        a = math.radians(deg)
        rr = L.coast_radius(a) - 22.0
        out.append(B(f"leisure.beach{i + 1}", "Beach", "leisure", math.cos(a) * rr, math.sin(a) * rr, 16, 8, 2.5, rot=deg - 90))
    out.append(B("leisure.camp", "Campsite", "leisure", -105.0, 95.0, 14, 10, 3))
    # forests and hills as single blocks
    for i, (hx, hy, hh, hr) in enumerate(L.HILLS):
        out.append(B(f"nature.forest{i + 1}", "Forest", "nature", hx, hy + hr * 0.2, hr * 0.7, hr * 0.5, 8, shape="cyl"))
    return out


# ---- Blender ---------------------------------------------------------------------------------------------

def build(out_dir):
    import bpy
    import bmesh
    from mathutils import Matrix
    import world_terrain as T
    import fvtile

    bpy.ops.wm.read_factory_settings(use_empty=True)
    tex = os.path.join(out_dir, "Textures")
    os.makedirs(tex, exist_ok=True)
    fvtile.write_all(tex, 512)
    scene = bpy.context.scene
    root = bpy.data.collections.new("Blockout")
    scene.collection.children.link(root)
    ground = bpy.data.collections.new("Ground")
    root.children.link(ground)
    T.terrain(ground, tex)
    T.sea(ground, tex)
    T.surf(ground)
    T.river(ground, tex)
    T.roads(ground, tex)
    T.tracks(ground, tex)
    mats = {}
    for cat, (rgb, _) in CATEGORIES.items():
        m = bpy.data.materials.new("Block " + cat)
        m.use_nodes = True
        m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*rgb, 1)
        m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.7
        m.diffuse_color = (*rgb, 1)
        mats[cat] = m
    label_mat = bpy.data.materials.new("Block Label")
    label_mat.use_nodes = True
    label_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.05, 0.05, 0.06, 1)
    groups = {}
    data = blocks()
    for b in data:
        cat = b["cat"]
        if cat not in groups:
            groups[cat] = bpy.data.collections.new(CATEGORIES[cat][1])
            root.children.link(groups[cat])
        bm = bmesh.new()
        if b["shape"] == "hex":
            res = bmesh.ops.create_cone(bm, cap_ends=True, segments=6, radius1=b["w"] / 2, radius2=b["w"] / 2, depth=b["h"])
        elif b["shape"] == "cyl":
            res = bmesh.ops.create_cone(bm, cap_ends=True, segments=20, radius1=b["w"] / 2, radius2=b["w"] / 2, depth=b["h"])
        else:
            res = bmesh.ops.create_cube(bm, size=1.0)
            bmesh.ops.scale(bm, vec=(b["w"], b["d"], b["h"]), verts=res["verts"])
        bmesh.ops.translate(bm, vec=(0, 0, b["h"] / 2), verts=bm.verts)
        me = bpy.data.meshes.new(b["id"])
        bm.to_mesh(me)
        bm.free()
        me.materials.append(mats[cat])
        o = bpy.data.objects.new(b["id"], me)
        z = T.ground_z(b["x"], b["y"]) if not b["id"].startswith("plot.") else 0.0
        if b["id"].startswith("port.boat"):
            z = L.SEA_LEVEL - 0.5
        if b["id"].startswith("port.pier") or b["id"].startswith("port.ferry"):
            z = L.SEA_LEVEL + 0.5
        o.location = (b["x"], b["y"], z)
        o.rotation_euler = (0, 0, math.radians(b["rot"]))
        o["label"] = b["label"]
        o["category"] = cat
        groups[cat].objects.link(o)
        b["z"] = round(z, 2)
        if b["label"] and b["label"] not in ("House", "Boat", "Pier", "Port warehouse", "Beach", "Forest"):
            cu = bpy.data.curves.new(b["id"] + ".label", "FONT")
            cu.body = b["label"]
            cu.size = 2.4 if not b["id"].startswith("gate.") else 3.2
            cu.align_x = "CENTER"
            cu.extrude = 0.05
            cu.materials.append(label_mat)
            t = bpy.data.objects.new(b["id"] + ".label", cu)
            t.location = (b["x"], b["y"], z + b["h"] + 1.0)
            groups[cat].objects.link(t)
    # light, sky and cameras
    w = bpy.data.worlds.new("Sky")
    scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.62, 0.78, 0.95, 1)
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.4
    so = bpy.data.objects.new("Sun", sun)
    so.rotation_euler = (math.radians(40), math.radians(10), math.radians(-35))
    root.objects.link(so)
    cams = {}
    for name, loc, rot, ortho in (("Map", (10, 0, 400), (0, 0, 0), 430), ("Perspective", (20, -330, 280), (48, 0, 0), None)):
        cam = bpy.data.cameras.new("Cam " + name)
        cam.clip_end = 3000
        if ortho:
            cam.type = "ORTHO"
            cam.ortho_scale = ortho
        else:
            cam.lens = 28
        co = bpy.data.objects.new("Cam " + name, cam)
        co.location = loc
        co.rotation_euler = tuple(math.radians(a) for a in rot)
        root.objects.link(co)
        cams[name] = co
    scene.camera = cams["Map"]
    scene.render.engine = "BLENDER_EEVEE"
    scene.view_settings.view_transform = "Standard"
    scene.eevee.taa_render_samples = 8
    import world_interiors
    world_interiors.build()
    for b in data:
        b["enter"] = b["id"] in ENTERABLE or b["id"].split(".")[0] in ("shop",) or b["id"].startswith("factories.") and b["id"] != "factories.depot"
    with open(os.path.join(out_dir, "world_blocks.json"), "w") as f:
        json.dump({"categories": {k: {"colour": v[0], "name": v[1]} for k, v in CATEGORIES.items()}, "blocks": data}, f, indent=1)
    L.write(os.path.join(out_dir, "world_layout.json"))
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "WorldBlockout.blend"), relative_remap=True)
    return cams


def render(out_dir, names=("Map", "Perspective"), size=(1600, 1600)):
    import bpy
    scene = bpy.context.scene
    for n in names:
        scene.camera = bpy.data.objects["Cam " + n]
        scene.render.resolution_x, scene.render.resolution_y = size if n == "Map" else (1600, 1000)
        scene.render.filepath = os.path.join(out_dir, "blockout_%s.png" % n.lower())
        bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    out = os.path.abspath([a for a in sys.argv[1:] if not a.startswith("--")][-1])
    os.makedirs(out, exist_ok=True)
    build(out)
    if "--render" in sys.argv:
        render(out)
    print("blocks done")
