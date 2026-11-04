"""Turn a raw Tripo GLB (about 2M triangles, 4K atlas) into a game-ready model.

python tripo_bake.py <raw.glb> <out_dir> <name> <footprint_m> <tris> [tex]

1. join + apply, scale so the longest footprint side is <footprint_m>, origin at bottom centre;
2. low-poly copy decimated to <tris> triangles;
3. clean UVs from xatlas (the decimated Tripo UVs tear across chart seams and show white streaks);
4. bake the high-poly colour and normals onto the low-poly with Cycles;
5. export <name>.glb (albedo JPEG + normal PNG, matte) plus a preview render.
Needs the bpy module and `pip install xatlas`.
"""
import sys, os, math, json, time
import bpy
import numpy as np
import xatlas
from mathutils import Vector, Matrix


def log(*a):
    print("[tripo]", *a, flush=True)


def build(src, out_dir, name, size_m, tris, tex=1024):
    t0 = time.time()
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=src)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    # frame = bounds of the whole model, so a subset of parts (FV_PARTS / FV_EXCLUDE, for moving
    # parts such as windmill sails) keeps the same scale and origin as the rest
    allpts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    fmn = Vector([min(v[i] for v in allpts) for i in range(3)])
    fmx = Vector([max(v[i] for v in allpts) for i in range(3)])
    inc = [x for x in os.environ.get("FV_PARTS", "").split(",") if x]
    exc = [x for x in os.environ.get("FV_EXCLUDE", "").split(",") if x]
    if inc or exc:
        keep = [o for o in meshes if (not inc or o.name.split(".")[0] in inc) and o.name.split(".")[0] not in exc]
        for o in meshes:
            if o not in keep:
                bpy.data.objects.remove(o, do_unlink=True)
        meshes = keep
    bpy.ops.object.select_all(action="DESELECT")
    for m in meshes:
        m.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    hi = bpy.context.view_layer.objects.active
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in list(bpy.context.scene.objects):
        if o is not hi:
            bpy.data.objects.remove(o, do_unlink=True)
    tri_before = sum(len(p.vertices) - 2 for p in hi.data.polygons)

    # scale and origin
    mn, mx = fmn, fmx
    s = size_m / max(mx.x - mn.x, mx.y - mn.y)
    hi.data.transform(Matrix.Scale(s, 4) @ Matrix.Translation(-Vector(((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, mn.z))))
    hi.name = name + "_hi"

    # high-poly material: drop its normal map so the bake reads the shape itself; keep base colour
    for m in hi.data.materials:
        if m and m.use_nodes:
            for n in list(m.node_tree.nodes):
                if n.type == "NORMAL_MAP":
                    m.node_tree.nodes.remove(n)

    # low-poly copy
    lo = bpy.data.objects.new(name, hi.data.copy())
    lo.data.name = name
    bpy.context.scene.collection.objects.link(lo)
    bpy.ops.object.select_all(action="DESELECT")
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo
    # batch exports from Tripo come with every triangle's corners split apart; weld them so the
    # decimator can collapse edges (otherwise it stalls at ~100k triangles)
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(lo.data)
    nv = len(bm.verts)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=0.0005 * size_m)
    bm.to_mesh(lo.data)
    bm.free()
    log(name, "welded", nv, "->", len(lo.data.vertices))
    mod = lo.modifiers.new("dec", "DECIMATE")
    mod.decimate_type = "COLLAPSE"
    mod.ratio = min(1.0, tris / max(tri_before, 1))
    mod.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    me = lo.data
    log(name, "decimated", tri_before, "->", len(me.polygons), round(time.time() - t0, 1), "s")

    # xatlas UVs on the decimated triangles (face order is kept, so UVs map back loop by loop)
    verts = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", verts)
    faces = np.empty(len(me.polygons) * 3, np.uint32)
    me.polygons.foreach_get("vertices", faces)
    atlas = xatlas.Atlas()
    atlas.add_mesh(verts.reshape(-1, 3), faces.reshape(-1, 3).astype(np.uint32))
    co = xatlas.ChartOptions()
    co.max_iterations = 2
    po = xatlas.PackOptions()
    po.resolution = tex
    po.padding = 3
    po.bilinear = True
    po.blockAlign = True
    atlas.generate(co, po)
    vmap, idx, uvs = atlas[0]
    assert idx.shape[0] == len(me.polygons), "xatlas changed the face count"
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    uvl = me.uv_layers.new(name="UVMap")
    loop_uv = uvs[idx.reshape(-1)]
    uvl.data.foreach_set("uv", loop_uv.astype(np.float32).ravel())
    log(name, "uv charts", atlas.chart_count, round(time.time() - t0, 1), "s")

    # bake targets
    albedo = bpy.data.images.new(name + "_albedo", tex, tex, alpha=False)
    normal = bpy.data.images.new(name + "_normal", tex, tex, alpha=False, float_buffer=False)
    normal.colorspace_settings.name = "Non-Color"
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.8
    bsdf.inputs["Metallic"].default_value = 0.0
    ta = nt.nodes.new("ShaderNodeTexImage"); ta.image = albedo; ta.location = (-600, 200)
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = normal; tn.location = (-600, -200)
    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.location = (-300, -200)
    me.materials.clear()
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True

    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = 1
    sc.render.bake.margin = 4
    ext = 0.004 * size_m
    ray = 0.015 * size_m
    bpy.ops.object.select_all(action="DESELECT")
    hi.select_set(True)
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo

    nt.nodes.active = ta
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, use_selected_to_active=True,
                        cage_extrusion=ext, max_ray_distance=ray, margin=4)
    log(name, "albedo baked", round(time.time() - t0, 1), "s")
    nt.nodes.active = tn
    bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT", use_selected_to_active=True,
                        cage_extrusion=ext, max_ray_distance=ray, margin=4)
    log(name, "normal baked", round(time.time() - t0, 1), "s")

    nt.links.new(ta.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])

    os.makedirs(out_dir, exist_ok=True)
    tex_dir = os.path.join(out_dir, "textures")
    os.makedirs(tex_dir, exist_ok=True)
    for img, ext_ in ((albedo, "png"), (normal, "png")):
        img.filepath_raw = os.path.join(tex_dir, img.name + "." + ext_)
        img.file_format = "PNG"
        img.save()
        img.pack()

    bpy.data.objects.remove(hi, do_unlink=True)
    for d in (bpy.data.meshes, bpy.data.materials, bpy.data.images):
        for x in list(d):
            if x.users == 0:
                d.remove(x)

    glb = os.path.join(out_dir, name + ".glb")
    bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB", export_image_format="AUTO",
                              export_apply=True)
    info = {"name": name, "tris_before": tri_before, "tris_after": len(lo.data.polygons),
            "dims_m": [round(d, 2) for d in lo.dimensions], "charts": int(atlas.chart_count),
            "glb_mb": round(os.path.getsize(glb) / 1e6, 2), "tex": tex, "secs": round(time.time() - t0, 1)}
    preview(lo, os.path.join(out_dir, "preview", name + ".png"))
    return info


def preview(ob, path, res=768):
    sc = bpy.context.scene
    d = max(ob.dimensions)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = d * 1.45
    a = math.radians(45)
    cam.location = (d * 2 * math.sin(a), -d * 2 * math.cos(a), d * 1.6 + ob.dimensions.z * 0.4)
    cam.rotation_euler = (math.radians(55), 0, a)
    sc.camera = cam
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3.0
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    sc.collection.objects.link(sun)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.7, 0.7, 0.72, 1)
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x = sc.render.resolution_y = res
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


if __name__ == "__main__":
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    src, out, name, size, tris = a[0], a[1], a[2], float(a[3]), int(a[4])
    tex = int(a[5]) if len(a) > 5 else 1024
    info = build(src, out, name, size, tris, tex)
    rep = os.path.join(out, "report.json")
    r = json.load(open(rep)) if os.path.exists(rep) else {}
    r[name] = info
    json.dump(r, open(rep, "w"), indent=2)
    log("DONE", json.dumps(info))
