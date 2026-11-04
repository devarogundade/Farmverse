"""FBX export for a multi-object cleaned GLB (e.g. windmill body + rotor); textures keep their image names."""
import sys, os, bpy
src, out = sys.argv[-2], sys.argv[-1]
name = os.path.splitext(os.path.basename(src))[0]
os.makedirs(os.path.join(out, "Textures"), exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
for img in bpy.data.images:
    img.filepath_raw = os.path.join(out, "Textures", img.name.replace(".png", "") + ".png")
    img.file_format = "PNG"; img.save()
bpy.ops.export_scene.fbx(filepath=os.path.join(out, name + ".fbx"), use_selection=False, object_types={"MESH", "EMPTY"},
                         apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", path_mode="RELATIVE",
                         mesh_smooth_type="FACE", use_tspace=True, bake_space_transform=True)
print("FBX", name, [o.name for o in bpy.data.objects], [i.name for i in bpy.data.images])
