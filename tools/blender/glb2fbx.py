"""Convert the cleaned Tripo GLBs to FBX for Unity (which imports FBX natively).
python glb2fbx.py <dir_with_glbs> <fbx_out_dir>   textures are written as <name>_albedo.png / <name>_normal.png"""
import sys, os, glob
import bpy

src, out = sys.argv[1], sys.argv[2]
os.makedirs(os.path.join(out, "Textures"), exist_ok=True)
for glb in sorted(glob.glob(os.path.join(src, "*.glb"))):
    name = os.path.splitext(os.path.basename(glb))[0]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glb)
    for img in bpy.data.images:
        kind = "normal" if "normal" in img.name.lower() else "albedo"
        img.filepath_raw = os.path.join(out, "Textures", "%s_%s.png" % (name, kind))
        img.file_format = "PNG"
        img.save()
    for ob in bpy.context.scene.objects:
        if ob.type == "MESH":
            ob.name = name
    bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=False,
                             object_types={"MESH"}, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", path_mode="RELATIVE", mesh_smooth_type="FACE",
                             use_tspace=True, bake_space_transform=True)
    print("FBX", name, flush=True)
