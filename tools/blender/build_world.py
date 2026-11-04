"""Assemble the whole world in Blender from world_layout: the island, sea, river, roads and
railway; the farm valley with its zones of hex plots (open ones in use, locked ones waiting);
the town, seaport, station, helipad and countryside; then cameras and renders.

    python build_world.py <out folder> [--render] [--blend]

Models are built once into a hidden "Library" collection and placed as collection
instances, so the .blend stays light and every placement is listed in World/Placements."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import build_all  # noqa: E402
import fvlib  # noqa: E402
import fvtile  # noqa: E402
import world_buildings as WB  # noqa: E402
import world_layout as L  # noqa: E402
import world_terrain as T  # noqa: E402
from fvlib import rng  # noqa: E402

SHOW_STAGES = {"Base", "Idle", "Body", "Grown", "Ready", "Product"}


class World:
    def __init__(self, out):
        self.out = out
        self.textures = os.path.join(out, "Textures")
        os.makedirs(self.textures, exist_ok=True)
        scene = bpy.context.scene
        self.root = bpy.data.collections.new("World")
        scene.collection.children.link(self.root)
        self.lib = bpy.data.collections.new("Library")
        scene.collection.children.link(self.lib)
        bpy.context.view_layer.layer_collection.children["Library"].exclude = True
        self.ground = self._sub("Ground")
        self.groups = {}
        self.models = {}
        self.placements = []

    def _sub(self, name, parent=None):
        c = bpy.data.collections.new(name)
        (parent or self.root).children.link(c)
        return c

    def group(self, name):
        if name not in self.groups:
            self.groups[name] = self._sub(name)
        return self.groups[name]

    # ---- library and placement -----------------------------------------------------------------

    def model(self, key):
        """Build a model once. key is 'id' or 'id@Stage' to keep one growth stage (plus Base)."""
        if key in self.models:
            return self.models[key]
        mid, _, stage = key.partition("@")
        builder = WB.ASSETS.get(mid) or build_all.ASSETS.get(mid)
        col = bpy.data.collections.new("lib " + key)
        self.lib.children.link(col)
        builder(col)
        keep = {"Base", stage} if stage else SHOW_STAGES
        for o in list(col.objects):
            base = o.name.split(".")[0]
            if o.type == "MESH" and base not in keep and not base.startswith("Spin"):
                bpy.data.objects.remove(o)
        self.models[key] = col
        return col

    def place(self, key, x, y, rot=0.0, scale=1.0, group="Props", z=None):
        col = self.model(key)
        e = bpy.data.objects.new(key, None)
        e.instance_type = "COLLECTION"
        e.instance_collection = col
        e.location = (x, y, T.ground_z(x, y) if z is None else z)
        e.rotation_euler = (0, 0, math.radians(rot))
        e.scale = (scale, scale, scale)
        self.group(group).objects.link(e)
        self.placements.append({"model": key, "x": round(x, 2), "y": round(y, 2), "rot": round(rot, 1), "scale": round(scale, 3), "group": group})
        return e

    # ---- the land --------------------------------------------------------------------------------

    def land(self):
        fvtile.write_all(self.textures, 512)
        fvlib.write_atlas(self.textures)
        T.terrain(self.ground, self.textures)
        T.sea(self.ground, self.textures)
        T.surf(self.ground)
        T.river(self.ground, self.textures)
        T.roads(self.ground, self.textures)
        self.track = T.tracks(self.ground, self.textures)
        # dirt tracks from the homestead to every zone
        dirt = T.material("World Dirt Path", "dirt", folder=self.textures)
        for z in L.ZONES[1:]:
            cx, cy = L.hex_xy(*z["centre"])
            d = math.hypot(cx, cy)
            T.ribbon("Farm Track " + z["id"], [(cx / d * 7.5, cy / d * 7.5), (cx * 0.62, cy * 0.62)], 3.0, T.ground_z, dirt, self.ground, lift=0.05)
            T.ribbon("Farm Spur " + z["id"], [(cx / d * (d + 12.0), cy / d * (d + 12.0)), (cx / d * L.VALLEY_RING, cy / d * L.VALLEY_RING)], 3.0, T.ground_z, dirt, self.ground, lift=0.05)
        ring = [(math.cos(k / 72 * math.tau) * L.VALLEY_RING, math.sin(k / 72 * math.tau) * L.VALLEY_RING) for k in range(73)]
        T.ribbon("Valley Ring", ring, 3.6, T.ground_z, dirt, self.ground, lift=0.05)

    # ---- the farm valley ---------------------------------------------------------------------------

    def farm(self):
        r = rng(7)
        crops = ["crop_wheat", "crop_maize", "crop_tomato", "crop_onion", "crop_potato", "crop_beans", "crop_sunflower"]
        stages = ["Ready", "Grown", "Young", "Sprout"]
        machines = ["machine_mill", "machine_oven", "machine_dairy", "machine_feed_mill", "machine_kettle", "machine_oil_press", "machine_sawmill"]
        for z in L.ZONES:
            g = "Farm " + z["id"]
            for i, (q, rr) in enumerate(z["tiles"]):
                x, y = L.hex_xy(q, rr)
                z0 = 0.0
                if i >= z["open"]:
                    # locked: meadow pad with a price stake for the next ones, a padlock further on
                    self.place("land_sale@Ready" if i < z["open"] + 3 else "land_sale@Idle", x, y, group=g, z=z0)
                    continue
                kind = z["kind"]
                if kind == "homestead":
                    self.place("land_tile@Base", x, y, group=g, z=z0)
                    thing = {0: "farmhouse", 1: "facility_warehouse"}.get(i)
                    if thing:
                        self.place(thing, x, y, group=g, z=z0)
                    elif i == 2:
                        self.place("decor_flower_bed", x, y, group=g, z=z0)
                    elif i == 3:
                        self.place("decor_fountain", x, y, group=g, z=z0)
                    else:
                        self.place("land_tile@Idle", x, y, group=g, z=z0)
                elif kind == "crops":
                    self.place("land_tile@Base", x, y, group=g, z=z0)
                    self.place("field", x, y, group=g, z=z0)
                    self.place(crops[i % len(crops)] + "@" + stages[i % len(stages)], x, y, group=g, z=z0)
                elif kind == "animals":
                    self.place("land_tile@Base", x, y, group=g, z=z0)
                    self.place("pen", x, y, group=g, z=z0)
                    animal = "livestock_cow@Grown" if i % 2 == 0 else "livestock_chicken@Grown"
                    for k in range(2 if animal.startswith("livestock_cow") else 3):
                        a = k * 2.1 + i
                        self.place(animal, x + math.cos(a) * 0.7, y + math.sin(a) * 0.7, rot=r.uniform(0, 360), group=g, z=z0 + 0.1)
                elif kind == "trees":
                    self.place("land_tile@Base", x, y, group=g, z=z0)
                    self.place(("tree_oak" if i % 2 == 0 else "tree_pine") + "@Ready", x, y, group=g, z=z0)
                elif kind == "machines":
                    self.place("land_tile@Base", x, y, group=g, z=z0)
                    self.place(machines[i % len(machines)], x, y, rot=0, group=g, z=z0)
            # a sign at each zone's gate
            cx, cy = L.hex_xy(*z["centre"])
            d = math.hypot(cx, cy) or 1.0
            if z["id"] != "homestead":
                sx, sy = cx - cx / d * 13.5, cy - cy / d * 13.5
                self.place("sign_default", sx, sy, rot=math.degrees(math.atan2(-cy, -cx)) + 90, group=g)
        # farm buildings on the valley rim
        self.place("silos", 30.0, 30.0, rot=-30, group="Farm Rim")
        self.place("water_tower", 22.0, 38.0, group="Farm Rim")
        self.place("windmill", *L.WINDMILL["centre"], rot=200, group="Farm Rim")
        self.place("greenhouse", -26.0, 30.0, rot=25, group="Farm Rim")
        for k in range(6):
            a = math.radians(-40 + k * 14)
            self.place(["villager_a", "villager_b", "villager_c"][k % 3], math.cos(a) * 24, math.sin(a) * 24, rot=k * 70, group="People")

    # ---- the town --------------------------------------------------------------------------------

    def town(self):
        tx, ty = L.TOWN["centre"]
        g = "Town"
        # the square: town hall north, market stalls, the park east, a cafe and shops
        self.place("town_hall", tx, ty + 14.0, group=g)
        for k, s in enumerate(("market_stall_red", "market_stall_green", "market_stall_blue")):
            self.place(s, tx - 6.0 + k * 6.0, ty + 3.0, group=g)
        self.place("park", tx + 18.0, ty - 0.5, group=g)
        self.place("cafe", tx - 18.0, ty + 12.0, rot=0, group=g)
        shops = ["shop_bakery", "shop_grocer", "shop_fish", "shop_tailor"]
        for k, s in enumerate(shops):
            self.place(s, tx - 24.0 + k * 8.0, ty - 16.0, rot=180, group=g)
        self.place("school", tx + 18.0, ty + 34.0, rot=180, group=g)
        self.place("clinic", tx + 34.0, ty + 14.0, rot=90, group=g)
        self.place("community_hall", tx - 14.0, ty + 34.0, rot=180, group=g)
        houses = ["house_cream", "house_blue", "house_pink", "house_mint", "house_yellow"]
        r = rng(11)
        k = 0
        for x in range(int(tx - 20), int(tx + 40), 10):
            self.place(houses[k % 5], x, ty - 36.0, rot=0, group="Town Houses")
            k += 1
        for y in range(int(ty - 24), int(ty + 30), 11):
            self.place(houses[k % 5], tx + 46.0, y, rot=90, group="Town Houses")
            k += 1
            self.place(houses[k % 5], tx - 36.0, y + 4, rot=-90, group="Town Houses")
            k += 1
        self.place("fire_station", tx - 30.0, ty - 26.0, group=g)
        self.place("hotel", tx + 30.0, ty - 22.0, group=g)
        self.place("chapel", tx + 42.0, ty + 42.0, rot=200, group=g)
        for road in L.TOWN_STREETS + [L.ROADS[0][1:], L.ROADS[2]]:
            for i in range(len(road) - 1):
                a, b = Vector((*road[i], 0)), Vector((*road[i + 1], 0))
                n = int((b - a).length / 12)
                d = (b - a).normalized()
                side = Vector((-d.y, d.x, 0)) * 4.6
                for j in range(n + 1):
                    p = a.lerp(b, j / max(1, n)) + side * (1 if j % 2 else -1)
                    self.place("street_lamp", p.x, p.y, rot=math.degrees(math.atan2(d.y, d.x)), group="Street Lamps")
        for j in range(10):
            x, y = tx + r.uniform(-40, 50), ty + r.uniform(-40, 45)
            self.place("scenery_tree_round", x, y, rot=r.uniform(0, 360), scale=r.uniform(0.8, 1.1), group="Town Trees")

    # ---- the seaport ----------------------------------------------------------------------------

    def port(self):
        px, py = L.PORT["centre"]
        g = "Seaport"
        # find the shore straight south of the port centre and run the pier out from it
        y = py
        while T.height(px, y) > L.SEA_LEVEL + 0.2 and y > py - 60:
            y -= 0.5
        shore = y + 1.0
        self.place("pier", px, shore, group=g, z=L.SEA_LEVEL + 0.3)
        self.place("pier", px + 22.0, shore + 2.0, rot=8, group=g, z=L.SEA_LEVEL + 0.3)
        self.place("boat_fishing", px - 5.0, shore - 14.0, rot=8, group="Boats", z=L.SEA_LEVEL - 0.45)
        self.place("boat_red", px + 5.5, shore - 22.0, rot=-6, group="Boats", z=L.SEA_LEVEL - 0.45)
        self.place("boat_sail", px + 17.0, shore - 12.0, rot=180, group="Boats", z=L.SEA_LEVEL - 0.45)
        self.place("boat_sail", px - 40.0, shore - 40.0, rot=60, group="Boats", z=L.SEA_LEVEL - 0.45)
        self.place("boat_fishing", px + 60.0, shore - 55.0, rot=-30, group="Boats", z=L.SEA_LEVEL - 0.45)
        self.place("port_warehouse", px - 22.0, shore + 16.0, rot=0, group=g)
        self.place("port_warehouse", px + 30.0, shore + 18.0, rot=0, group=g)
        self.place("crane", px + 12.0, shore + 6.0, rot=0, group=g)
        self.place("shop_fish", px - 2.0, shore + 22.0, rot=180, group=g)
        lx, ly = L.LIGHTHOUSE["centre"]
        self.place("lighthouse", lx, ly, group=g)

    # ---- railway, helipad, bridge ---------------------------------------------------------------

    def transport(self):
        sx, sy = L.STATION["centre"]
        self.place("station", sx, sy - 6.5, rot=L.STATION["angle"] - 18 + 0, group="Railway")
        # the train stands on the track west of the station
        tr = self.track
        i = min(range(len(tr)), key=lambda k: (tr[k] - Vector((sx - 18, sy + 2, 0))).length)
        a, b = tr[i], tr[min(i + 1, len(tr) - 1)]
        ang = math.degrees(math.atan2(b.y - a.y, b.x - a.x))
        self.place("train", a.x, a.y, rot=ang, group="Railway", z=T.ground_z(a.x, a.y) + 0.35)
        hx, hy = L.HELIPAD["centre"]
        self.place("helipad", hx, hy, group="Helipad")
        # the bridge where the west road crosses the river
        self.place("bridge", -89.0, -11.0, rot=-6, group="Bridge", z=L.GROUND + 0.15)

    # ---- countryside ------------------------------------------------------------------------------

    def country(self):
        for fx, fy, fr, what, turn in L.FLAT_SPOTS:
            self.place(what, fx, fy, rot=turn, group="Countryside")
        # beach spots where the shore is sandy
        for deg in (-60.0, -125.0, 15.0, 160.0):
            a = math.radians(deg)
            rr = 40.0
            while T.height(math.cos(a) * rr, math.sin(a) * rr) > L.SEA_LEVEL + 0.6 and rr < 260:
                rr += 0.5
            rr -= 9.0
            self.place("beach_spot", math.cos(a) * rr, math.sin(a) * rr, rot=deg - 90, group="Beach")
        # villagers along the roads and in the town
        r = rng(77)
        for k in range(24):
            road = r.choice(L.ROADS + L.TOWN_STREETS)
            i = r.randrange(len(road) - 1)
            a, b = Vector((*road[i], 0)), Vector((*road[i + 1], 0))
            p = a.lerp(b, r.random())
            d = (b - a).normalized()
            p += Vector((-d.y, d.x, 0)) * r.choice((-4.2, 4.2))
            self.place(["villager_a", "villager_b", "villager_c"][k % 3], p.x, p.y, rot=r.uniform(0, 360), group="People")
        for k in range(5):
            a = r.uniform(0, math.tau)
            self.place(r.choice(["boat_sail", "boat_fishing", "boat_red"]), math.cos(a) * 230, math.sin(a) * 230, rot=r.uniform(0, 360), group="Boats", z=L.SEA_LEVEL - 0.45)

    def nature(self):
        r = rng(23)
        placed = 0
        tries = 0
        while placed < 240 and tries < 20000:
            tries += 1
            x, y = r.uniform(-200, 200), r.uniform(-200, 200)
            h = T.height(x, y)
            if h < L.SEA_LEVEL + 1.2:
                continue
            if math.hypot(x, y) < L.VALLEY_RADIUS + 6:
                continue
            tx, ty = L.TOWN["centre"]
            if abs(x - tx) < 58 and abs(y - ty) < 58:
                continue
            px, py = L.PORT["centre"]
            if math.hypot(x - px, y - py) < 40:
                continue
            if any(math.hypot(x - fx, y - fy) < fr + 4 for fx, fy, fr, _, _ in L.FLAT_SPOTS):
                continue
            if any(math.hypot(x - lm["centre"][0], y - lm["centre"][1]) < 16 for lm in (L.STATION, L.HELIPAD, L.WINDMILL, L.LIGHTHOUSE)):
                continue
            if T.poly_dist((x, y), L.RIVER) < 9 or min(T.poly_dist((x, y), rd) for rd in L.ROADS) < 7 or T.poly_dist((x, y), L.TRACKS) < 6:
                continue
            uphill = h > 2.0
            kind = r.choices(["forest_pines", "forest_pines_b", "forest_mixed", "scenery_tree_round", "scenery_bush", "wildflowers", "scenery_rock"],
                             weights=[5 if uphill else 1, 4 if uphill else 1, 1 if uphill else 4, 3, 3, 3, 3 if uphill else 1])[0]
            self.place(kind, x, y, rot=r.uniform(0, 360), scale=r.uniform(0.85, 1.2), group="Nature")
            placed += 1
        # the valley edge: a ring of trees and bushes
        for k in range(40):
            a = k / 40 * math.tau + r.uniform(-0.05, 0.05)
            d = L.VALLEY_RADIUS + r.uniform(1, 8)
            x, y = math.cos(a) * d, math.sin(a) * d
            if min(T.poly_dist((x, y), rd) for rd in L.ROADS) < 6:
                continue
            self.place(r.choice(["scenery_tree_round", "scenery_tree_pine", "scenery_bush", "scenery_tree_round"]), x, y, rot=r.uniform(0, 360), scale=r.uniform(0.8, 1.2), group="Valley Edge")

    # ---- views ------------------------------------------------------------------------------------

    def stage(self):
        scene = bpy.context.scene
        w = bpy.data.worlds.new("World Sky")
        scene.world = w
        w.use_nodes = True
        bg = w.node_tree.nodes["Background"]
        bg.inputs[0].default_value = (0.62, 0.78, 0.95, 1)
        bg.inputs[1].default_value = 1.0
        sun = bpy.data.lights.new("Sun", "SUN")
        sun.energy = 3.6
        sun.angle = 0.12
        so = bpy.data.objects.new("Sun", sun)
        so.rotation_euler = (math.radians(48), math.radians(10), math.radians(-35))
        self.root.objects.link(so)
        scene.render.engine = "BLENDER_EEVEE"
        scene.view_settings.view_transform = "Standard"
        scene.eevee.taa_render_samples = 16
        scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
        self.cams = {}
        for name, loc, rot, lens in (
            ("Overview", (40, -330, 260), (48, 0, 0), 28),
            ("Farm", (0, -62, 58), (44, 0, 0), 32),
            ("Town", (112, -40, 55), (50, 0, 0), 30),
            ("Seaport", (30, -170, 45), (62, 0, 18), 30),
            ("Game", (0, -40, 48), (50, 0, 0), 35),
        ):
            cam = bpy.data.cameras.new("Cam " + name)
            cam.lens = lens
            cam.clip_end = 3000
            co = bpy.data.objects.new("Cam " + name, cam)
            co.location = loc
            co.rotation_euler = tuple(math.radians(a) for a in rot)
            self.root.objects.link(co)
            self.cams[name] = co
        scene.camera = self.cams["Overview"]

    def render(self, names):
        scene = bpy.context.scene
        for n in names:
            scene.camera = self.cams[n]
            scene.render.filepath = os.path.join(self.out, "world_%s.png" % n.lower())
            bpy.ops.render.render(write_still=True)
        scene.camera = self.cams["Overview"]


def build(out, render=(), blend=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    w = World(out)
    w.land()
    w.farm()
    w.town()
    w.port()
    w.transport()
    w.country()
    w.nature()
    w.stage()
    L.write(os.path.join(out, "world_layout.json"))
    import json
    with open(os.path.join(out, "world_placements.json"), "w") as f:
        json.dump(w.placements, f)
    if blend:
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "FarmverseWorld.blend"), relative_remap=True)
    if render:
        w.render(render)
    print("placements", len(w.placements), "models", len(w.models))
    return w


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    out = os.path.abspath(args[-1] if args else "world_out")
    os.makedirs(out, exist_ok=True)
    views = []
    for a in sys.argv:
        if a.startswith("--render="):
            views = a.split("=", 1)[1].split(",")
    build(out, views)
