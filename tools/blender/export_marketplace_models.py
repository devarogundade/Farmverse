"""Export the 3D models the marketplace renders from NFT metadata.

Run headless:
    blender -b --factory-startup -P tools/blender/export_marketplace_models.py

Outputs (web/shared/public/models, served by both websites):
    character.glb        one rig (44-bone `Skeleton`) + every outfit part as its own skinned mesh,
                         plus a few animation clips. The web renderer shows only the parts an NFT's
                         `outfit` names, so one download covers every worker.
    character.parts.json slot -> [part ids] (what `outfit.<slot>` may contain)
    items/<id>.glb       one static model per item: the game's goods and buildings (by game id, the model
                         the island shows) and the studio's tractors, land and animals
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

# What the game mints, by its own item ids, with the model the game itself shows (goods: the pack
# prefab behind Content/Art/Goods/good_<id>; buildings: the main model in Editor/Ithappy/IthappyArt.cs).
# "Food:" models come from the Food pack. These win over ITEMS where both name an id.
GAME_ITEMS = {
    # goods
    "wheat": "plant_011", "maize": "plant_001", "pumpkin": "plant_002", "tomato": "plant_003", "cabbage": "plant_004",
    "beans": "plant_009", "sunflower": "plant_008", "flowers": "plant_007", "wood": "firewood_005", "planks": "firewood_011",
    "stone": "stone_fence_006", "flour": "bag_002", "feed": "bag_001", "oil": "barrel_007", "veg_box": "case_003",
    "egg": "Food:Egg_002", "milk": "Food:Yogurt_002", "meat": "Food:Sasuage_003", "croissant": "Food:Croissant_001",
    "juice": "Food:Bottle_002", "mushroom": "Food:Mushroom_002", "fish": "Food:Fish_004", "yogurt": "Food:Coffee_005",
    # buildings
    "farmhouse": "house_007", "barn": "hangar_002", "chicken_coop": "coop_001", "pigsty": "coop_001", "stable": "hangar_005",
    "grain_silos": "tower_002", "windmill": "tower_001", "greenhouse": "greenhouse_004", "potting_shed": "tower_011",
    "packing_shed": "hangar_013", "cider_press": "hangar_011", "dairy_factory": "hangar_007", "bakery_factory": "house_001",
    "sugar_mill": "hangar_029", "feed_mill": "tower_010", "lumber_yard": "hangar_010", "fishing_shed": "house_031",
    "mine_entrance": "mountain_001", "market_stall": "stall_002", "grocer_shop": "stall_001", "bakery_shop": "house_027",
    "fish_market": "stall_001", "workers_house": "house_009", "army_house": "house_011", "builders_hut": "house_028",
    "police_station": "house_008", "fire_station": "hangar_004", "clinic": "house_010", "school": "house_023",
    "library": "house_013", "chapel": "house_024", "community_hall": "house_005", "post_office": "house_017",
    "bank": "house_018", "town_hall": "house_006", "facility_well": "tower_004", "facility_warehouse": "hangar_008",
}
ITEMS = {**ITEMS, **GAME_ITEMS}
FOOD = os.path.join(REPO, "Assets", "ithappy", "Food_Free")


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
    os.makedirs(os.path.join(OUT, "items"), exist_ok=True)
    only = set(filter(None, os.environ.get("ONLY", "").split(",")))
    for item_id, src in ITEMS.items():
        if only and item_id not in only:
            continue
        reset()
        # each pack paints every model from one texture
        if src.startswith("Food:"):
            mat = textured_material("Food", os.path.join(FOOD, "Textures", "Textures_4.png"))
            path = os.path.join(FOOD, "Meshes", src[5:] + ".fbx")
        else:
            mat = textured_material("Farm", os.path.join(FARM, "Textures", "Main_Texture.png"))
            path = os.path.join(FARM, "Meshes", src + ".fbx")
        objs = import_fbx(path)
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


# ITEMS_ONLY=1 skips the character (slow); ONLY=wood,barn exports just those items
if not os.environ.get("ITEMS_ONLY"):
    export_character()
export_items()
