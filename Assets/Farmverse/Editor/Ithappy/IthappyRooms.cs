#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Farmverse.Domain.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Farmverse.Editor
{
    /// <summary>
    /// The farmhouse's five rooms (plan: Farmhouse interiors) as open-sided dioramas: a floor and two
    /// walls (back wall along +Z, left wall along −X, open towards +X/−Z), a window and a door from the
    /// Christmas pack's house kit, and furniture from the Christmas and Cute Furniture packs. Every
    /// buyable item (GameplayRules.RoomItems) is in the prefab as a hidden child opt_&lt;id&gt;, and is also
    /// saved alone under Content/Art/Furniture for the shop pictures (f:&lt;id&gt;).
    /// Output: Content/Art/Rooms/room_&lt;name&gt;.prefab.
    /// </summary>
    public static class IthappyRooms
    {
        public const string Rooms = IthappyArt.Art + "/Rooms";
        public const string Furniture = IthappyArt.Art + "/Furniture";
        const float W = 6f, D = 5f, H = 3.1f, T = 0.22f;   // floor width (x) and depth (z), wall height and thickness

        enum Pk { Xmas, Furn, Farm }

        /// <summary>One thing in a room: where it stands (bounds centre x/z, bounds bottom on the floor), which way it faces,
        /// or how high it hangs (bounds centre y) when on a wall; an id makes it a buyable extra.</summary>
        sealed class Item
        {
            public Pk pack; public string[] names; public float x, z, yaw, hang = -1f, scale = 1f; public string? opt;
        }
        static Item I(Pk pack, string name, float x, float z, float yaw = 0f, float hang = -1f, float scale = 1f, string? opt = null)
            => new Item { pack = pack, names = new[] { name }, x = x, z = z, yaw = yaw, hang = hang, scale = scale, opt = opt };
        static Item G(Pk pack, string[] names, float x, float z, float yaw = 0f, float hang = -1f, float scale = 1f, string? opt = null)
            => new Item { pack = pack, names = names, x = x, z = z, yaw = yaw, hang = hang, scale = scale, opt = opt };

        sealed class RoomDef { public string name = ""; public Color wall, floor; public bool tiles; public Item[] items = Array.Empty<Item>(); public float windowX = 0.6f; }

        // pack models face +Z: against the back wall (z = +2.5) they turn 180 to face the viewer; against the left wall (x = −3) they turn 90 to face +X
        static readonly RoomDef[] Defs =
        {
            new RoomDef
            {
                name = "living", wall = new Color(0.94f, 0.87f, 0.74f), floor = new Color(0.60f, 0.42f, 0.27f), windowX = 1.3f,
                items = new[]
                {
                    I(Pk.Xmas, "Couch_001", 0.9f, 1.95f, 180f),
                    I(Pk.Xmas, "Armchair_001", -1.9f, 0.4f, 90f),
                    I(Pk.Xmas, "Carpet_001", 0.7f, 0.2f),
                    I(Pk.Xmas, "Coffee_Table_001", 0.7f, 0.2f),
                    I(Pk.Xmas, "Book_001", 0.55f, 0.25f, 20f, 0.55f, 0.8f),
                    G(Pk.Xmas, new[] { "Fireplace_001", "Fireplace_fire_001", "Fireplace_coal_001", "Fireplace_firewood_001", "Fireplace_pipe_001" }, -2.55f, -1.2f, 90f),
                    I(Pk.Xmas, "Painting_001", -0.9f, 2.36f, 180f, 1.75f),
                    I(Pk.Xmas, "Clock_001", -2.86f, 1.0f, 90f, 2.1f),
                    I(Pk.Xmas, "Light_001", 2.55f, 2.0f),
                    I(Pk.Xmas, "Hanger_001", -2.6f, 2.1f, 90f),
                    I(Pk.Furn, "Plants_05", 2.6f, 0.6f),
                    // extras
                    I(Pk.Xmas, "Christmass_tree_indoor_001_toys", 2.3f, -1.4f, 0f, -1f, 0.9f, "tree_indoor"),
                    I(Pk.Xmas, "Pouf_001", -0.6f, -1.0f, 30f, -1f, 1f, "pouf"),
                    I(Pk.Furn, "Armchair_02", 2.3f, 0.3f, -90f, -1f, 1f, "armchair_2"),
                    I(Pk.Furn, "Guitar_01", -2.7f, -0.1f, 120f, -1f, 1f, "guitar"),
                    I(Pk.Furn, "Picture_08", 1.6f, 2.36f, 180f, 1.9f, 1f, "picture_sunset"),
                    I(Pk.Furn, "Plants_15", -2.5f, 1.0f, 0f, -1f, 1f, "plant_tall"),
                    G(Pk.Xmas, new[] { "Book_002", "Book_003", "Book_004" }, 0.9f, 0.15f, -15f, 0.55f, 0.8f, "books"),
                },
            },
            new RoomDef
            {
                name = "kitchen", wall = new Color(0.82f, 0.90f, 0.83f), floor = new Color(0.86f, 0.80f, 0.68f), tiles = true, windowX = 1.9f,
                items = new[]
                {
                    I(Pk.Xmas, "Kitchen_Cabinet_001", -2.0f, 2.15f, 180f),
                    I(Pk.Xmas, "Kitchen_sink_001", -0.9f, 2.15f, 180f),
                    I(Pk.Xmas, "Kitchen_stove_001", 0.2f, 2.15f, 180f),
                    I(Pk.Xmas, "Kitchen_Cabinet_002", 1.3f, 2.15f, 180f),
                    I(Pk.Xmas, "Kitchen_Shelf_001", -1.4f, 2.3f, 180f, 1.75f),
                    I(Pk.Xmas, "Kitchen_Shelf_002", 0.8f, 2.3f, 180f, 1.75f),
                    I(Pk.Xmas, "Fridge_001", -2.6f, -0.3f, 90f),
                    I(Pk.Xmas, "Kitchen_Table_001", 0.5f, -0.4f),
                    I(Pk.Xmas, "Kitchen_stool_003", 0.5f, 0.35f, 180f),
                    I(Pk.Xmas, "Kitchen_stool_003", 0.5f, -1.15f),
                    I(Pk.Xmas, "Teapot_001", 0.15f, -0.4f, 0f, 0.84f, 0.8f),
                    I(Pk.Xmas, "Plate_001", 0.75f, -0.25f, 0f, 0.77f, 0.8f),
                    I(Pk.Xmas, "Cup_001", 0.95f, -0.6f, 0f, 0.80f, 0.8f),
                    I(Pk.Xmas, "Bowl_001", 0.4f, -0.7f, 0f, 0.80f, 0.8f),
                    I(Pk.Xmas, "Spices_001", 1.5f, 2.1f, 0f, 0.95f, 0.8f),
                    I(Pk.Xmas, "Salt_shaker_001", 1.1f, 2.1f, 0f, 0.92f, 0.8f),
                    I(Pk.Xmas, "Utensils_001", -0.9f, 2.25f, 180f, 1.3f, 0.8f),
                    // extras
                    I(Pk.Furn, "Microwave_01", -2.0f, 2.1f, 180f, 1.05f, 1f, "microwave"),
                    I(Pk.Furn, "Mixer_08", 1.3f, 2.1f, 180f, 1.05f, 0.9f, "mixer"),
                    I(Pk.Furn, "Kitchen_D_08", 2.2f, -0.2f, -90f, -1f, 1f, "island"),
                    I(Pk.Furn, "Cutting_board_02", -0.3f, 2.1f, 0f, 0.9f, 0.9f, "cutting_board"),
                    I(Pk.Furn, "Plants_19", 2.5f, 1.8f, 0f, -1f, 1f, "herb_pot"),
                    I(Pk.Furn, "Picture_21", -2.86f, 1.5f, 90f, 1.9f, 1f, "picture_fruit"),
                },
            },
            new RoomDef
            {
                name = "bedroom", wall = new Color(0.80f, 0.86f, 0.95f), floor = new Color(0.66f, 0.48f, 0.32f), windowX = 1.9f,
                items = new[]
                {
                    I(Pk.Xmas, "Bed_001", -0.7f, 1.4f, 180f),
                    I(Pk.Xmas, "Pillow_001", -1.1f, 2.0f, 180f, 0.75f, 0.9f),
                    I(Pk.Xmas, "Pillow_002", -0.3f, 2.0f, 180f, 0.75f, 0.9f),
                    I(Pk.Xmas, "Nightstand_001", 0.9f, 2.1f, 180f),
                    I(Pk.Furn, "Light_05", 0.9f, 2.1f, 0f, 0.85f, 0.8f),
                    I(Pk.Furn, "Closet_01", -2.65f, -0.8f, 90f),
                    I(Pk.Xmas, "Carpet_002", 0.6f, -0.6f),
                    I(Pk.Furn, "Picture_17", 1.9f, 2.36f, 180f, 1.9f),
                    I(Pk.Xmas, "Light_002", 2.5f, 1.9f),
                    // extras
                    I(Pk.Furn, "Toy_02", -1.6f, -0.3f, 30f, -1f, 1f, "teddy"),
                    I(Pk.Furn, "Toy_03", 1.6f, -0.9f, -40f, -1f, 1f, "toy_car"),
                    I(Pk.Furn, "Closet_02", -2.65f, 0.8f, 90f, -1f, 1f, "closet_2"),
                    I(Pk.Furn, "Clock_03", 0.9f, 2.0f, 0f, 0.82f, 0.7f, "bed_clock"),
                    I(Pk.Furn, "Picture_08", -2.86f, 0.0f, 90f, 1.9f, 1f, "picture_flowers"),
                    I(Pk.Furn, "Plants_05", 2.5f, -1.6f, 0f, -1f, 1f, "plant_bedroom"),
                },
            },
            new RoomDef
            {
                name = "bathroom", wall = new Color(0.84f, 0.93f, 0.95f), floor = new Color(0.88f, 0.90f, 0.92f), tiles = true, windowX = 2.0f,
                items = new[]
                {
                    I(Pk.Furn, "Bath_03", -0.6f, 2.0f, 180f),
                    I(Pk.Furn, "Toilet_03", -2.6f, 1.2f, 90f),
                    I(Pk.Furn, "Wash_Basin_07", -2.6f, -0.6f, 90f),
                    I(Pk.Furn, "Mixer_08", -2.7f, -0.6f, 90f, 1.15f, 0.7f),
                    I(Pk.Xmas, "Light_003", 2.5f, 1.9f),
                    I(Pk.Xmas, "Hanger_001", 2.6f, -1.0f, -90f),
                    // extras
                    I(Pk.Xmas, "Carpet_002", 0.3f, -0.4f, 0f, -1f, 0.75f, "bath_mat"),
                    I(Pk.Furn, "Plants_15", 2.4f, 0.5f, 0f, -1f, 0.9f, "bath_plant"),
                    I(Pk.Furn, "Picture_21", 1.6f, 2.36f, 180f, 1.9f, 1f, "bath_picture"),
                    G(Pk.Furn, new[] { "Toothbrush_01", "Toothpaste_01" }, -2.45f, -0.5f, 90f, 1.12f, 0.8f, "toothbrushes"),
                },
            },
            new RoomDef
            {
                name = "study", wall = new Color(0.87f, 0.88f, 0.78f), floor = new Color(0.58f, 0.40f, 0.26f), windowX = -0.4f,
                items = new[]
                {
                    I(Pk.Furn, "Work_Table_06", -1.6f, 2.0f, 180f),
                    I(Pk.Furn, "Computer_01", -1.6f, 2.0f, 180f, 1.0f, 0.9f),
                    I(Pk.Furn, "Keyboard_01", -1.6f, 1.6f, 180f, 0.80f, 0.9f),
                    I(Pk.Furn, "Mouse_01", -1.05f, 1.6f, 180f, 0.80f, 0.9f),
                    I(Pk.Furn, "Chair_PC_04", -1.6f, 1.1f),
                    I(Pk.Furn, "Paper_01", -2.2f, 1.7f, 15f, 0.78f, 0.9f),
                    I(Pk.Furn, "Book_03", 1.0f, 2.15f, 180f, 0.9f, 0.9f),
                    I(Pk.Furn, "Book_08", -2.65f, 0.6f, 90f, 1.3f, 0.9f),
                    I(Pk.Xmas, "Cabinet_001", 1.2f, 2.1f, 180f),
                    I(Pk.Xmas, "Carpet_001", 0.3f, -0.5f),
                    I(Pk.Xmas, "Light_001", 2.5f, 1.9f),
                    // extras
                    I(Pk.Furn, "GameConsole_01", 1.2f, 2.0f, 180f, 0.95f, 0.9f, "console"),
                    I(Pk.Furn, "ExerciseBike_01", 2.0f, -1.3f, -60f, -1f, 1f, "exercise_bike"),
                    I(Pk.Furn, "Guitar_01", -2.7f, -1.4f, 110f, -1f, 1f, "study_guitar"),
                    I(Pk.Furn, "Plants_19", 2.5f, 0.6f, 0f, -1f, 1f, "study_plant"),
                    I(Pk.Furn, "Picture_17", 0.6f, 2.36f, 180f, 1.9f, 1f, "study_picture"),
                    I(Pk.Furn, "Clock_03", -2.86f, 1.6f, 90f, 2.1f, 1.4f, "study_clock"),
                },
            },
        };

        [MenuItem("Farmverse/Art/Build ITHappy Rooms", priority = 3)]
        public static void BuildAll()
        {
            IthappyArt.Folder(Rooms); IthappyArt.Folder(Furniture);
            var made = 0; var items = 0;
            var seen = new HashSet<string>();
            foreach (var def in Defs)
            {
                var root = new GameObject("room_" + def.name);
                try
                {
                    Shell(root.transform, def);
                    foreach (var it in def.items)
                    {
                        var go = Place(root.transform, it);
                        if (go == null) continue;
                        if (it.opt != null)
                        {
                            go.name = "opt_" + it.opt;
                            go.SetActive(false);
                            if (seen.Add(it.opt)) { SaveAlone(it); items++; }
                        }
                    }
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Rooms}/room_{def.name}.prefab");
                    made++;
                }
                finally { Object.DestroyImmediate(root); }
            }
            foreach (var item in GameplayRules.RoomItems)
                if (!seen.Contains(item.Id)) Debug.LogWarning($"[Rooms] GameplayRules.RoomItems has {item.Id} but no room places it");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Rooms] {made} rooms, {items} furniture items built under {Rooms}");
        }

        /// <summary>Floor, back wall (+Z) and left wall (−X), skirting, a window in the back wall and the doorway in the left wall.</summary>
        static void Shell(Transform room, RoomDef def)
        {
            var floor = Box(room, "Floor", new Vector3(0, -0.1f, 0), new Vector3(W, 0.2f, D), Mat("room_floor_" + def.name, def.floor, def.tiles ? 0.45f : 0.25f));
            Box(room, "Back wall", new Vector3(0, H * 0.5f, D * 0.5f + T * 0.5f), new Vector3(W + T, H, T), Mat("room_wall_" + def.name, def.wall, 0.1f));
            Box(room, "Left wall", new Vector3(-W * 0.5f - T * 0.5f, H * 0.5f, 0), new Vector3(T, H, D), Mat("room_wall_" + def.name, def.wall, 0.1f));
            var trim = Mat("room_trim", new Color(0.97f, 0.95f, 0.90f), 0.3f);
            Box(room, "Skirting back", new Vector3(0, 0.06f, D * 0.5f - 0.03f), new Vector3(W, 0.12f, 0.06f), trim);
            Box(room, "Skirting left", new Vector3(-W * 0.5f + 0.03f, 0.06f, 0), new Vector3(0.06f, 0.12f, D), trim);
            Box(room, "Cornice back", new Vector3(0, H - 0.05f, D * 0.5f - 0.03f), new Vector3(W, 0.1f, 0.06f), trim);
            Box(room, "Cornice left", new Vector3(-W * 0.5f + 0.03f, H - 0.05f, 0), new Vector3(0.06f, 0.1f, D), trim);
            // the house kit's window on the back wall and its doorway (with the door) in the left wall
            Place(room, I(Pk.Xmas, "Window_003", def.windowX, D * 0.5f - 0.05f, 180f, 1.85f, 0.95f));
            Place(room, G(Pk.Xmas, new[] { "Doorway_001", "Door_001" }, -W * 0.5f + 0.1f, -1.3f, 90f, -1f, 0.95f));
            // outside the window: a little daylight
            var day = new GameObject("Daylight").AddComponent<Light>();
            day.transform.SetParent(room, false);
            day.transform.localPosition = new Vector3(def.windowX, 2.2f, D * 0.5f - 0.6f);
            day.type = LightType.Point;
            day.range = 5f;
            day.intensity = 1.1f;
            day.color = new Color(0.85f, 0.92f, 1f);
            _ = floor;
        }

        static GameObject Box(Transform parent, string name, Vector3 at, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Material Mat(string id, Color colour, float smooth)
        {
            IthappyArt.Folder(IthappyArt.Materials);
            var path = $"{IthappyArt.Materials}/{id}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", colour);
            mat.SetFloat("_Smoothness", smooth);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static GameObject? Prefab(Pk pack, string name) => pack switch
        {
            Pk.Xmas => IthappyArt.Xmas(name),
            Pk.Furn => IthappyArt.Furn(name),
            _ => IthappyArt.Pack(name),
        };

        /// <summary>Instantiate an item's prefabs under one holder, turn and scale it, then stand it on the floor (or hang it) at x/z.</summary>
        static GameObject? Place(Transform room, Item it)
        {
            var holder = new GameObject(it.names[0]);
            holder.transform.SetParent(room, false);
            var any = false;
            foreach (var n in it.names)
            {
                var pf = Prefab(it.pack, n);
                if (pf == null) { Debug.LogWarning($"[Rooms] no prefab {it.pack}:{n}"); continue; }
                var go = IthappyArt.Inst(pf, holder.transform);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                any = true;
            }
            if (!any) { Object.DestroyImmediate(holder); return null; }
            holder.transform.localRotation = Quaternion.Euler(0, it.yaw, 0);
            holder.transform.localScale = Vector3.one * it.scale;
            var b = IthappyArt.WorldBounds(holder);
            var want = new Vector3(it.x, it.hang >= 0f ? it.hang : 0f, it.z);
            var have = new Vector3(b.center.x, it.hang >= 0f ? b.center.y : b.min.y, b.center.z);
            holder.transform.position += room.TransformVector(want - have);
            return holder;
        }

        /// <summary>The item alone, standing at the origin, for the shop pictures.</summary>
        static void SaveAlone(Item it)
        {
            var root = new GameObject(it.opt);
            try
            {
                var copy = new Item { pack = it.pack, names = it.names, x = 0, z = 0, yaw = 0, hang = -1f, scale = 1f };
                var go = Place(root.transform, copy);
                if (go == null) return;
                go.name = "Model";
                PrefabUtility.SaveAsPrefabAsset(root, $"{Furniture}/{it.opt}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
