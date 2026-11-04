#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Farmverse.Presentation.World;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse / Art / Build ITHappy Characters: the island's people, raiders and animals from the
    /// ITHappy packs.
    ///  - People stand on the Creative Characters rig (Base_Mesh) and wear parts from
    ///    <see cref="CharacterParts"/>; each role (player, army, labourer, builder, townsfolk) has a
    ///    <see cref="CharacterLook"/> with its default look, which the player can change in Customize.
    ///    One Villager.controller (states Idle, Walk, Run, Dig, Chop…) drives them all: its clips are
    ///    copies of the packs' humanoid clips with the right looping.
    ///  - Raiders are the Zombies Mini characters on their own Zombie.controller.
    ///  - Animals are the Farm pack's rigged animals, each with a controller (Idle, Walk, Run, Eat…)
    ///    made from its own clips, scaled to a height.
    /// Every prefab gets CharacterMotion, so the island moves them all the same way.
    /// </summary>
    public static class IthappyCharacters
    {
        public const string People = IthappyArt.Art + "/People";
        public const string Animals = IthappyArt.Art + "/Animals";
        const string Clips = People + "/Clips";
        const string CC = "Assets/ithappy/Creative_Characters_FREE/";
        const string ZM = "Assets/ithappy/Zombies_Mini/";
        const string FarmAnim = "Assets/ithappy/Farm/Animations/";
        public const string PartsPath = People + "/CharacterParts.asset";
        public const string VillagerController = People + "/Villager.controller";
        public const string ZombieController = People + "/Zombie.controller";

        [MenuItem("Farmverse/Art/Build ITHappy Characters", priority = 2)]
        public static void BuildAll()
        {
            IthappyArt.Folder(People); IthappyArt.Folder(Animals); IthappyArt.Folder(Clips);
            var parts = BuildParts();
            var villager = BuildController(VillagerController, VillagerStates);
            var zombie = BuildController(ZombieController, ZombieStates);
            int n = 0;
            foreach (var r in Roles) if (BuildPerson(r, parts, villager)) n++;
            BuildCatalog(parts, villager);
            foreach (var z in Zombies) if (BuildZombie(z.id, z.pack, zombie)) n++;
            // a cured boss: the same clothes, human skin, a townsperson's moves
            foreach (var z in Zombies) BuildCured(z.id, villager);
            BuildSanta(villager);
            foreach (var a in AnimalList) if (BuildAnimal(a)) n++;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ITHappy] {n} character prefabs under {People} and {Animals}");
        }

        // ------------------------------------------------------------------ clips and controllers

        /// <summary>A state of a controller: its name, the pack clip it plays, whether it loops, and whether a
        /// finished one-shot goes back to Idle (false = it holds its last pose, like lying beaten).</summary>
        public struct State
        {
            public string name, clip; public bool loop, back;
            public State(string name, string clip, bool loop, bool back = true) { this.name = name; this.clip = clip; this.loop = loop; this.back = back; }
        }

        static string Creative(string n) => CC + "Animations/Other_Animations/" + n + ".anim";
        static string Zomb(string n) => ZM + "Animations/Animations_Zombies/" + n + ".anim";

        static string Santa(string n) => "Assets/ithappy/Christmas_Pack_FREE/Animations/Other_Animations/Santa_" + n + ".anim";

        /// <summary>The dances (Zombies Mini and Santa's): parties after a beaten night, festivals, the café. Dance1..DanceN.</summary>
        public static readonly string[] DanceClips =
        {
            Zomb("Silly_Dancing"), Zomb("Gangnam_Style"), Zomb("Salsa_Dancing"), Zomb("Swing_Dance"), Zomb("Chicken_Dance"),
            Zomb("Bboy_Hip_Hop_Move"), Zomb("Breakdance_Ready"), Zomb("Dancing_Running_Man"), Zomb("Dancing_Twerk"), Zomb("Head_Spin"),
            Zomb("Hip_Hop_Dance_5"), Zomb("Hip_Hop_Dancing"), Zomb("Hip_Hop_Dancing_1"), Zomb("Hip_Hop_Dancing_2"), Zomb("Hip_Hop_Dancing_3"),
            Zomb("Hip_Hop_Dancing_4"), Zomb("Jazz_Dancing"), Zomb("Rumba_Dancing"), Zomb("Shuffling"),
            Santa("Chicken_Dance"), Santa("DanceIdle"), Santa("Dancing_Running_Man"), Santa("Gangnam_Style"), Santa("Hip_Hop_Dancing"),
            Santa("Hip_Hop_Dancing_1"), Santa("Hip_Hop_Dancing_2"), Santa("Hip_Hop_Dancing_3"), Santa("Hip_Hop_Dancing_4"), Santa("Hip_Hop_Dancing_5"),
            Santa("Hip_Hop_Move"), Santa("Jazz_Dancing"), Santa("Rumba_Dancing"), Santa("Rumba_Dancing.001"), Santa("Salsa_Dancing"),
            Santa("Shuffling"), Santa("Silly_Dancing"),
        };

        static State[] Dances() => DanceClips.Select((c, i) => new State("Dance" + (i + 1), c, false)).ToArray();

        /// <summary>
        /// People: every humanoid clip in the packs (Creative 14, Zombies Mini 47, Santa 27) has a state.
        /// Walk/Run have back, left and right versions (CharacterMotion picks by the way they move against the way they face).
        /// </summary>
        public static readonly State[] VillagerStates = new[]
        {
            new State("Idle", Creative("Idle_Relaxed"), true),
            new State("Relax", Creative("Idle_Breathing"), true),
            new State("LookAround", Creative("Idle_Look_Around"), false),
            new State("LookAround2", Santa("IdleLookAround"), false),
            new State("LookAround3", Santa("IdleLookAround_2"), false),
            new State("Walk", Creative("Walk_Forward"), true),
            new State("WalkBack", Creative("Walk_Backward"), true),
            new State("WalkLeft", Creative("Strafe_Left"), true),
            new State("WalkRight", Creative("Strafe_Right"), true),
            new State("Run", Creative("Run_Forward"), true),
            new State("RunBack", Creative("Run_Backward"), true),
            new State("RunLeft", Creative("Run_Left"), true),
            new State("RunRight", Creative("Run_Right"), true),
            new State("Hurry", Santa("WalkFast"), true),
            new State("Carry", Santa("WalkWithItem"), true),
            new State("Flee", Santa("Run"), true),
            new State("JumpStart", Creative("Jump_Start"), false, back: false),   // chained: start → loop once → land (BuildController)
            new State("Jump", Creative("Jump_Loop"), false, back: false),
            new State("JumpEnd", Creative("Jump_End"), false),
            // work
            new State("Dig", Zomb("Button_Pushing"), true),
            new State("Shovel", Zomb("Action"), true),
            new State("Machine", Zomb("Button_Pushing"), true),
            new State("Lift", Zomb("Action"), true),
            new State("Chop", Zomb("Zombie_Attack"), true),
            new State("Swing", Zomb("Attack_Hand"), true),
            new State("Slash", Zomb("Attack_Hand"), false),
            new State("Punch", Zomb("Attack_Hand"), false),
            new State("Climb", Zomb("Climb_Ladder"), true),
            new State("Situps", Zomb("Situps"), true),
            new State("Eat", Zomb("Eating"), true),
            new State("Swim", Zomb("Swimming"), true),
            // falls and fights
            new State("Hit", Zomb("Hit_Reaction"), false),
            new State("Fall", Zomb("Fall"), false, back: false),
            new State("Defeat", Zomb("Death_Backward"), false, back: false),
            new State("GetUp", Zomb("Get_Up"), false),
            new State("Shake", Zomb("Convulsing"), false),
            new State("Flip", Zomb("Running_Forward_Flip"), false),
            new State("RunFlip", Zomb("Run_to_Flip"), false),
            // seated on the horse: a crouch frame (frozen by CharacterMotion.Pose)
            new State("Sit", Zomb("Crouch_Walk _Forward"), true),
            new State("Crouch", Zomb("Crouch_Walk _Forward"), true),
            new State("CrouchBack", Zomb("Crouch_Walk _Backward"), true),
            new State("CrouchLeft", Zomb("Crouch_Walk _Left"), true),
            new State("CrouchRight", Zomb("Crouch_Walk _Right"), true),
            // talking and greeting
            new State("Wave", Santa("WaveHello"), false),
            new State("Greet", Santa("WaveHello"), false),
            new State("Talk", Santa("TalkGestureActive"), true),
            new State("Listen", Santa("TalkGestureListen"), true),
            new State("Cheer", Zomb("Chicken_Dance"), false),
            new State("Ready", Zomb("Breakdance_Ready"), false),
            new State("Rare", Zomb("Idle_Rare"), false),
        }.Concat(Dances()).ToArray();

        public static readonly State[] ZombieStates = new[]
        {
            new State("Idle", Zomb("Idle"), true),
            new State("LookAround", Zomb("Idle_Rare"), false),
            new State("Walk", Zomb("Crouch_Walk _Forward"), true),
            new State("WalkBack", Zomb("Crouch_Walk _Backward"), true),
            new State("WalkLeft", Zomb("Crouch_Walk _Left"), true),
            new State("WalkRight", Zomb("Crouch_Walk _Right"), true),
            new State("Run", Zomb("Run _Forward"), true),
            new State("RunBack", Zomb("Run _Backward"), true),
            new State("RunLeft", Zomb("Run_Left"), true),
            new State("RunRight", Zomb("Run_Right"), true),
            new State("Flee", Zomb("Run _Forward"), true),
            new State("Crawl", Zomb("Crawl_Forward"), true),
            new State("Slash", Zomb("Zombie_Attack"), false),
            new State("Punch", Zomb("Attack_Hand"), false),
            new State("Bite", Zomb("Attack_Bite"), false),
            new State("Eat", Zomb("Eating"), true),
            new State("Hit", Zomb("Hit_Reaction"), false),
            new State("Defeat", Zomb("Death_Backward"), false, back: false),
            new State("GetUp", Zomb("Get_Up"), false),
            new State("Fall", Zomb("Fall"), false, back: false),
            new State("Cheer", Zomb("Convulsing"), false),
            new State("Shake", Zomb("Convulsing"), false),
            new State("Machine", Zomb("Button_Pushing"), true),
            new State("Lift", Zomb("Action"), true),
            new State("Climb", Zomb("Climb_Ladder"), true),
            new State("Swim", Zomb("Swimming"), true),
            new State("Situps", Zomb("Situps"), true),
            new State("Flip", Zomb("Running_Forward_Flip"), false),
            new State("RunFlip", Zomb("Run_to_Flip"), false),
            new State("Skin", Zomb("Skinn_Test"), false),
        }.Concat(Dances()).ToArray();

        /// <summary>A controller whose every state plays its own copy of a pack clip (the copy carries the looping).</summary>
        static AnimatorController BuildController(string path, State[] states)
        {
            // keep the asset (and everything pointing at it): empty its state machine and fill it again
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (ctrl == null) ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            var sm = ctrl.layers[0].stateMachine;
            foreach (var old in sm.states.Select(x => x.state).ToList()) sm.RemoveState(old);
            foreach (var t in sm.anyStateTransitions.ToList()) sm.RemoveAnyStateTransition(t);
            var prefix = Path.GetFileNameWithoutExtension(path);
            var made = new Dictionary<string, AnimatorState>();
            foreach (var s in states)
            {
                var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(s.clip);
                if (src == null) { Debug.LogWarning($"[ITHappy] {prefix}: no clip at {s.clip} for {s.name}"); continue; }
                var clipPath = $"{Clips}/{prefix}_{s.name}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                if (clip == null) { clip = Object.Instantiate(src); clip.name = prefix + "_" + s.name; AssetDatabase.CreateAsset(clip, clipPath); }
                else { EditorUtility.CopySerialized(src, clip); clip.name = prefix + "_" + s.name; }
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                settings.loopTime = s.loop;
                settings.loopBlend = s.loop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
                var st = sm.AddState(s.name);
                st.motion = clip;
                made[s.name] = st;
            }
            if (made.TryGetValue("Idle", out var idle))
            {
                sm.defaultState = idle;
                foreach (var s in states)
                    if (!s.loop && s.back && made.TryGetValue(s.name, out var st) && st != idle)
                    {
                        var tr = st.AddTransition(idle);
                        tr.hasExitTime = true; tr.exitTime = 0.95f; tr.hasFixedDuration = true; tr.duration = 0.2f;
                    }
            }
            // a jump plays through: take-off, a moment in the air, landing
            if (made.TryGetValue("JumpStart", out var js) && made.TryGetValue("Jump", out var jl) && made.TryGetValue("JumpEnd", out var je))
            {
                var t1 = js.AddTransition(jl); t1.hasExitTime = true; t1.exitTime = 0.9f; t1.duration = 0.1f;
                var t2 = jl.AddTransition(je); t2.hasExitTime = true; t2.exitTime = 0.95f; t2.duration = 0.1f;
            }
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        // ------------------------------------------------------------------ parts (what people can wear)

        /// <summary>Slot renderer names on the Creative rig, their labels, and whether they may be empty.</summary>
        static readonly (string slot, string label, bool canBeEmpty)[] Slots =
        {
            ("Body", "Skin", false), ("Faces", "Face", false), ("Hairstyle", "Hair", true), ("Hat", "Hat", true), ("Glasses", "Glasses", true),
            ("Mustache", "Moustache", true), ("Accessories", "Accessory", true), ("Outerwear", "Top", false), ("T_Shirt", "Shirt", true),
            ("Pants", "Bottom", false), ("Shoes", "Shoes", true), ("Gloves", "Gloves", true), ("Full_body", "Costume", true),
        };

        /// <summary>Which slot a part prefab's folder (and name) goes in.</summary>
        static string? SlotOf(string folder, string name) => folder switch
        {
            "Body" => "Body",
            "Faces" => "Faces",
            "Hairstyle" or "Hairstyle Single" => "Hairstyle",
            "Hat" or "Hat Single" => "Hat",
            "Glasses" => "Glasses",
            "Face Accessories" => name.StartsWith("Mustache") ? "Mustache" : "Accessories",
            "Gloves" => "Gloves",
            "Outwear" or "Outfit" or "Mascots" => "Outerwear",
            "Pants" or "Shorts" => "Pants",
            "Shoes" => "Shoes",
            "Costumes" => name.EndsWith("_002") ? "Hat" : "Outerwear",
            _ => null,
        };

        /// <summary>Part prefab file name → mesh name, filled while the parts asset is built (looks are written with mesh names).</summary>
        static readonly Dictionary<string, string> MeshNames = new Dictionary<string, string>(StringComparer.Ordinal);

        static CharacterParts BuildParts()
        {
            var parts = AssetDatabase.LoadAssetAtPath<CharacterParts>(PartsPath);
            if (parts == null) { parts = ScriptableObject.CreateInstance<CharacterParts>(); AssetDatabase.CreateAsset(parts, PartsPath); }
            parts.slots.Clear();
            MeshNames.Clear();
            foreach (var (slot, label, canBeEmpty) in Slots)
                parts.slots.Add(new CharacterParts.Slot { name = slot, label = label, canBeEmpty = canBeEmpty });
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { CC + "Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var folder = Path.GetFileName(Path.GetDirectoryName(path)!);
                var name = Path.GetFileNameWithoutExtension(path);
                if (name == "Base_Mesh") continue;
                var slot = SlotOf(folder, name);
                if (slot == null) continue;
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var smr = pf != null ? pf.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                if (smr == null || smr.sharedMesh == null) { Debug.LogWarning($"[ITHappy] part {name}: no skinned mesh"); continue; }
                parts.Find(slot)!.options.Add(smr.sharedMesh);
                MeshNames[name] = smr.sharedMesh.name;
            }
            parts.material = AssetDatabase.LoadAssetAtPath<Material>(CC + "Materials/Color.mat");
            EditorUtility.SetDirty(parts);
            Debug.Log($"[ITHappy] parts: {string.Join(", ", parts.slots.Select(s => s.name + " " + s.options.Count))}");
            return parts;
        }

        // ------------------------------------------------------------------ people

        public sealed class Role
        {
            public string id = "", role = "", look = "", label = ""; public float height = 2f; public bool ownLook;
            /// <summary>The Customize tab: the role itself, or townsfolk for the folk_* roles.</summary>
            public string Tab => role.StartsWith("folk_") ? "townsfolk" : role;
        }
        static Role R(string id, string role, float height, string look, bool ownLook = false, string label = "") => new Role { id = id, role = role, height = height, look = look, ownLook = ownLook, label = label };

        /// <summary>Looks are "Slot=PartFile;…" by part file name; an empty value = nothing in that slot. Every slot is
        /// named so the rig's own default clothes never show through.</summary>
        const string Bare = "Body=Body_010;Faces=Male_emotion_usual_001;Hairstyle=;Hat=;Glasses=;Mustache=;Accessories=;Outerwear=Outwear_004;T_Shirt=;Pants=Pants_009;Shoes=Shoe_Sneakers_009;Gloves=;Full_body=";

        public static readonly Role[] Roles =
        {
            R("char_farmer", "player", 2.1f, Bare + ";Faces=Male_emotion_happy_002;Hat=Hat_010;Hairstyle=Hairstyle_Male_001;Mustache=Mustache_003", label: "Farmer"),
            R("char_warrior", "army", 2.05f, Bare + ";Faces=Male_emotion_angry_003;Hat=Hat_Single_013;Outerwear=Outwear_050;Pants=Pants_010;Gloves=Gloves_014", label: "Soldier"),
            R("char_farm_worker", "labourer", 1.95f, Bare + ";Hairstyle=Hairstyle_Male_005;Outerwear=Outwear_043;Pants=Pants_010;Gloves=Gloves_006", label: "Labourer"),
            R("char_builder", "builder", 1.95f, Bare + ";Hat=Hat_Single_008;Outerwear=Outwear_004;Pants=Pants_010;Gloves=Gloves_006", label: "Builder"),
            // each townsperson is dressed on their own (folk_* roles), so the town keeps its variety
            R("char_baker", "folk_baker", 1.9f, Bare + ";Faces=Male_emotion_happy_002;Glasses=Glasses_004;Outerwear=Outwear_043;Pants=Shorts_003;Shoes=Shoe_Slippers_005;Hairstyle=Hairstyle_Male_005", label: "Baker"),
            R("villager_a", "folk_a", 1.9f, Bare + ";Outerwear=Outfit_010;Hairstyle=Hairstyle_Male_Single_006;Glasses=Glasses_006", label: "Grocer"),
            R("villager_b", "folk_b", 1.9f, Bare + ";Outerwear=Mascot_002;Shoes=Shoe_Slippers_002", label: "Mascot"),
            R("villager_c", "folk_c", 1.9f, Bare + ";Hat=Hat_Single_016;Outerwear=Outwear_050;Accessories=Clown_nose_001;Pants=Shorts_003", label: "Jester"),
        };

        /// <summary>A look by part file names → the same look by mesh names (what CharacterLook applies).</summary>
        static string LookByMeshes(string look)
        {
            var d = CharacterLook.Parse(look);
            var keys = d.Keys.ToList();
            foreach (var k in keys)
            {
                var v = d[k];
                if (v.Length == 0) continue;
                if (MeshNames.TryGetValue(v, out var mesh)) d[k] = mesh;
                else Debug.LogWarning($"[ITHappy] look names unknown part {v}");
            }
            return CharacterLook.Write(d);
        }

        static bool BuildPerson(Role r, CharacterParts parts, AnimatorController ctrl)
        {
            var basePf = AssetDatabase.LoadAssetAtPath<GameObject>(CC + "Prefabs/Base_Mesh.prefab");
            if (basePf == null) { Debug.LogError("[ITHappy] no Creative Characters Base_Mesh prefab"); return false; }
            var root = new GameObject(r.id);
            try
            {
                var inst = IthappyArt.Inst(basePf, root.transform);
                // unpacked: a controller set on a nested model's Animator is lost on runtime Instantiate (clones came up without one)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(inst)) PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                var anim = inst.GetComponentInChildren<Animator>(true);
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var look = root.AddComponent<CharacterLook>();
                var lookStr = LookByMeshes(r.look);
                IthappyArt.Set(look, "role", r.role);
                IthappyArt.Set(look, "parts", parts);
                IthappyArt.Set(look, "defaultLook", lookStr);
                IthappyArt.Set(look, "ownLook", r.ownLook);
                look.Apply(lookStr);
                // stand them to height, feet on the origin
                var b = IthappyArt.WorldBounds(inst);
                inst.transform.localScale = Vector3.one * (r.height / Mathf.Max(b.size.y, 0.01f));
                b = IthappyArt.WorldBounds(inst);
                inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                root.AddComponent<CharacterMotion>();
                foreach (var sr in root.GetComponentsInChildren<Renderer>(true)) { sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; sr.receiveShadows = true; }
                PrefabUtility.SaveAsPrefabAsset(root, $"{People}/{r.id}.prefab");
                return true;
            }
            finally { Object.DestroyImmediate(root); }
        }

        public const string CatalogPath = "Assets/Farmverse/Content/Resources/CharacterCatalog.asset";

        /// <summary>The Customize screen's list of who can be dressed, in Resources.</summary>
        static void BuildCatalog(CharacterParts parts, RuntimeAnimatorController villager)
        {
            IthappyArt.Folder("Assets/Farmverse/Content/Resources");
            var cat = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogPath);
            if (cat == null) { cat = ScriptableObject.CreateInstance<CharacterCatalog>(); AssetDatabase.CreateAsset(cat, CatalogPath); }
            cat.parts = parts;
            cat.animator = villager;
            cat.people.Clear();
            foreach (var r in Roles)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{People}/{r.id}.prefab");
                if (prefab == null) continue;
                cat.people.Add(new CharacterCatalog.Person { role = r.role, label = r.label, tab = r.Tab, prefab = prefab, defaultLook = LookByMeshes(r.look) });
            }
            Costumes(cat);
            EditorUtility.SetDirty(cat);
        }

        /// <summary>The costumes sold for gems: Santa from the Christmas pack, and every Zombies Mini outfit on a human body.</summary>
        public static readonly (string id, string label, string pack, string prefab)[] CostumeList =
        {
            ("santa", "Santa", "Christmas", "char_santa"),
            ("builder", "Builder", "Zombies", "char_cured_builder"), ("clerk", "Clerk", "Zombies", "char_cured_clerk"),
            ("footballer", "Footballer", "Zombies", "char_cured_footballer"), ("policeman", "Policeman", "Zombies", "char_cured_policeman"),
            ("mime", "Mime", "Zombies", "char_cured_mime"), ("tourist", "Tourist", "Zombies", "char_cured_tourist"),
            ("wanderer", "Wanderer", "Zombies", "char_cured_1"), ("drifter", "Drifter", "Zombies", "char_cured_2"),
            ("rambler", "Rambler", "Zombies", "char_cured_3"), ("roamer", "Roamer", "Zombies", "char_cured_4"),
        };

        static void Costumes(CharacterCatalog cat)
        {
            cat.costumes.Clear();
            foreach (var (id, label, pack, prefab) in CostumeList)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{People}/{prefab}.prefab");
                if (pf != null) cat.costumes.Add(new CharacterCatalog.Costume { id = id, label = label, pack = pack, prefab = pf });
                else Debug.LogWarning("[ITHappy] no costume prefab " + prefab);
            }
        }

        /// <summary>char_santa: the Christmas pack's Santa as a person (humanoid, the townsfolk's animator, 2 m like everyone).</summary>
        static bool BuildSanta(AnimatorController villagerCtrl)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ithappy/Christmas_Pack_FREE/Prefabs/Santa.prefab");
            if (pf == null) { Debug.LogWarning("[ITHappy] no Santa prefab"); return false; }
            var root = new GameObject("char_santa");
            try
            {
                var inst = IthappyArt.Inst(pf, root.transform);
                if (PrefabUtility.IsAnyPrefabInstanceRoot(inst)) PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                var anim = inst.GetComponentInChildren<Animator>(true);
                if (anim == null)
                {
                    anim = inst.AddComponent<Animator>();
                    anim.avatar = AssetDatabase.LoadAllAssetsAtPath("Assets/ithappy/Christmas_Pack_FREE/Meshes/Santa.fbx").OfType<Avatar>().FirstOrDefault();
                }
                anim.runtimeAnimatorController = villagerCtrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                var b = IthappyArt.WorldBounds(inst);
                inst.transform.localScale *= 2.0f / Mathf.Max(b.size.y, 0.01f);
                b = IthappyArt.WorldBounds(inst);
                inst.transform.localPosition -= new Vector3(b.center.x - root.transform.position.x, b.min.y, b.center.z - root.transform.position.z);
                root.AddComponent<CharacterMotion>();
                PrefabUtility.SaveAsPrefabAsset(root, $"{People}/char_santa.prefab");
                return true;
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------------ raiders

        public static readonly (string id, string pack)[] Zombies =
        {
            ("char_zombie_1", "Zombie01"), ("char_zombie_2", "Zombie02"), ("char_zombie_3", "Zombie03"), ("char_zombie_4", "Zombie04"),
            ("char_zombie_footballer", "Footballer"), ("char_zombie_builder", "Builder"), ("char_zombie_clerk", "Clerk"),
            ("char_zombie_policeman", "Policeman"), ("char_zombie_mime", "Mime"), ("char_zombie_tourist", "Tourist"),
        };

        static bool BuildZombie(string id, string pack, AnimatorController ctrl)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>(ZM + "Prefabs/Characters/" + pack + ".prefab");
            if (pf == null) { Debug.LogWarning($"[ITHappy] no zombie prefab {pack}"); return false; }
            var root = new GameObject(id);
            try
            {
                var inst = IthappyArt.Inst(pf, root.transform);
                // unpacked: a controller set on a nested model's Animator is lost on runtime Instantiate (clones came up without one)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(inst)) PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                var anim = inst.GetComponentInChildren<Animator>(true);
                if (anim == null)
                {
                    var fbxRoot = inst.transform.childCount > 0 ? inst.transform.GetChild(0).gameObject : inst;
                    anim = fbxRoot.AddComponent<Animator>();
                    anim.avatar = AssetDatabase.LoadAllAssetsAtPath(ZM + "Meshes/Base_Mesh.fbx").OfType<Avatar>().FirstOrDefault();
                }
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var b = IthappyArt.WorldBounds(inst);
                inst.transform.localScale = Vector3.one * (2.0f / Mathf.Max(b.size.y, 0.01f));
                b = IthappyArt.WorldBounds(inst);
                inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                root.AddComponent<CharacterMotion>();
                PrefabUtility.SaveAsPrefabAsset(root, $"{People}/{id}.prefab");
                return true;
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>
        /// char_cured_&lt;boss&gt;: the boss zombie as the townsperson it becomes when cured. Its skin parts (Body_*,
        /// Head_*) get their palette coordinates moved from the zombie greens to a warm skin tone on the same atlas
        /// (the pack's palette is a strip of hues: shifting u changes the hue and keeps the shading); the clothes stay.
        /// </summary>
        [MenuItem("Farmverse/Art/Build Costumes (cured townsfolk, Santa)", priority = 4)]
        public static void BuildCuredAll()
        {
            var villager = AssetDatabase.LoadAssetAtPath<AnimatorController>(VillagerController);
            if (villager == null) { Debug.LogWarning("[ITHappy] build the characters first"); return; }
            foreach (var z in Zombies) BuildCured(z.id, villager);
            BuildSanta(villager);
            var cat = AssetDatabase.LoadAssetAtPath<CharacterCatalog>(CatalogPath);
            if (cat != null) { Costumes(cat); EditorUtility.SetDirty(cat); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ITHappy] cured townsfolk and Santa built; {cat?.costumes.Count ?? 0} costumes in the catalogue");
        }

        /// <summary>
        /// The Zombies pack's palette with a skin-tone swatch painted into its plain right-hand strip (u 0.95–1,
        /// upper half: pale at the top, warmer lower down, so the shading the UVs carry still reads), and a copy of
        /// its material using it.
        /// </summary>
        /// <summary>Paint the swatch again even if the palette copy exists (after a colour change).</summary>
        static bool Repaint = true;

        static Material? CuredPalette(string dir)
        {
            var matPath = dir + "/Color_Cured.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null && File.Exists(dir + "/Palette_Cured.png") && !Repaint) return mat;
            var src = AssetDatabase.LoadAssetAtPath<Material>(ZM + "Materials/Color.mat");
            var srcTexPath = ZM + "Textures/Textures_7.png";
            if (src == null || !File.Exists(srcTexPath)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            tex.LoadImage(File.ReadAllBytes(srcTexPath));
            int w = tex.width, h = tex.height;
            var top = new Color32(250, 212, 184, 255);
            var low = new Color32(222, 170, 138, 255);
            for (var y = h / 2; y < h; y++)
            {
                var c = Color32.Lerp(low, top, (y - h / 2f) / (h / 2f));
                for (var x = (int)(w * 0.955f); x < w; x++) tex.SetPixel(x, y, c);
            }
            tex.Apply();
            var texPath = dir + "/Palette_Cured.png";
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            var srcTi = (TextureImporter)AssetImporter.GetAtPath(srcTexPath);
            if (ti != null && srcTi != null) { ti.filterMode = srcTi.filterMode; ti.mipmapEnabled = srcTi.mipmapEnabled; ti.SaveAndReimport(); }
            var fresh = mat == null;
            if (fresh) mat = new Material(src) { name = "Color_Cured" };
            var newTex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (mat!.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", newTex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", newTex);
            if (fresh) AssetDatabase.CreateAsset(mat, matPath); else EditorUtility.SetDirty(mat);
            return mat;
        }

        static void BuildCured(string zombieId, AnimatorController villagerCtrl)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{People}/{zombieId}.prefab");
            if (pf == null) return;
            var id = zombieId.Replace("char_zombie_", "char_cured_");
            var dir = People + "/Cured";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(People, "Cured");
            var root = (GameObject)Object.Instantiate(pf);
            root.name = id;
            try
            {
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    var part = smr.sharedMesh.name;   // the pack's skin meshes: Body_A…, Head_C… (the objects are just "Body", "Head1")
                    var head = part.StartsWith("Head") || smr.gameObject.name == "Head1";
                    var skin = head || part.StartsWith("Body") || smr.gameObject.name == "Body";
                    var eyes = part.StartsWith("Eyes") || smr.gameObject.name is "Eye" or "Eyes";
                    if (!skin && !eyes) continue;
                    Debug.Log($"[ITHappy] cured skin: {zombieId} {smr.gameObject.name} ({part})");
                    var meshPath = $"{dir}/{id}_{part}.asset";
                    var mesh = Object.Instantiate(smr.sharedMesh);
                    var uv = new List<Vector2>();
                    mesh.GetUVs(0, uv);
                    for (var i = 0; i < uv.Count; i++)
                    {
                        var u = uv[i].x;
                        if (eyes)
                        {
                            // red zombie irises → dark brown ones (the dark end of the strip)
                            if (u < 0.15f || (u > 0.62f && u < 0.8f)) uv[i] = new Vector2(0.86f, Mathf.Min(uv[i].y, 0.35f));
                            continue;
                        }
                        // the zombie greens and teals (about 0.36–0.62 across the strip), and a head's dead greys → skin
                        if ((u >= 0.36f && u <= 0.62f) || (head && u >= 0.82f && u <= 0.95f))
                            uv[i] = new Vector2(0.985f, 0.55f + Mathf.Clamp01(uv[i].y) * 0.43f);   // the skin swatch painted on the palette copy (CuredPalette)
                    }
                    mesh.SetUVs(0, uv);
                    // the asset is updated in place when it exists (deleting and re-creating it leaves the prefab's reference empty)
                    var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (old != null && old.vertexCount == mesh.vertexCount)
                    {
                        old.SetUVs(0, uv);
                        EditorUtility.SetDirty(old);
                        Object.DestroyImmediate(mesh);
                        mesh = old;
                    }
                    else
                    {
                        if (old != null) AssetDatabase.DeleteAsset(meshPath);
                        mesh.name = id + "_" + part;
                        AssetDatabase.CreateAsset(mesh, meshPath);
                    }
                    var check = new List<Vector2>(); mesh.GetUVs(0, check);
                    Debug.Log($"[ITHappy] {mesh.name}: {check.Count(p => p.x > 0.98f)} of {check.Count} uvs on the skin swatch");
                    smr.sharedMesh = mesh;
                }
                // the pack's single atlas material → its copy with the skin swatch
                var cured = CuredPalette(dir);
                if (cured != null)
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        for (var m = 0; m < mats.Length; m++) if (mats[m] != null && mats[m].name == "Color") mats[m] = cured;
                        r.sharedMaterials = mats;
                    }
                var anim = root.GetComponentInChildren<Animator>(true);
                if (anim != null) anim.runtimeAnimatorController = villagerCtrl;
                PrefabUtility.SaveAsPrefabAsset(root, $"{People}/{id}.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        // ------------------------------------------------------------------ animals

        public sealed class Animal { public string id = "", pack = "", folder = ""; public float height = 1f; public float yaw; }
        static Animal A(string id, string pack, string folder, float height, float yaw = 0f) => new Animal { id = id, pack = pack, folder = folder, height = height, yaw = yaw };

        public static readonly Animal[] AnimalList =
        {
            A("chicken", "Chicken_01", "Chicken", 0.6f), A("chicken_2", "Chicken_02", "Chicken", 0.6f), A("chicken_3", "Chicken_03", "Chicken", 0.6f), A("chick", "Chick_01", "Chick", 0.3f),
            A("cow", "Cow_01", "Cow", 1.8f), A("cow_2", "Cow_02", "Cow", 1.8f),
            A("sheep", "Sheep_01", "Sheep", 1.15f), A("sheep_2", "Sheep_02", "Sheep", 1.15f), A("sheep_3", "Sheep_03", "Sheep", 1.15f),
            A("pig", "Pig_01", "Pig", 0.95f), A("pig_2", "Pig_02", "Pig", 0.95f), A("pig_3", "Pig_03", "Pig", 0.95f),
            A("goat", "Goat_01", "Goat", 1.15f), A("goat_2", "Goat_02", "Goat", 1.15f),
            A("horse", "Horse_02", "Horse", 2.1f), A("horse_2", "Horse_01", "Horse", 2.1f), A("horse_3", "Horse_03", "Horse", 2.1f), A("horse_4", "Horse_04", "Horse", 2.1f), A("horse_5", "Horse_05", "Horse", 2.1f),
            A("donkey", "Donkey_01", "Donkey", 1.6f), A("donkey_2", "Donkey_02", "Donkey", 1.6f),
            A("duck", "Duck_01", "Duck", 0.5f),
            A("dog", "Dog_01", "Dog", 0.8f), A("dog_2", "Dog_02", "Dog", 0.8f), A("dog_3", "Dog_03", "Dog", 0.8f), A("dog_4", "Dog_04", "Dog", 0.8f), A("dog_5", "Dog_05", "Dog", 0.8f), A("dog_6", "Dog_06", "Dog", 0.8f),
            A("cat", "Cat_01", "Cat", 0.5f), A("cat_2", "Cat_02", "Cat", 0.5f), A("cat_3", "Cat_03", "Cat", 0.5f), A("cat_4", "Cat_04", "Cat", 0.5f),
            A("kitty", "Kitty_01", "Kitty", 0.32f), A("kitty_2", "Kitty_02", "Kitty", 0.32f), A("kitty_3", "Kitty_03", "Kitty", 0.32f), A("kitty_4", "Kitty_04", "Kitty", 0.32f),
            A("pigeon", "Pigeon_01", "Pigeon", 0.28f), A("pigeon_2", "Pigeon_02", "Pigeon", 0.28f), A("pigeon_3", "Pigeon_03", "Pigeon", 0.28f),
        };

        static readonly Dictionary<string, AnimatorController> AnimalControllers = new Dictionary<string, AnimatorController>();

        /// <summary>Idle / Walk / Run from the kind's own clips, Eat where it has one (else its gesture), and the rest by name.</summary>
        static AnimatorController AnimalController(string folder)
        {
            if (AnimalControllers.TryGetValue(folder, out var cached) && cached != null) return cached;
            var dir = FarmAnim + folder;
            var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { dir })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".anim"))
                .ToDictionary(p => Path.GetFileNameWithoutExtension(p).Substring(folder.Length + 1), p => p);
            var states = new List<State>();
            string? Clip(params string[] names) { foreach (var n in names) if (clips.TryGetValue(n, out var p)) return p; return null; }
            void Add(string state, bool loop, params string[] names) { var c = Clip(names); if (c != null) states.Add(new State(state, c, loop)); }
            Add("Idle", true, "Idle");
            Add("Walk", true, "Walk");
            Add("Run", true, "Run", "Walk");
            Add("Eat", true, "Eat", "Gesture");
            Add("LookAround", false, "Gesture", "Eat", "Stretch");
            Add("Sit", true, "Lying", "Sit", "Idle");
            Add("Swim", true, "Swim");
            Add("Fly", true, "Fly");
            Add("Jump", false, "Jump");
            // every clip of the kind under its own name too (Gesture, Stretch, Lying, Swim_Idle…), so nothing in the pack goes unused
            foreach (var kv in clips)
                if (!states.Any(s => s.name == kv.Key)) states.Add(new State(kv.Key, kv.Value, kv.Key is "Lying" or "Sit" or "Swim_Idle" or "Eat" or "Fly" or "Swim"));
            var ctrl = BuildController($"{Animals}/{folder}.controller", states.ToArray());
            AnimalControllers[folder] = ctrl;
            return ctrl;
        }

        static bool BuildAnimal(Animal a)
        {
            var pf = IthappyArt.Pack(a.pack);
            if (pf == null) { Debug.LogWarning($"[ITHappy] no animal prefab {a.pack}"); return false; }
            var ctrl = AnimalController(a.folder);
            var root = new GameObject(a.id);
            try
            {
                var inst = IthappyArt.Inst(pf, root.transform);
                // unpacked: a controller set on a nested model's Animator is lost on runtime Instantiate (clones came up without one)
                if (PrefabUtility.IsAnyPrefabInstanceRoot(inst)) PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                inst.transform.localRotation = Quaternion.Euler(0, a.yaw, 0);
                var anim = inst.GetComponentInChildren<Animator>(true);
                if (anim == null) anim = inst.AddComponent<Animator>();
                anim.runtimeAnimatorController = ctrl;
                anim.applyRootMotion = false;
                anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var b = IthappyArt.WorldBounds(inst);
                inst.transform.localScale = Vector3.one * (a.height / Mathf.Max(b.size.y, 0.01f));
                b = IthappyArt.WorldBounds(inst);
                inst.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                root.AddComponent<CharacterMotion>();
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }
                PrefabUtility.SaveAsPrefabAsset(root, $"{Animals}/{a.id}.prefab");
                return true;
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
