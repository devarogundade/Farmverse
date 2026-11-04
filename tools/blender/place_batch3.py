# Background: blender -b WorldBlockout.blend --python place_batch3.py  (imports batch-3 models, places them, saves)
import bpy, os
from mathutils import Vector
CLEAN = r"C:\Users\devar\Documents\Farmverse\ArtSource\Tripo\clean"
sc = bpy.data.scenes["World Blockout"]
bpy.context.window_manager.windows[0].scene = sc if bpy.context.window_manager.windows else None
lib = bpy.data.collections["Tripo Models"]
names = ["textile_mill","lumber_yard","packing_shed","bee_hives","cider_press","fish_market","smokehouse","ore_shed","stone_crusher","potting_shed"]
for n in names:
    cname = "tripo." + n
    if bpy.data.collections.get(cname): continue
    c = bpy.data.collections.new(cname); lib.children.link(c)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=os.path.join(CLEAN, n + ".glb"))
    for o in set(bpy.data.objects) - before:
        for uc in list(o.users_collection): uc.objects.unlink(o)
        c.objects.link(o)
def fp(model):
    obs = [o for o in bpy.data.collections["tripo."+model].objects if o.type == "MESH"]
    pts = [o.matrix_world @ Vector(v) for o in obs for v in o.bound_box]
    return (max(p.x for p in pts)-min(p.x for p in pts), max(p.y for p in pts)-min(p.y for p in pts))
MAP = {"factories.textile":"textile_mill","factories.lumber":"lumber_yard","orchard.packing":"packing_shed","orchard.bees":"bee_hives","orchard.press":"cider_press","fishery.market":"fish_market","fishery.smokehouse":"smokehouse","fishery.nets":"fishing_shed","mine.ore":"ore_shed","mine.crusher":"stone_crusher","greenhouses.potting":"potting_shed"}
inst = bpy.data.collections["Tripo Placed"]
for bname, model in MAP.items():
    old = sc.objects.get("tripo@" + bname)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    b = sc.objects[bname]
    pts = [b.matrix_world @ Vector(c) for c in b.bound_box]
    z0 = min(p.z for p in pts)
    mw, md = fp(model)
    s = max(0.7, min(1.3, min(b.dimensions.x*1.15/mw, b.dimensions.y*1.6/md)))
    e = bpy.data.objects.new("tripo@" + bname, None)
    e.instance_type = "COLLECTION"; e.instance_collection = bpy.data.collections["tripo." + model]
    e.location = (b.location.x, b.location.y, z0); e.rotation_euler = (0, 0, b.rotation_euler.z); e.scale = (s, s, s)
    e["model"] = model; e["block"] = bname
    inst.objects.link(e)
    for nm in (bname, bname + ".label"):
        o = sc.objects.get(nm)
        if o: o.hide_render = True; o.hide_viewport = True
    print("PLACED", bname, model, round(s, 2))
# keep the old Tripo-model library hidden from the view layer
def lc(l, n):
    if l.name == n: return l
    for c in l.children:
        r = lc(c, n)
        if r: return r
lc(sc.view_layers[0].layer_collection, "Tripo Models").exclude = True
bpy.ops.wm.save_mainfile()
print("SAVED")
