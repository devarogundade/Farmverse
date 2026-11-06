"""Export the 3D models the marketplace renders from NFT metadata.

Run headless:
    blender -b --factory-startup -P tools/blender/export_marketplace_models.py

Outputs (web/shared/public/models, served by both websites):
    character.glb        one rig (44-bone `Skeleton`) + every outfit part as its own skinned mesh,
                         plus a few animation clips. The web renderer shows only the parts an NFT's
                         `outfit` names, so one download covers every worker.
    character.parts.json slot -> [part ids] (what `outfit.<slot>` may contain)
    items/<id>.glb       one static model per item NFT type (tractor, building, land, goods, animals)
"""

import json
import os

import bpy

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
CHAR = os.path.join(REPO, "Assets", "ithappy", "Creative_Characters_FREE")
FARM = os.path.join(REPO, "Assets", "ithappy", "Farm")
OUT = os.path.join(REPO, "web", "shared", "public", "models")

# Folder in the pack -> outfit slot used in NFT metadata.
SLOTS = {
    "Hairstyle": "hair",
    "Hairstyle Single": "hair",
    "Hat": "hat",
    "Hat Single": "hat",
    "Faces": "face",
    "Glasses": "glasses",
    "Face Accessories": "accessory",
    "Outwear": "top",
    "Outfit": "top",
    "Pants": "bottom",
    "Shorts": "bottom",
    "Gloves": "glove",
    "Socks": "socks",
    "Shoes": "shoes",
    "Costumes": "costume",
    "Mascots": "costume",
}

CLIPS = ["Idle_Relaxed", "Idle_Look_Around", "Walk_Forward", "Run_Forward", "Attack_Punch", "Jump_Start"]

# Item NFT type id -> FBX in the Farm pack.
ITEMS = {
    "tractor_gold": "tractor_001", "tractor_blue": "tractor_002", "tractor_green": "tractor_003",
    "tractor_red": "tractor_004", "harvester": "harvester_001",
    "farmhouse": "house_001", "cottage": "house_005", "greenhouse": "greenhouse_001",
    "coop": "coop_001", "barn": "hangar_001", "silo": "tower_001", "apiary": "apiary_001",
    "field": "field_004", "garden_bed": "garden_bed_001",
    "wheat": "hay_001", "flour": "bag_001", "milk": "barrel_001", "egg": "box_004",
    "honey": "bucket_001", "tomato": "plant_003",
    "cow": "Animals/Cow_01", "chicken": "Animals/Chicken_01",
    "sheep": "Animals/Sheep_01", "pig": "Animals/Pig_01",
}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def textured_material(name, image_path, *, emission=False, glass=False):
    mat = bpy.data.materials.new(name)
    nt = mat.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.85
    if glass:
        bsdf.inputs["Base Color"].default_value = (0.6, 0.8, 1.0, 1)
        bsdf.inputs["Alpha"].default_value = 0.35
        bsdf.inputs["Roughness"].default_value = 0.1
        return mat
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(image_path, check_existing=True)
    tex.interpolation = "Closest"  # palette atlases: keep colour cells crisp
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    if emission:
        nt.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
        bsdf.inputs["Emission Strength"].default_value = 1.5
    return mat


def find_fbx(folder):
    root = os.path.join(CHAR, "Meshes", folder)
    return sorted(os.path.join(root, f) for f in os.listdir(root) if f.lower().endswith(".fbx"))


def export_character():
    reset()
    atlas = os.path.join(CHAR, "Textures", "Textures.png")
    mats = {
        "Color": textured_material("Character", atlas),
        "Emission": textured_material("Emission", atlas, emission=True),
        "Glass": textured_material("Glass", atlas, glass=True),
    }

    # Base rig with the animation clips.
    anim_objs = import_fbx(os.path.join(CHAR, "Animations", "Animation_Mesh", "Aminset_Basic.fbx"))
    rig = next(o for o in anim_objs if o.type == "ARMATURE")
    rig.name = "Skeleton"
    for o in anim_objs:
        if o.type == "MESH":
            bpy.data.objects.remove(o)

    def attach(mesh, name, slot):
        mesh.name = name
        mesh.data.name = name
        world = mesh.matrix_world.copy()
        mesh.parent = rig
        mesh.matrix_world = world
        for mod in mesh.modifiers:
            if mod.type == "ARMATURE":
                mod.object = rig
        mesh["slot"] = slot
        for i, m in enumerate(mesh.data.materials):
            base = (m.name.split(".")[0] if m else "Color")
            mesh.data.materials[i] = mats.get(base, mats["Color"])

    def import_part(path, slot, keep=None):
        objs = import_fbx(path)
        stem = os.path.splitext(os.path.basename(path))[0]
        meshes = [o for o in objs if o.type == "MESH" and len(o.data.vertices) > 8]
        if keep:
            meshes = [o for o in meshes if o.name.startswith(keep)]
        for i, mesh in enumerate(meshes):
            attach(mesh, stem if i == 0 else f"{stem}_{i}", slot)
        for o in objs:
            if o not in meshes:
                bpy.data.objects.remove(o)
        return stem if meshes else None

    # Body from the base mesh (drop its 4-vertex placeholder slots).
    # Skinned body the pack's prefabs use (Base_Mesh's own body has neutral, untextured UVs).
    import_part(os.path.join(CHAR, "Meshes", "Body", "Body_010.fbx"), "body")
    body = bpy.data.objects["Body_010"]
    body.name = body.data.name = "Body"

    parts = {}
    for folder, slot in SLOTS.items():
        for path in find_fbx(folder):
            pid = import_part(path, slot)
            if pid:
                parts.setdefault(slot, []).append(pid)

    # Keep only the clips we ship; strip the "Skeleton|" prefix.
    for act in list(bpy.data.actions):
        clip = act.name.split("|")[-1]
        if clip in CLIPS:
            act.name = clip
            act.use_fake_user = True
        else:
            bpy.data.actions.remove(act)
    rig.animation_data.action = bpy.data.actions[CLIPS[0]]

    os.makedirs(OUT, exist_ok=True)
    bpy.ops.export_scene.gltf(
        filepath=os.path.join(OUT, "character.glb"),
        export_format="GLB",
        export_image_format="WEBP",
        export_image_quality=85,
        export_extras=True,
        export_animation_mode="ACTIONS",
        export_skins=True,
        export_apply=False,
        export_yup=True,
    )
    with open(os.path.join(OUT, "character.parts.json"), "w", newline="\n") as f:
        json.dump({"slots": parts, "clips": CLIPS}, f, indent=2)
    print("CHARACTER", {k: len(v) for k, v in parts.items()})


def export_items():
    atlas = os.path.join(FARM, "Textures", "Main_Texture.png")
    os.makedirs(os.path.join(OUT, "items"), exist_ok=True)
    for item_id, src in ITEMS.items():
        reset()
        mat = textured_material("Farm", atlas)
        objs = import_fbx(os.path.join(FARM, "Meshes", src + ".fbx"))
        for o in objs:
            if o.type == "MESH":
                if not o.data.materials:
                    o.data.materials.append(mat)
                for i in range(len(o.data.materials)):
                    o.data.materials[i] = mat
        bpy.ops.export_scene.gltf(
            filepath=os.path.join(OUT, "items", item_id + ".glb"),
            export_format="GLB",
        export_image_format="WEBP",
        export_image_quality=85,
            export_animation_mode="ACTIONS",
            export_yup=True,
        )
        print("ITEM", item_id, src)


export_character()
export_items()
