using System.Collections.Generic;
using System.IO;
using System.Linq;
using Farmverse.Presentation.World;
using Farmverse.Presentation.World.Island;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Farmverse.Editor
{
    /// <summary>
    /// Farmverse / World / Build Island: generates the open island the player builds on.
    ///  - Ground: a 640 m square of meshes (8×8 chunks, 2 m between vertices) shaped as one big island
    ///    with a wide flat meadow for building, beaches, a few rocky hills, and sea floor around it.
    ///    Vertex colours carry the layer weights for Farmverse/IslandGround (grass, flowers, sand, rock).
    ///  - Water: a dense wave grid over the island area and a coarse ring out to the horizon,
    ///    using Farmverse/Water with a depth mask baked from the ground height.
    ///  - Building grid (IslandData): 2 m cells, buildable on dry, flat land.
    ///  - Building catalogue: every prefab in Content/Tripo/Prefabs (not the interior rooms), with a
    ///    footprint from its collider, a price and a category; farmhouse, barn, chicken coop and
    ///    market stall are the starter pack.
    ///  - Scene Scenes/Island.unity with light, fog, camera, placement and the sandbox panel.
    /// Deterministic (fixed seed): running it again rebuilds the same island.
    /// </summary>
    public static class IslandBuilder
    {
        const string Root = "Assets/Farmverse/Content/Island";
        const string Gen = Root + "/Generated";
        internal const float Size = 640f;          // metres, square, centred on 0
        const int Chunks = 8;
        const int Quads = 40;             // per chunk side → 2 m spacing
        internal const float Sea = 0f;
        const int Chapters = IslandShape.Chapters;
        const float Cell = 2f;

        [MenuItem("Farmverse/World/Build Island")]
        public static void Build()
        {
            Directory.CreateDirectory(Gen);
            AssetDatabase.Refresh();

            IslandPaths.Prepare();   // routes first: the ground is levelled under them
            var groundMat = GroundMaterial();
            var waterMat = WaterMaterial();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- ground: each chunk baked once per chapter (shared where a chapter changes nothing there) ----
            var island = new GameObject("Island");
            float chunk = Size / Chunks;
            int distinct = 0;
            for (int cz = 0; cz < Chunks; cz++)
                for (int cx = 0; cx < Chunks; cx++)
                {
                    var meshes = new Mesh[Chapters];
                    for (int c = 1; c <= Chapters; c++)
                    {
                        var m = ChunkMesh(c, -Size / 2 + cx * chunk, -Size / 2 + cz * chunk, chunk);
                        if (c > 1 && SameGround(m, meshes[c - 2])) { meshes[c - 1] = meshes[c - 2]; Object.DestroyImmediate(m); continue; }
                        AssetDatabase.CreateAsset(m, $"{Gen}/ground_{cx}_{cz}_c{c}.asset");
                        meshes[c - 1] = m;
                        distinct++;
                    }
                    var mesh = meshes[0];
                    var go = new GameObject($"Ground {cx},{cz}");
                    go.transform.SetParent(island.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<IslandGroundChunk>().chapters = meshes;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = groundMat;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                    go.AddComponent<IslandGround>();
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                }

            // ---- roads and railway ----
            IslandPaths.CreateObjects(Gen, Root);

            // ---- water ----
            var masks = new Texture2D[Chapters];
            for (int c = 1; c <= Chapters; c++) masks[c - 1] = WaterMask(c);
            var mask = masks[0];
            waterMat.SetTexture("_Mask", mask);
            waterMat.SetVector("_MaskRect", new Vector4(-Size / 2, -Size / 2, Size, Size));
            var water = new GameObject("Water");
            var inner = WaterGrid(-Size / 2, Size, 160);
            AssetDatabase.CreateAsset(inner, $"{Gen}/water_inner.asset");
            var outer = WaterRing(1800f, 80f, Size / 2);
            AssetDatabase.CreateAsset(outer, $"{Gen}/water_outer.asset");
            foreach (var (m, n) in new[] { (inner, "Water near"), (outer, "Water far") })
            {
                var go = new GameObject(n);
                go.transform.SetParent(water.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = waterMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
            }

            // ---- mist: drifting fog banks over the sea, a thin low layer creeping onto the shore ----
            var mistRoot = new GameObject("Mist");
            var mistMesh = MistQuad(1800f);
            AssetDatabase.CreateAsset(mistMesh, $"{Gen}/mist_quad.asset");
            var layers = new (float y, float density, float cover, float scale, float overLand, Vector2 wind)[]
            {
                (3.2f, 0.38f, 0.50f, 210f, 0.35f, new Vector2(1.6f, 0.7f)),
                (7.5f, 0.42f, 0.52f, 300f, 0.10f, new Vector2(2.4f, 1.0f)),
                (14f,  0.36f, 0.56f, 420f, 0.00f, new Vector2(3.2f, 1.3f)),
            };
            for (int li = 0; li < layers.Length; li++)
            {
                var L = layers[li];
                var mm = MistMaterial(li, mask, L.density, L.cover, L.scale, L.overLand, L.wind);
                var go = new GameObject($"Mist {li + 1}");
                go.transform.SetParent(mistRoot.transform, false);
                go.transform.localPosition = new Vector3(0, L.y, 0);
                go.AddComponent<MeshFilter>().sharedMesh = mistMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mm;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            // ---- grid data ----
            var data = GridData();

            // ---- station and train (scenery; skipped until their models are in) ----
            StationAndTrain(data);

            // ---- catalogue ----
            var catalogue = Catalogue(data.cellSize);

            // ---- nature: woods, bushes and rocks that the player clears to make room ----
            var nature = Nature(data);

            // ---- landmarks by chapter: jetty, piers and boats, lily pads, the falls, the lighthouse, the town square ----
            var landmarkWater = IslandLandmarks.Build(Gen, Root, waterMat);
            Backdrop();

            // ---- light, sky, fog: the packs' own renders (ArtSource/sample): a low warm sun, long soft shadows, a pink haze ----
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.88f, 0.74f);
            sun.intensity = 1.55f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.82f;
            sun.shadowBias = 0.03f;
            sun.shadowNormalBias = 0.5f;
            sunGo.transform.rotation = Quaternion.Euler(34f, -42f, 0f);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.70f, 0.68f, 0.92f);
            RenderSettings.ambientEquatorColor = new Color(0.92f, 0.76f, 0.74f);
            RenderSettings.ambientGroundColor = new Color(0.52f, 0.44f, 0.38f);
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>("Assets/ithappy/Farm/Skyboxes/Skybox_2.mat");
            RenderSettings.fog = true;
            // a warm haze: clear up close, thickening with distance so the far sea and the mountains melt into the sky
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.93f, 0.80f, 0.80f);
            RenderSettings.fogDensity = 0.0011f;

            // ---- camera + systems ----
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 2500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.allowMSAA = true;
            var camData = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
            var icam = camGo.AddComponent<IslandCamera>();
            PostProcessing(camGo);

            var sys = new GameObject("Island Systems");
            var grid = sys.AddComponent<IslandGrid>();
            Set(grid, "data", data);
            // the island's growth by chapter: ground, water, scenery and the camera's reach
            var chapters = sys.AddComponent<IslandChapters>();
            Set(chapters, "data", data);
            Set(chapters, "islandCamera", icam);
            Set(chapters, "groundRoot", island);
            SetArray(chapters, "waterMasks", masks);
            SetArray(chapters, "waterRenderers", water.GetComponentsInChildren<MeshRenderer>().Concat(landmarkWater).ToArray());
            SetArray(chapters, "mistRenderers", mistRoot.GetComponentsInChildren<MeshRenderer>());
            Set(chapters, "nature", nature);
            // the camera may roam a little past each chapter's farthest coast
            var camR = new float[Chapters];
            for (int c = 1; c <= Chapters; c++)
            {
                float far = 0f;
                for (int k = 0; k < 72; k++) { float an = k * 5f * Mathf.Deg2Rad; far = Mathf.Max(far, IslandShape.Edge(c, Mathf.Cos(an) * 50f, Mathf.Sin(an) * 50f)); }
                camR[c - 1] = far + 20f;
            }
            { var so = new SerializedObject(chapters); var pr = so.FindProperty("cameraRadius"); pr.arraySize = camR.Length; for (int i = 0; i < camR.Length; i++) pr.GetArrayElementAtIndex(i).floatValue = camR[i]; so.ApplyModifiedPropertiesWithoutUndo(); }
            chapters.SetTour(Enumerable.Range(2, Chapters - 1).SelectMany(c => IslandShape.Tour(c).Select(t => (c, t.name, t.at, t.distance))));
            EditorUtility.SetDirty(chapters);
            // the Winter Festival: decorations, Santa, the Christmas pack's sky (December only)
            var festival = sys.AddComponent<IslandFestival>();
            if (IslandLandmarks.FestivalDecor != null) Set(festival, "decor", IslandLandmarks.FestivalDecor);
            if (IslandLandmarks.FestivalSanta != null) Set(festival, "santa", IslandLandmarks.FestivalSanta);
            var xmasSky = AssetDatabase.LoadAssetAtPath<Material>("Assets/ithappy/Christmas_Pack_FREE/Skyboxes/Skybox_8.mat");
            if (xmasSky != null) Set(festival, "festivalSky", xmasSky);
            var placer = sys.AddComponent<BuildPlacer>();
            Set(placer, "grid", grid);
            Set(placer, "islandCamera", icam);
            var sandbox = sys.AddComponent<IslandSandbox>();
            Set(sandbox, "catalogue", catalogue);
            Set(sandbox, "grid", grid);
            Set(sandbox, "placer", placer);
            Set(sandbox, "islandCamera", icam);
            Set(sandbox, "nature", nature);
            var economy = sys.AddComponent<IslandEconomyHost>();
            var visuals = AssetDatabase.LoadAssetAtPath<WorldVisualCatalog>("Assets/Farmverse/Content/World/WorldVisualCatalog.asset");
            if (visuals != null) Set(economy, "worldVisuals", visuals);
            Set(sandbox, "economy", economy);
            GameObject Person(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(IthappyCharacters.People + "/" + name + ".prefab");
            GameObject Animal(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(IthappyCharacters.Animals + "/" + name + ".prefab");
            var farmer = Person("char_farmer");
            if (farmer != null) Set(sandbox, "playerPrefab", farmer);
            else Debug.LogWarning("[IslandBuilder] no char_farmer prefab yet (Farmverse / Art / Build ITHappy Characters): no player on the island");
            // the island's people: farm workers, builders, warriors, raiders, and the farmer's horse
            var life = sys.AddComponent<IslandLife>();
            // townsfolk: the baker and the villagers
            var folk = new[] { "char_baker", "villager_a", "villager_b", "villager_c" }.Select(Person).Where(p => p != null).ToList();
            SetArray(life, "villagerPrefabs", folk.ToArray()!);
            // the farmhouse's rooms, for going inside
            SetArray(sandbox, "roomPrefabs", new[] { "living", "kitchen", "bedroom", "bathroom", "study" }
                .Select(r => AssetDatabase.LoadAssetAtPath<GameObject>($"{IthappyRooms.Rooms}/room_{r}.prefab")).Where(g => g != null).ToArray()!);
            if (Person("char_farm_worker") is { } worker) Set(life, "workerPrefab", worker);
            if (Person("char_warrior") is { } warrior) Set(life, "warriorPrefab", warrior);
            // builders: a made builder model if there is one, otherwise the farm worker in a hard hat (IslandLife adds it)
            SetArray(life, "builderPrefabs", new[] { "char_builder", "char_farm_worker" }.Select(Person).Where(p => p != null).Take(1).ToArray()!);
            // raiders: the zombies (the zombie nights)
            SetArray(life, "raiderPrefabs", new[] { "char_zombie_1", "char_zombie_2", "char_zombie_3", "char_zombie_4" }.Select(Person).Where(p => p != null).ToArray()!);
            SetArray(life, "bossPrefabs", new[] { "char_zombie_footballer", "char_zombie_builder", "char_zombie_clerk", "char_zombie_policeman", "char_zombie_mime", "char_zombie_tourist" }.Select(Person).Where(p => p != null).ToArray()!);
            SetArray(life, "curedPrefabs", new[] { "char_cured_footballer", "char_cured_builder", "char_cured_clerk", "char_cured_policeman", "char_cured_mime", "char_cured_tourist" }.Select(Person).Where(p => p != null).ToArray()!);
            SetArray(life, "weaponPrefabs", new[] { "Bat", "Crowbar", "Pipe", "PipeWrench", "Baton", "RoadSign", "Weapon_005" }
                .Select(w => AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Weapons + "/" + w + ".prefab")).Where(p => p != null).ToArray()!);
            if (Animal("horse") is { } horse) Set(sandbox, "horsePrefab", horse);
            // the animals round their houses: chickens and cows from the world's livestock, sheep and pig made here
            var animals = sys.AddComponent<IslandAnimals>();
            if (Animal("chicken") is { } chicken) Set(animals, "chickenPrefab", chicken);
            if (Animal("cow") is { } cow) Set(animals, "cowPrefab", cow);
            if (Animal("sheep") is { } sheep) Set(animals, "sheepPrefab", sheep);
            if (Animal("pig") is { } pig) Set(animals, "pigPrefab", pig);
            if (Animal("horse") is { } stableHorse) Set(animals, "horsePrefab", stableHorse);
            // every animal in the pack, and the ponds the ducks swim on
            SetArray(animals, "animalPrefabs", AssetDatabase.FindAssets("t:Prefab", new[] { IthappyCharacters.Animals })
                .Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p != null).ToArray()!);
            {
                var so = new SerializedObject(animals);
                var pp = so.FindProperty("ponds");
                var list = new[] { new Vector4(IslandShape.Pond.x, IslandShape.Sea + 0.02f, IslandShape.Pond.y, IslandShape.PondR * 0.75f), new Vector4(IslandShape.Lake.x, IslandShape.Sea + 0.02f, IslandShape.Lake.y, 12f) };
                pp.arraySize = list.Length;
                for (var i = 0; i < list.Length; i++) pp.GetArrayElementAtIndex(i).vector4Value = list[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            // the pack animals face where they walk already
            foreach (var f in new[] { "cowYaw", "chickenYaw" }) IthappyArt.Set(animals, f, 0f);
            foreach (var f in new[] { "chickenScale", "cowScale", "sheepScale", "pigScale" }) IthappyArt.Set(animals, f, 1f);
            // sound: every effect in Content/Audio, played by name, and only the music track that plays
            // (every clip wired here is in the web download: 12 unused tracks were ~30 MB of it)
            var audio = sys.AddComponent<IslandAudio>();
            var music = new SerializedObject(audio).FindProperty("musicClip").stringValue;
            var clips = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Farmverse/Content/Audio" })
                .Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(path => !path.StartsWith(AudioSetup.Music + "/") || System.IO.Path.GetFileNameWithoutExtension(path) == music)
                .Select(path => AssetDatabase.LoadAssetAtPath<AudioClip>(path)).Where(c => c != null).ToArray();
            SetArray(audio, "clips", clips);
            if (clips.Length == 0) Debug.LogWarning("[IslandBuilder] no sounds in Content/Audio: the island will be silent");

            // the game's screen (UI Toolkit, the kit's look) with pictures of the island's own models
            var pictures = IslandPictureMaker.Make(catalogue);
            var uiGo = new GameObject("Island UI");
            var doc = uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>("Assets/Farmverse/Content/UI/PanelSettings.asset");
            doc.sortingOrder = 10;
            var gameUi = uiGo.AddComponent<Farmverse.Presentation.UI.Game.GameUi>();   // the new UI, rebuilt to the design
            Set(gameUi, "sandbox", sandbox);

            // the events' dressing goes to its own scenes (loaded on top in October and December)
            EditorSceneManager.SaveScene(scene, "Assets/Farmverse/Scenes/Island.unity");   // saved first: a new scene can't open beside an untitled one
            if (IslandScenes.SplitEvents()) { Set(festival, "decor", null); Set(festival, "santa", null); }
            EditorSceneManager.SaveScene(scene, "Assets/Farmverse/Scenes/Island.unity");
            FarmverseSetup.AddSceneToBuild(FarmverseSetup.IslandScenePath);   // the game loads it beside the boot scene
            if (!File.Exists(IslandScenes.Farmhouse) || !File.Exists(IslandScenes.Studio)) { IslandScenes.BuildFarmhouse(); IslandScenes.BuildStudio(); }
            IslandScenes.BuildList();
            AssetDatabase.SaveAssets();
            Debug.Log($"[IslandBuilder] island built: {Chunks * Chunks} ground chunks, grid {data.width}x{data.depth}, " +
                      $"{data.buildable.Count(b => b != 0)} buildable cells, {catalogue.entries.Count} catalogue entries, {nature.transform.childCount} nature props");
        }

        // ------------------------------------------------------------------ railway scenery

        static void StationAndTrain(IslandData data)
        {
            var station = AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Props + "/depot.prefab");
            if (station != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(station);
                go.name = "Depot";
                go.transform.SetPositionAndRotation(IslandPaths.StationPos, Quaternion.Euler(0, IslandPaths.StationYaw + StationYawFix, 0));
                go.AddComponent<IslandChapterTag>().fromChapter = 1;
            }

            // the order truck: one box truck from the Farm pack, driving the causeway in and out
            var names = new[] { "truck_005" };
            var cars = new List<Transform>(); var lengths = new List<float>();
            var root = new GameObject("Truck");
            foreach (var n in names)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Vehicles + "/" + n + ".prefab");
                if (pf == null) continue;
                var car = new GameObject(n);
                car.transform.SetParent(root.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(pf, car.transform);
                foreach (var col in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                foreach (var r in model.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
                GameObjectUtility.SetStaticEditorFlags(model, 0);
                foreach (Transform t in model.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                // length along +Z, wheels on the car's origin
                var b = Bounds(model);
                if (b.size.x > b.size.z) { model.transform.localRotation = Quaternion.Euler(0, 90, 0); b = Bounds(model); }
                if (EngineFlip) model.transform.localRotation *= Quaternion.Euler(0, 180, 0);
                b = Bounds(model);
                float target = Mathf.Clamp(b.size.z, 5f, 9f);
                model.transform.localScale *= target / Mathf.Max(b.size.z, 0.01f);
                b = Bounds(model);
                model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
                cars.Add(car.transform); lengths.Add(target);
            }
            if (cars.Count == 0) { Object.DestroyImmediate(root); return; }
            var train = root.AddComponent<IslandTrain>();
            var so = new SerializedObject(train);
            so.FindProperty("data").objectReferenceValue = data;
            var pc = so.FindProperty("cars"); pc.arraySize = cars.Count;
            for (int i = 0; i < cars.Count; i++) pc.GetArrayElementAtIndex(i).objectReferenceValue = cars[i];
            var pl = so.FindProperty("carLengths"); pl.arraySize = lengths.Count;
            for (int i = 0; i < lengths.Count; i++) pl.GetArrayElementAtIndex(i).floatValue = lengths[i];
            so.FindProperty("stationIndex").floatValue = IslandPaths.StationIndex;
            so.FindProperty("railTop").floatValue = 0.08f;
            so.FindProperty("turnsAround").boolValue = true;
            so.FindProperty("cruiseSpeed").floatValue = 9f;
            so.ApplyModifiedPropertiesWithoutUndo();

            // visitors' cars and camper vans on the same causeway (from the valley chapter)
            var traffic = new GameObject("Traffic").AddComponent<IslandTraffic>();
            var carNames = new List<string> { "bus_005", "bus_006", "bus_003" };
            for (var i = 1; i <= 23; i++) carNames.Add($"car_{i:000}");
            var carPrefabs = carNames.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Vehicles + "/" + n + ".prefab")).Where(p => p != null).ToArray();
            Set(traffic, "data", data);
            SetArray(traffic, "carPrefabs", carPrefabs!);
        }

        const float StationYawFix = 180f;     // turn if the station model's front is not its +Z
        const bool EngineFlip = false;       // pack vehicles face +Z, the way they drive
        /// <summary>Highest point of the engine (the funnel), in the engine's own frame.</summary>
        static Vector3 Funnel(Transform car)
        {
            var top = new Vector3(0, 4f, 0); float best = float.MinValue;
            foreach (var mf in car.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = car.InverseTransformPoint(mf.transform.TransformPoint(v));
                    if (p.y > best) { best = p.y; top = p; }
                }
            }
            return top;
        }
        static readonly Dictionary<string, float> TrainLength = new Dictionary<string, float>
        { ["train_engine"] = 7f, ["train_carriage"] = 7.5f, ["train_wagon"] = 7f };

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            // relative to the car (parent) frame
            var parent = go.transform.parent;
            if (parent != null) b.center = parent.InverseTransformPoint(b.center);
            return b;
        }

        static ParticleSystem Smoke(GameObject go)
        {
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
            main.startColor = new Color(0.97f, 0.97f, 0.95f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.gravityModifier = -0.05f;
            var em = ps.emission; em.rateOverTime = 8f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.15f;
            sh.rotation = new Vector3(-90, 0, 0);
            var sz = ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 2.6f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.85f, 0.87f, 0.9f), 1) },
                      new[] { new GradientAlphaKey(0.9f, 0), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = SmokeMaterial();
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static Material SmokeMaterial()
        {
            var path = $"{Root}/Smoke.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", SoftDot());
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D SoftDot()
        {
            var path = $"{Root}/Textures/soft_dot.png";
            if (!File.Exists(path))
            {
                const int R = 64;
                var t = new Texture2D(R, R, TextureFormat.RGBA32, false);
                for (int y = 0; y < R; y++) for (int x = 0; x < R; x++)
                {
                    float dx = (x + 0.5f) / R * 2 - 1, dy = (y + 0.5f) / R * 2 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(dx * dx + dy * dy)); a = a * a * (3 - 2 * a);
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
                File.WriteAllBytes(path, t.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ nature

        const string NatureDir = IthappyArt.Nature + "/";
        const float StarterClear = 46f * IslandShape.K;   // metres kept free around the middle for the starter pack

        struct PropKind { public string id, name, prefab; public float scale, jitter; public int cost, cells; }

        // The ITHappy Farm pack's nature (Content/Art/Nature, built by Farmverse / Art / Build ITHappy Art), already
        // sized in metres: the scale here is only a nudge, the jitter the spread of sizes.
        static readonly PropKind Oak = new PropKind { id = "oak", name = "Oak tree", prefab = "tree_leafy_1", scale = 1.0f, jitter = 0.2f, cost = 40, cells = 3 };
        static readonly PropKind Round = new PropKind { id = "round", name = "Leafy tree", prefab = "tree_leafy_3", scale = 1.0f, jitter = 0.2f, cost = 40, cells = 3 };
        static readonly PropKind Pine = new PropKind { id = "pine", name = "Pine tree", prefab = "tree_fir_1", scale = 1.0f, jitter = 0.25f, cost = 40, cells = 3 };
        static readonly PropKind Pine2 = new PropKind { id = "pine", name = "Pine tree", prefab = "tree_fir_2", scale = 1.0f, jitter = 0.25f, cost = 40, cells = 3 };
        static readonly PropKind Bush = new PropKind { id = "bush", name = "Bush", prefab = "bush_1", scale = 1.0f, jitter = 0.3f, cost = 15, cells = 1 };
        static readonly PropKind Rock = new PropKind { id = "rock", name = "Boulder", prefab = "rock_2", scale = 1.0f, jitter = 0.4f, cost = 60, cells = 2 };
        static readonly PropKind Pond = new PropKind { id = "pond", name = "Pond", prefab = "pond", scale = 1.0f, jitter = 0.2f, cost = 200, cells = 3 };
        static readonly PropKind Flowers = new PropKind { id = "flowers", name = "Wild grass", prefab = "grass_1", scale = 1.0f, jitter = 0.3f, cost = 10, cells = 1 };
        static readonly PropKind Mushroom = new PropKind { id = "mushroom", name = "Mushrooms", prefab = "mushrooms", scale = 1.0f, jitter = 0.3f, cost = 10, cells = 1 };

        // Which tree grows where: the scatter decides where a tree stands and how many cells it blocks;
        // the look decides which pack tree it is, picked at random from its kind's models.
        const string TreeDir = IthappyArt.Nature + "/";
        struct TreeLook { public string id, name; public string[] prefabs; public float scale; }
        static readonly TreeLook TOak = new TreeLook { id = "oak", name = "Oak tree", prefabs = new[] { "tree_leafy_1", "tree_leafy_2", "tree_leafy_5", "tree_leafy_8" }, scale = 1.0f };
        static readonly TreeLook TMaple = new TreeLook { id = "maple", name = "Maple tree", prefabs = new[] { "tree_leafy_3", "tree_leafy_4", "tree_leafy_6" }, scale = 1.0f };
        static readonly TreeLook TApple = new TreeLook { id = "apple", name = "Apple tree", prefabs = new[] { "tree_fruit_1", "tree_fruit_2" }, scale = 1.0f };
        static readonly TreeLook TCherry = new TreeLook { id = "cherry", name = "Cherry tree", prefabs = new[] { "tree_fruit_3", "tree_fruit_1" }, scale = 1.0f };
        static readonly TreeLook TBirch = new TreeLook { id = "birch", name = "Birch tree", prefabs = new[] { "tree_leafy_7", "tree_leafy_2" }, scale = 1.0f };
        static readonly TreeLook TPine = new TreeLook { id = "pine", name = "Pine tree", prefabs = new[] { "tree_fir_1", "tree_fir_2", "tree_fir_3", "tree_fir_5", "tree_fir_teal_1", "tree_fir_teal_2" }, scale = 1.0f };
        static readonly TreeLook TPalm = new TreeLook { id = "palm", name = "Cypress", prefabs = new[] { "tree_cypress" }, scale = 1.0f };

        /// <summary>Which tree grows here: palms by the shore, pines up high, and groves of one
        /// broadleaf kind at a time in the lowlands so the woods read as patches, not confetti.</summary>
        static TreeLook LookFor(PropKind k, float px, float pz, float h, float inland, float r)
        {
            if (inland < 26f && h < Sea + 3.2f) return r < 0.8f ? TPalm : TBirch;
            if (k.id == "pine") return r < 0.82f ? TPine : TBirch;
            float grove = Fbm(px * 0.017f + 203f, pz * 0.017f + 91f, 2);
            if (grove < 0.40f) return r < 0.7f ? TOak : TMaple;
            if (grove < 0.47f) return r < 0.6f ? TBirch : TOak;
            if (grove < 0.54f) return r < 0.65f ? TApple : TOak;
            if (grove < 0.60f) return r < 0.6f ? TMaple : TCherry;
            return r < 0.7f ? TCherry : TBirch;
        }

        static IslandNature Nature(IslandData data)
        {
            var root = new GameObject("Nature");
            var nature = root.AddComponent<IslandNature>();
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var k in new[] { Oak, Round, Pine, Pine2, Bush, Rock, Pond, Flowers })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(NatureDir + k.prefab + ".prefab");
                if (go != null) prefabs[k.prefab] = go; else Debug.LogWarning($"[IslandBuilder] missing nature prefab {k.prefab}");
            }
            var trees = new Dictionary<string, GameObject>();
            foreach (var t in new[] { TOak, TMaple, TApple, TCherry, TBirch, TPine, TPalm })
                foreach (var name in t.prefabs)
                {
                    if (trees.ContainsKey(name)) continue;
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(TreeDir + name + ".prefab");
                    if (go != null) trees[name] = go; else Debug.LogWarning($"[IslandBuilder] missing tree prefab {name} (run Farmverse/Art/Build ITHappy Art)");
                }
            var mush = AssetDatabase.LoadAssetAtPath<GameObject>(NatureDir + "mushrooms.prefab");
            if (mush != null) prefabs["mushrooms"] = mush; else Debug.LogWarning("[IslandBuilder] no mushrooms prefab (Farmverse/Art/Build ITHappy Art)");
            var lookRng = new System.Random(7);   // its own stream, so picking species never moves a prop
            var varRng = new System.Random(11);   // bush and rock variants, same idea
            var bushBerry = AssetDatabase.LoadAssetAtPath<GameObject>(NatureDir + "bush_berry.prefab");
            var rockSand = AssetDatabase.LoadAssetAtPath<GameObject>(NatureDir + "rock_6.prefab");
            var rng = new System.Random(20261028);
            float R() => (float)rng.NextDouble();
            var taken = new HashSet<long>();
            int index = 0;
            const float step = 5f;
            for (float z = -Size / 2 + step; z < Size / 2 - step; z += step)
                for (float x = -Size / 2 + step; x < Size / 2 - step; x += step)
                {
                    float px = x + (R() - 0.5f) * step * 0.9f, pz = z + (R() - 0.5f) * step * 0.9f;
                    float roll = R(), pick = R();
                    if (px * px + pz * pz < StarterClear * StarterClear) continue;
                    float h = Height(px, pz);
                    if (h < Sea + 1.65f) continue;                       // beach and sea
                    if (IslandPaths.RoadDist(px, pz) < 8.5f || IslandPaths.RailDist(px, pz) < 9.5f || IslandPaths.TrackDist(px, pz) < 4.5f
                        || IslandPaths.StationDist(px, pz) < IslandPaths.StationRadius + 4f || IslandPaths.InCamp(px, pz, 4f)) continue;
                    var n = NormalAt(px, pz);
                    var sp = Splat(px, pz, h, n);
                    float rocky = sp.a, inland = Inland(px, pz);
                    float forest = Fbm(px * 0.011f + 70f, pz * 0.011f + 31f, 3);
                    float woods = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.52f, 0.66f, forest));
                    PropKind? kind = null;
                    if (rocky > 0.35f) { if (roll < 0.10f) kind = Rock; else if (roll < 0.16f && n.y > 0.8f) kind = pick < 0.7f ? Pine : Pine2; }
                    else if (roll < woods * 0.8f) kind = h > Sea + 4.5f ? (pick < 0.6f ? Pine : Pine2) : (pick < 0.45f ? Oak : pick < 0.8f ? Round : Pine);
                    else if (roll < woods * 0.8f + 0.03f + Mathf.InverseLerp(0.42f, 0.52f, forest) * 0.10f) kind = Bush;
                    else if (roll < woods * 0.8f + 0.05f + Mathf.InverseLerp(0.42f, 0.52f, forest) * 0.10f)
                        kind = pick < 0.5f ? Rock : (pick < 0.75f ? Oak : Round);   // the odd lone tree or boulder in the meadow
                    else if (inland < 14f && roll < 0.12f && pick < 0.5f) kind = Bush; // scrub just above the beach
                    if (kind == null) continue;
                    // the farmland is planned: no trees dotted about its blocks (they line the roads and stand in orchards instead)
                    if (InFarmGrid(px, pz) && (kind.Value.id != "rock" || pick > 0.45f)) continue;
                    Put(kind.Value, px, pz, h, inland, R);
                }

            // a second, denser pass (its own random stream, so every prop above keeps its index and
            // saved clearings stay right): more woods and stone to gather, including some near the farm
            var more = new System.Random(20261031);
            float M() => (float)more.NextDouble();
            for (float z = -Size / 2 + step * 1.5f; z < Size / 2 - step; z += step)
                for (float x = -Size / 2 + step * 1.5f; x < Size / 2 - step; x += step)
                {
                    float px = x + (M() - 0.5f) * step * 0.9f, pz = z + (M() - 0.5f) * step * 0.9f;
                    float roll = M(), pick = M();
                    var d2 = px * px + pz * pz;
                    if (d2 < 24f * 24f) continue;                         // the farm's own yard stays clear
                    float h = Height(px, pz);
                    if (h < Sea + 1.65f) continue;
                    if (IslandPaths.RoadDist(px, pz) < 8.5f || IslandPaths.RailDist(px, pz) < 9.5f || IslandPaths.TrackDist(px, pz) < 4.5f
                        || IslandPaths.StationDist(px, pz) < IslandPaths.StationRadius + 4f || IslandPaths.InCamp(px, pz, 4f)) continue;
                    var n = NormalAt(px, pz);
                    var sp = Splat(px, pz, h, n);
                    float rocky = sp.a, inland = Inland(px, pz);
                    float forest = Fbm(px * 0.011f + 70f, pz * 0.011f + 31f, 3);
                    float woods = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.45f, 0.62f, forest));
                    bool nearFarm = d2 < StarterClear * StarterClear;
                    PropKind? kind = null;
                    if (rocky > 0.3f) { if (roll < 0.22f) kind = Rock; else if (roll < 0.3f && n.y > 0.8f) kind = Pine; }
                    else if (nearFarm) { if (roll < 0.05f) kind = Rock; else if (roll < 0.11f) kind = pick < 0.5f ? Oak : Round; }
                    else if (roll < woods * 0.6f) kind = h > Sea + 4.5f ? Pine : (pick < 0.5f ? Oak : Round);
                    else if (roll < woods * 0.6f + 0.05f) kind = Rock;               // stones scattered through the meadows
                    else if (roll < woods * 0.6f + 0.09f) kind = pick < 0.5f ? Oak : Round;  // and lone trees
                    if (kind == null) continue;
                    if (InFarmGrid(px, pz) && (kind.Value.id != "rock" || pick > 0.45f)) continue;
                    Put(kind.Value, px, pz, h, inland, M);
                }

            // a third pass, again its own stream: wild flowers in the meadows, mushrooms in the woods, the odd pond
            var wild = new System.Random(20261101);
            float F() => (float)wild.NextDouble();
            for (float z = -Size / 2 + step; z < Size / 2 - step; z += step * 1.4f)
                for (float x = -Size / 2 + step; x < Size / 2 - step; x += step * 1.4f)
                {
                    float px = x + (F() - 0.5f) * step, pz = z + (F() - 0.5f) * step;
                    float roll = F();
                    if (px * px + pz * pz < 22f * 22f) continue;
                    float h = Height(px, pz);
                    if (h < Sea + 1.8f) continue;
                    if (IslandPaths.RoadDist(px, pz) < 8.5f || IslandPaths.RailDist(px, pz) < 9.5f || IslandPaths.TrackDist(px, pz) < 4.5f
                        || IslandPaths.StationDist(px, pz) < IslandPaths.StationRadius + 4f || IslandPaths.InCamp(px, pz, 4f)) continue;
                    var n = NormalAt(px, pz);
                    var sp = Splat(px, pz, h, n);
                    float inland = Inland(px, pz);
                    float forest = Fbm(px * 0.011f + 70f, pz * 0.011f + 31f, 3);
                    PropKind? kind = null;
                    if (sp.a > 0.3f) continue;                                        // not on rocky ground
                    if (forest > 0.55f) { if (roll < 0.09f) kind = Mushroom; }
                    else if (roll < 0.006f && n.y > 0.985f && inland > 30f) kind = Pond;
                    else if (roll < 0.07f) kind = Flowers;
                    if (kind == null) continue;
                    if (InFarmGrid(px, pz) && kind.Value.id != "flowers") continue;
                    Put(kind.Value, px, pz, h, inland, F);
                }

            // the planned trees: rows along the farm's streets and lanes, and orchards in a few blocks
            var plan = new System.Random(20261103);
            float P() => (float)plan.NextDouble();
            var rowLooks = new[] { TOak, TMaple, TBirch, TPine, TOak, TMaple };
            int ri = 0;
            foreach (var route in IslandPaths.Routes)
            {
                ri++;
                if (route.kind == IslandPaths.Kind.Rail || route.chapter > 2) continue;
                if (ri % 3 == 0) continue;   // not every road: some stay open, between fields
                var look = rowLooks[ri % rowLooks.Length];
                var off = (route.kind == IslandPaths.Kind.Track ? IslandPaths.TrackHalf : IslandPaths.RoadHalf) + 4.6f;
                var pts = route.pts;
                for (int i = 6; i < pts.Count - 6; i += 12)
                {
                    var dir = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[i - 1]); dir.y = 0; dir.Normalize();
                    var right = new Vector3(dir.z, 0, -dir.x);
                    foreach (var sd in new[] { -1f, 1f })
                    {
                        var q = pts[i] + right * sd * off;
                        if (!InFarmGrid(q.x, q.z)) continue;
                        if (IslandPaths.RoadDist(q.x, q.z) < IslandPaths.RoadHalf + 3.5f && route.kind == IslandPaths.Kind.Track) continue;
                        if (IslandPaths.TrackDist(q.x, q.z) < IslandPaths.TrackHalf + 3f && route.kind != IslandPaths.Kind.Track) continue;
                        if (IslandPaths.RailDist(q.x, q.z) < 9.5f || IslandPaths.StationDist(q.x, q.z) < IslandPaths.StationRadius + 4f) continue;
                        float hq = Height(q.x, q.z);
                        if (hq < Sea + 1.65f) continue;
                        Put(Oak, q.x, q.z, hq, Inland(q.x, q.z), P, look);
                    }
                }
            }
            var gx = IslandPaths.GridX; var gz = IslandPaths.GridZ;
            for (int bx = 0; bx + 1 < gx.Length; bx++)
                for (int bz = 0; bz + 1 < gz.Length; bz++)
                {
                    if ((bx * 7 + bz * 3) % 6 != 1) continue;
                    float x0 = gx[bx] + 9f, x1 = gx[bx + 1] - 9f, z0 = (gz[bz] + gz[bz + 1]) * 0.5f, z1 = gz[bz + 1] - 9f;
                    if (x1 - x0 < 14f || z1 - z0 < 10f) continue;
                    if (!InFarmGrid(x0, z0) || !InFarmGrid(x1, z1) || !InFarmGrid(x0, z1) || !InFarmGrid(x1, z0)) continue;
                    var look = (bx + bz) % 2 == 0 ? TApple : TCherry;
                    for (float z = z0; z <= z1; z += 7f)
                        for (float x = x0; x <= x1; x += 7f)
                        {
                            float hq = Height(x, z);
                            if (hq < Sea + 1.65f) continue;
                            Put(Oak, x, z, hq, Inland(x, z), P, look);
                        }
                }

            void Put(PropKind k, float px, float pz, float h, float inland, System.Func<float> rand, TreeLook? force = null)
            {
                    if (!prefabs.TryGetValue(k.prefab, out var prefab)) return;
                    if (IslandPaths.InCamp(px, pz, 3f)) return;   // the caravan park's lot stays clear
                    bool isTree = k.id is not ("bush" or "rock" or "pond" or "flowers" or "mushroom");
                    if (h > Sea + 26f) return;                                                    // above the tree line
                    if (Vector2.Distance(new Vector2(px, pz), IslandShape.Plateau) < IslandShape.PlateauR - 2f && rand() < 0.85f) return;   // room for the town
                    int born = ChapterOf(px, pz);
                    if (born == 1 && isTree && rand() < 0.4f) return;                               // chapter 1 is open farmland: lighter woods

                    int c = k.cells;
                    int cx = Mathf.RoundToInt((px - data.origin.x) / data.cellSize - c * 0.5f);
                    int cz = Mathf.RoundToInt((pz - data.origin.y) / data.cellSize - c * 0.5f);
                    bool clash = false;
                    for (int j = 0; j < c && !clash; j++) for (int i = 0; i < c; i++) if (taken.Contains(((long)(cz + j) << 32) | (uint)(cx + i))) { clash = true; break; }
                    if (clash) return;
                    for (int j = 0; j < c; j++) for (int i = 0; i < c; i++) taken.Add(((long)(cz + j) << 32) | (uint)(cx + i));

                    // stand it in the middle of its cells so it lines up with what it blocks
                    float wx = data.origin.x + (cx + c * 0.5f) * data.cellSize + (rand() - 0.5f) * 0.6f;
                    float wz = data.origin.y + (cz + c * 0.5f) * data.cellSize + (rand() - 0.5f) * 0.6f;
                    float wy = Height(wx, wz) - 0.15f;
                    string id = k.id, label = k.name;
                    float baseScale = k.scale;
                    bool swayed = false;
                    if (k.id == "bush" && bushBerry != null && varRng.NextDouble() < 0.4) { prefab = bushBerry; label = "Berry bush"; }
                    else if (k.id == "rock" && rockSand != null && varRng.NextDouble() < 0.45) { prefab = rockSand; label = "Stones"; }
                    else if (k.id is not ("bush" or "rock" or "pond" or "flowers" or "mushroom"))
                    {
                        var look = force ?? LookFor(k, wx, wz, h, inland, (float)lookRng.NextDouble());
                        var pick = look.prefabs[lookRng.Next(look.prefabs.Length)];
                        if (trees.TryGetValue(pick, out var tree))
                        {
                            prefab = tree; id = look.id; label = look.name; baseScale = look.scale; swayed = true;
                        }
                    }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    go.name = $"{id}_{index}";
                    go.transform.SetPositionAndRotation(new Vector3(wx, wy, wz), Quaternion.Euler(0, rand() * 360f, 0));
                    float sc = baseScale * (1 + (rand() - 0.5f) * 2 * k.jitter);
                    go.transform.localScale = Vector3.one * sc;
                    SettleOnGround(go, wx, wz, k.id is "bush" or "rock" or "pond" or "flowers" or "mushroom", k.id == "rock" ? 0.12f : 0.1f);
                    if (go.GetComponentInChildren<Collider>() == null)
                    {
                        var b = new Bounds(go.transform.position, Vector3.zero);
                        foreach (var r in go.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                        var box = go.AddComponent<BoxCollider>();
                        box.center = go.transform.InverseTransformPoint(b.center);
                        box.size = b.size / sc;
                    }
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = k.id is "bush" or "flowers" or "pond" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    var np = go.AddComponent<NatureProp>();
                    np.index = index++;
                    np.kind = id; np.displayName = label; np.clearCost = k.cost; np.shaderSway = swayed;
                    np.cells = new RectInt(cx, cz, c, c);
                    np.chapter = ChapterOf(wx, wz);
                            }
            foreach (var go in prefabs.Values)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                    foreach (var m in r.sharedMaterials) if (m != null && !m.enableInstancing) { m.enableInstancing = true; EditorUtility.SetDirty(m); }
            return nature;
        }

        /// <summary>
        /// Stand a prop on the lowest ground under its base — the trunk for a tree, the whole body for
        /// a bush or rock — and settle it in a little, so on a slope no edge hangs in the air.
        /// </summary>
        static void SettleOnGround(GameObject go, float wx, float wz, bool wholeBody, float sink)
        {
            var half = new Vector2(0.5f, 0.5f);
            if (wholeBody)
            {
                var turn0 = go.transform.rotation;
                go.transform.rotation = Quaternion.identity;
                var any = false;
                var b = default(Bounds);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!any) b = r.bounds; else b.Encapsulate(r.bounds); any = true; }
                go.transform.rotation = turn0;
                if (any) half = new Vector2(b.extents.x, b.extents.z) * 0.75f;
            }
            var turn = go.transform.rotation;
            float low = float.MaxValue;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    var o = turn * new Vector3(i * half.x, 0, j * half.y);
                    low = Mathf.Min(low, Height(wx + o.x, wz + o.z));
                }
            go.transform.position = new Vector3(wx, low - sink, wz);
        }

        // ------------------------------------------------------------------ shape

        internal static float Fbm(float x, float z, int oct) => IslandShape.Fbm(x, z, oct);

        /// <summary>Metres inland from the final island's coast (for what grows where: palms by the beach).</summary>
        internal static float Inland(float x, float z) => IslandShape.Inland(Chapters, x, z);

        /// <summary>The final island's ground (every chapter's land): what nature is placed on.</summary>
        public static float Height(float x, float z) => Height(Chapters, x, z);

        /// <summary>Chapter <paramref name="c"/>'s ground: its landscape with its routes levelled in.</summary>
        public static float Height(int c, float x, float z) => IslandPaths.Level(c, x, z, IslandShape.Base(c, x, z));

        internal static Vector3 NormalAt(float x, float z) => NormalAt(Chapters, x, z);

        internal static Vector3 NormalAt(int c, float x, float z)
        {
            const float e = 1f;
            float hx = Height(c, x + e, z) - Height(c, x - e, z);
            float hz = Height(c, x, z + e) - Height(c, x, z - e);
            return new Vector3(-hx, 2 * e, -hz).normalized;
        }

        /// <summary>The first chapter a spot belongs to: where its ground is already as it will be.</summary>
        /// <summary>Inside the planned farm grid (chapters 1–2, off the mountain): no scattered trees here.</summary>
        static bool InFarmGrid(float x, float z) =>
            x > IslandPaths.GridX[0] && x < IslandPaths.GridX[IslandPaths.GridX.Length - 1] && z > IslandPaths.GridZ[0] && z < IslandPaths.GridZ[IslandPaths.GridZ.Length - 1]
            && ChapterOf(x, z) <= 2 && !IslandShape.IsMountain(x, z);

        internal static int ChapterOf(float x, float z)
        {
            float h3 = Height(Chapters, x, z);
            for (int c = 1; c < Chapters; c++)
                if (IslandShape.Inland(c, x, z) > 18f && Mathf.Abs(Height(c, x, z) - h3) < 0.05f) return c;
            return Chapters;
        }

        static Color Splat(float x, float z, float h, Vector3 n)
        {
            float slope = 1 - n.y;
            float rock = Mathf.Clamp01(Mathf.InverseLerp(0.16f, 0.34f, slope) + Mathf.InverseLerp(Sea + 9f, Sea + 18f, h) * 0.85f);
            float sand = 1 - Mathf.InverseLerp(Sea + 0.9f, Sea + 1.55f, h);
            // sandy patches inland (old dunes), only on low flat ground
            float dune = 0f;   // no sandy patches inland: clean lawns, like the packs' renders
            sand = Mathf.Max(Mathf.Clamp01(sand), Mathf.Clamp01(dune) * 0.85f) * (1 - rock);
            float fl = Mathf.InverseLerp(0.6f, 0.75f, IslandShape.Noise(x * 0.018f + 3f, z * 0.018f + 9f));
            fl = Mathf.Clamp01(fl) * 0.9f * (1 - rock) * (1 - sand);
            float grass = Mathf.Clamp01((1 - rock) * (1 - sand) - fl);
            return new Color(grass, fl, sand, rock);
        }

        /// <summary>Bare earth: chapter <paramref name="c"/>'s tracks and route shoulders, plus worn patches in the meadow.</summary>
        static float Dirt(int c, float x, float z, float h, Color splat)
        {
            float meadow = Mathf.Clamp01(splat.r + splat.g);
            float worn = 0f * meadow;   // the packs' farms are clean lawn between sand roads: no worn patches
            float muddy = 0f;
            // the farmyard in the middle, where the tracks meet
            float yardR = Mathf.Sqrt(x * x + z * z) + (IslandShape.Noise(x * 0.3f + 5f, z * 0.3f + 9f) - 0.5f) * 3f;
            float yard = 0f * yardR;    // the planned roads replace the old round farmyard
            // the town square on the plateau (chapter 3)
            float square = c >= 3 ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 13f, Vector2.Distance(new Vector2(x, z), IslandShape.Plateau))) : 0f;
            return Mathf.Clamp01(Mathf.Max(Mathf.Max(IslandPaths.DirtWeight(c, x, z) * (1 - splat.a), Mathf.Max(yard, square)), Mathf.Max(worn, muddy)));
        }

        static Mesh ChunkMesh(int ch, float x0, float z0, float size)
        {
            int n = Quads + 1;
            var v = new Vector3[n * n];
            var nn = new Vector3[n * n];
            var c = new Color[n * n];
            var dirt = new Vector2[n * n];
            float step = size / Quads;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    float h = Height(ch, x, z);
                    var nor = NormalAt(ch, x, z);
                    int k = j * n + i;
                    v[k] = new Vector3(x, h, z);
                    nn[k] = nor;
                    c[k] = Splat(x, z, h, nor);
                    dirt[k] = new Vector2(Dirt(ch, x, z, h, c[k]), 0);
                }
            var tris = new int[Quads * Quads * 6];
            int t = 0;
            for (int j = 0; j < Quads; j++)
                for (int i = 0; i < Quads; i++)
                {
                    int a = j * n + i, b = a + 1, d = a + n, e = d + 1;
                    tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    tris[t++] = b; tris[t++] = d; tris[t++] = e;
                }
            var m = new Mesh { name = $"island ground c{ch}" };
            m.vertices = v; m.normals = nn; m.colors = c; m.uv = dirt; m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        static bool SameGround(Mesh a, Mesh b)
        {
            if (b == null) return false;
            var va = a.vertices; var vb = b.vertices;
            if (va.Length != vb.Length) return false;
            for (int i = 0; i < va.Length; i++) if (Mathf.Abs(va[i].y - vb[i].y) > 0.002f) return false;
            var ca = a.colors; var cb = b.colors;
            for (int i = 0; i < ca.Length; i++) if (Mathf.Abs(ca[i].r - cb[i].r) + Mathf.Abs(ca[i].b - cb[i].b) + Mathf.Abs(ca[i].a - cb[i].a) > 0.01f) return false;
            var ua = a.uv; var ub = b.uv;
            for (int i = 0; i < ua.Length; i++) if (Mathf.Abs(ua[i].x - ub[i].x) > 0.01f) return false;
            return true;
        }

        // ------------------------------------------------------------------ water

        static Mesh WaterGrid(float min, float size, int quads)
        {
            int n = quads + 1;
            var v = new Vector3[n * n];
            float step = size / quads;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    v[j * n + i] = new Vector3(min + i * step, Sea, min + j * step);
            var tris = new List<int>(quads * quads * 6);
            for (int j = 0; j < quads; j++)
                for (int i = 0; i < quads; i++)
                {
                    int a = j * n + i, b = a + 1, d = a + n, e = d + 1;
                    tris.AddRange(new[] { a, d, b, b, d, e });
                }
            var m = new Mesh { name = "water near", indexFormat = IndexFormat.UInt32 };
            m.vertices = v; m.triangles = tris.ToArray();
            m.RecalculateNormals(); m.RecalculateBounds();
            m.bounds = new Bounds(m.bounds.center, m.bounds.size + Vector3.up * 4);
            return m;
        }

        static Mesh WaterRing(float half, float cell, float hole)
        {
            var v = new List<Vector3>(); var tris = new List<int>();
            int n = Mathf.RoundToInt(2 * half / cell);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = -half + i * cell, z = -half + j * cell;
                    if (x >= -hole && x + cell <= hole && z >= -hole && z + cell <= hole) continue;
                    int b = v.Count;
                    v.Add(new Vector3(x, Sea, z)); v.Add(new Vector3(x + cell, Sea, z));
                    v.Add(new Vector3(x, Sea, z + cell)); v.Add(new Vector3(x + cell, Sea, z + cell));
                    tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
                }
            var m = new Mesh { name = "water far", indexFormat = IndexFormat.UInt32 };
            m.SetVertices(v); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Texture2D WaterMask(int ch)
        {
            const int R = 256;
            var tex = new Texture2D(R, R, TextureFormat.RGBA32, false, true);
            var px = new Color[R * R];
            for (int j = 0; j < R; j++)
                for (int i = 0; i < R; i++)
                {
                    float x = -Size / 2 + (i + 0.5f) * Size / R, z = -Size / 2 + (j + 0.5f) * Size / R;
                    float h = Height(ch, x, z);
                    float d = Mathf.Clamp01((Sea - h) / 7f);
                    px[j * R + i] = new Color(d, Mathf.Clamp01(h / 32f), d, 1);   // G: ground height for the mist
                }
            tex.SetPixels(px); tex.Apply();
            var path = $"{Gen}/water_mask_c{ch}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = false; imp.mipmapEnabled = false; imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed; imp.filterMode = FilterMode.Bilinear;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ grid

        static IslandData GridData()
        {
            var path = $"{Root}/IslandData.asset";
            var d = AssetDatabase.LoadAssetAtPath<IslandData>(path);
            if (d == null) { d = ScriptableObject.CreateInstance<IslandData>(); AssetDatabase.CreateAsset(d, path); }
            int w = Mathf.RoundToInt(Size / Cell);
            d.origin = new Vector2(-Size / 2, -Size / 2);
            d.cellSize = Cell; d.width = w; d.depth = w; d.seaLevel = Sea;
            d.chapters = Chapters;
            d.layoutVersion = IslandShape.LayoutVersion;
            d.previousLayoutScale = IslandShape.K / IslandShape.PreviousScale;
            d.buildable = new byte[w * w];
            d.firstChapter = new byte[w * w];
            var hs = new float[Chapters][];
            for (int c = 0; c < Chapters; c++) hs[c] = new float[w * w];
            var oks = new bool[Chapters][];
            for (int c = 0; c < Chapters; c++) oks[c] = new bool[w * w];
            for (int z = 0; z < w; z++)
                for (int x = 0; x < w; x++)
                {
                    float x0 = d.origin.x + x * Cell, z0 = d.origin.y + z * Cell, xm = x0 + Cell / 2, zm = z0 + Cell / 2;
                    int k = z * w + x;
                    bool route = IslandPaths.Blocks(xm, zm);
                    for (int c = 1; c <= Chapters; c++)
                    {
                        float a = Height(c, x0, z0), b = Height(c, x0 + Cell, z0), e = Height(c, x0, z0 + Cell), f = Height(c, x0 + Cell, z0 + Cell);
                        float mid = Height(c, xm, zm);
                        float lo = Mathf.Min(Mathf.Min(a, b), Mathf.Min(e, Mathf.Min(f, mid)));
                        float hi = Mathf.Max(Mathf.Max(a, b), Mathf.Max(e, Mathf.Max(f, mid)));
                        // a bridge is walkable at its deck
                        var deck = IslandPaths.BridgeAt(c, xm, zm);
                        hs[c - 1][k] = deck.HasValue ? Mathf.Max(hi, deck.Value) : hi;
                        // buildable: dry, flat, off every route, well inside this chapter's coast, not up a mountain
                        oks[c - 1][k] = !route && lo > Sea + 1.0f && hi - lo < 0.45f && IslandShape.Inland(c, xm, zm) >= 22f && hi < Sea + 14f
                                        && Vector2.Distance(new Vector2(xm, zm), IslandShape.Plateau) > 14f;   // the town square stays open
                    }
                }
            // nothing built may ever move: a cell is buildable in a chapter only if it stays buildable, at the
            // same height, in every chapter after (near a later road its ground is levelled: it waits for that road)
            int moved = 0;
            for (int k = 0; k < w * w; k++)
                for (int c = 1; c <= Chapters; c++)
                {
                    bool ok = oks[c - 1][k];
                    for (int c2 = c + 1; c2 <= Chapters && ok; c2++) ok = oks[c2 - 1][k] && Mathf.Abs(hs[c2 - 1][k] - hs[c - 1][k]) < 0.01f;
                    if (!ok) continue;
                    d.buildable[k] |= (byte)(1 << (c - 1));
                    if (d.firstChapter[k] == 0) d.firstChapter[k] = (byte)c;
                }
            for (int k = 0; k < w * w; k++)
                for (int c = 2; c <= Chapters; c++)
                    if ((d.buildable[k] & (1 << (c - 2))) != 0 && (d.buildable[k] & (1 << (c - 1))) == 0) moved++;
            d.heightCh1 = hs[0];
            d.heightCh2 = hs[Mathf.Min(1, Chapters - 1)];
            d.height = hs[Chapters - 1];
            d.railPath = IslandPaths.Rail.ToArray();
            d.stationIndex = IslandPaths.StationIndex;
            EditorUtility.SetDirty(d);
            if (moved > 0) Debug.LogError($"[IslandBuilder] {moved} cells stop being buildable in a later chapter: the island must only grow around its old land");
            for (int c = 1; c <= Chapters; c++)
            {
                int n = 0; for (int k = 0; k < d.buildable.Length; k++) if ((d.buildable[k] & (1 << (c - 1))) != 0) n++;
                Debug.Log($"[IslandBuilder] chapter {c}: {n} buildable cells");
            }
            return d;
        }

        // ------------------------------------------------------------------ catalogue

        static readonly Dictionary<string, (string name, int price, string cat)> Info = new Dictionary<string, (string, int, string)>
        {
            ["farmhouse"] = ("Farmhouse", 0, "Home"), ["barn"] = ("Barn", 600, "Farm"), ["chicken_coop"] = ("Chicken coop", 250, "Animals"),
            ["market_stall"] = ("Market stall", 200, "Shops"), ["grain_silos"] = ("Grain silos", 450, "Farm"), ["windmill"] = ("Windmill", 900, "Farm"),
            ["greenhouse"] = ("Greenhouse", 800, "Farm"), ["potting_shed"] = ("Potting shed", 300, "Farm"),
            ["stable"] = ("Stable", 700, "Animals"), ["pigsty"] = ("Pigsty", 350, "Animals"),
            ["packing_shed"] = ("Packing shed", 500, "Orchard"), ["cider_press"] = ("Cider press", 650, "Orchard"),
            ["dairy_factory"] = ("Dairy", 1200, "Factories"), ["bakery_factory"] = ("Bakery", 1100, "Factories"), ["sugar_mill"] = ("Sugar mill", 1300, "Factories"),
            ["feed_mill"] = ("Feed mill", 1000, "Factories"), ["lumber_yard"] = ("Lumber yard", 1100, "Factories"),
            ["fishing_shed"] = ("Fishing shed", 500, "Sea"), ["fish_market"] = ("Fish market", 900, "Sea"),
            ["mine_entrance"] = ("Quarry", 1400, "Mine"),
            ["army_house"] = ("Army house", 1200, "People"), ["workers_house"] = ("Workers' house", 800, "People"), ["builders_hut"] = ("Builders' hut", 500, "People"),
            ["facility_well"] = ("Water tower", 400, "Farm"), ["facility_warehouse"] = ("Warehouse", 900, "Farm"),
            ["bakery_shop"] = ("Bakery shop", 800, "Shops"), ["grocer_shop"] = ("Grocer", 800, "Shops"),
            ["town_hall"] = ("Town hall", 3000, "Town"), ["school"] = ("School", 1800, "Town"), ["clinic"] = ("Clinic", 1600, "Town"),
            ["fire_station"] = ("Fire station", 1700, "Town"), ["police_station"] = ("Police station", 1700, "Town"), ["library"] = ("Library", 1400, "Town"),
            ["community_hall"] = ("Community hall", 2000, "Town"), ["post_office"] = ("Post office", 1200, "Town"), ["bank"] = ("Bank", 2500, "Town"),
            ["chapel"] = ("Chapel", 1500, "Town"),
        };
        // gameplay design §1: a new island starts with one farmhouse; everything else is bought
        static readonly HashSet<string> Starter = new HashSet<string> { "farmhouse", "market_stall" };   // and a stall to sell at

        static BuildingCatalogue Catalogue(float cell)
        {
            var path = $"{Root}/BuildingCatalogue.asset";
            var cat = AssetDatabase.LoadAssetAtPath<BuildingCatalogue>(path);
            if (cat == null) { cat = ScriptableObject.CreateInstance<BuildingCatalogue>(); AssetDatabase.CreateAsset(cat, path); }
            var keep = cat.entries.ToDictionary(e => e.id);   // hand-tuned values survive a rebuild
            cat.entries.Clear();
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { IthappyArt.Buildings }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (prefab == null || prefab.name.Contains("__")) continue;   // "id__n" is a restyle of id
                var box = prefab.GetComponent<BoxCollider>();
                var size = box != null ? box.size : new Vector3(6, 4, 6);
                var fp = new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(size.x / cell - 0.15f)), Mathf.Max(1, Mathf.CeilToInt(size.z / cell - 0.15f)));
                Info.TryGetValue(prefab.name, out var info);
                var e = keep.TryGetValue(prefab.name, out var old) ? old : new BuildingCatalogue.Entry
                {
                    id = prefab.name,
                    displayName = info.name ?? prefab.name,
                    price = info.name != null ? info.price : 500,
                    category = info.cat ?? "Other",
                    starter = Starter.Contains(prefab.name),
                };
                e.prefab = prefab;
                e.footprint = fp;
                e.starter = Starter.Contains(prefab.name);
                cat.entries.Add(e);
            }
            // fields are the soil bed the farm uses; decorations, fences and paths are pack props (Content/Art/Props)
            const string W = IthappyArt.Props + "/";
            Extra(cat, keep, "field", W + "field.prefab", "Field", 20, "Farm", new Vector2Int(3, 3));
            foreach (var (id, name, price) in new[]
            {
                ("decor_bench", "Picnic table", 60), ("decor_flower_bed", "Flower planter", 80), ("decor_lamp_post", "Lamp post", 100),
                ("decor_mailbox", "Crates", 60), ("decor_hay_bales", "Hay bales", 90), ("decor_fountain", "Water trough", 600),
                ("decor_beehive", "Beehive box", 120), ("decor_default", "Lawn mower", 70), ("sign_default", "Signpost", 50),
                ("seasonal_default", "Seasonal display", 150), ("pen", "Animal pen", 150),
            })
                Extra(cat, keep, id, W + id + ".prefab", name, price, "Decor", null, cell);
            foreach (var (id, name, price) in new[] { ("fence_wood", "Wood fence", 20), ("fence_white", "White fence", 30), ("path_dirt", "Dirt path", 5), ("path_stone", "Stone path", 15) })
                Extra(cat, keep, id, W + id + ".prefab", name, price, "Paths", new Vector2Int(1, 1), cell);
            // a building's other pack model, when it has one: a look it can be restyled to
            foreach (var e in cat.entries)
                e.altPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(IthappyArt.Buildings + "/" + e.id + "__1.prefab");
            cat.entries.Sort((a, b) => string.CompareOrdinal(a.category + a.price.ToString("D6"), b.category + b.price.ToString("D6")));
            EditorUtility.SetDirty(cat);
            return cat;
        }

        static void Extra(BuildingCatalogue cat, Dictionary<string, BuildingCatalogue.Entry> keep, string id, string prefabPath, string name, int price, string category, Vector2Int? footprint, float cell = 2f)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Debug.LogWarning($"[IslandBuilder] {id}: no prefab at {prefabPath}"); return; }
            var box = prefab.GetComponent<BoxCollider>();
            var size = box != null ? box.size : new Vector3(6, 4, 6);
            var fp = footprint ?? new Vector2Int(Mathf.Max(1, Mathf.CeilToInt(size.x / cell - 0.15f)), Mathf.Max(1, Mathf.CeilToInt(size.z / cell - 0.15f)));
            var e = keep.TryGetValue(id, out var old) ? old : new BuildingCatalogue.Entry { id = id, displayName = name, price = price, category = category };
            e.prefab = prefab;
            e.footprint = fp;
            e.starter = Starter.Contains(id);
            cat.entries.Add(e);
        }

        // ------------------------------------------------------------------ materials

        static Texture2D T(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static Material GroundMaterial()
        {
            var path = $"{Root}/IslandGround.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Farmverse/IslandGround")); AssetDatabase.CreateAsset(m, path); }
            const string W = "Assets/Farmverse/Content/Textures/World/";
            m.SetTexture("_Grass", T(W + "grass_meadow_albedo.png"));
            m.SetTexture("_GrassN", T(W + "grass_meadow_normal.png"));
            m.SetTexture("_Flowers", T(W + "grass_flowers_albedo.png"));
            m.SetTexture("_Sand", T(W + "sand_beach_albedo.png"));
            m.SetTexture("_SandN", T(W + "sand_beach_normal.png"));
            m.SetTexture("_Rock", T(W + "rock_cliff_albedo.png"));
            m.SetTexture("_RockN", T(W + "rock_cliff_normal.png"));
            m.SetTexture("_Dirt", T(W + "dirt_path_albedo.png"));
            m.SetTexture("_DirtN", T(W + "dirt_path_normal.png"));
            m.SetTexture("_Macro", T(Root + "/Textures/macro_noise.png"));
            m.SetColor("_DirtTint", new Color(0.86f, 0.80f, 0.72f));
            m.SetFloat("_DirtSat", 0.55f);
            // the low-poly look of the packs' renders: flat palette colours, soft big patches, hardly any normal detail
            m.SetFloat("_Flat", 1f);
            m.SetColor("_FlatGrass", new Color(0.50f, 0.78f, 0.31f));
            m.SetColor("_FlatFlowers", new Color(0.62f, 0.83f, 0.33f));
            m.SetColor("_FlatSand", new Color(0.93f, 0.80f, 0.58f));
            m.SetColor("_FlatRock", new Color(0.66f, 0.54f, 0.46f));
            m.SetColor("_FlatDirt", new Color(0.92f, 0.77f, 0.55f));
            m.SetColor("_MacroDark", new Color(0.90f, 0.95f, 0.84f));
            m.SetColor("_MacroLight", new Color(1.06f, 1.04f, 0.94f));
            m.SetFloat("_Saturation", 1.0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>
        /// The horizon (the packs' renders have low-poly mountains behind every farm): a ring of the Farm pack's
        /// full-size mountains far out beyond the sea, melting into the haze, and two wind turbines on the island.
        /// </summary>
        static void Backdrop()
        {
            var root = new GameObject("Backdrop");
            var names = new[] { "mountain_001", "mountain_007", "mountain_008", "mountain_007", "mountain_001", "mountain_008", "mountain_007" };
            var rnd = new System.Random(7);
            for (int i = 0; i < names.Length; i++)
            {
                var pf = IthappyArt.Pack(names[i]);
                if (pf == null) continue;
                var go = IthappyArt.Inst(pf, root.transform);
                go.name = "Mountain " + i;
                float a = (i / (float)names.Length) * Mathf.PI * 2f + (float)rnd.NextDouble() * 0.3f;
                float r = 950f + (float)rnd.NextDouble() * 300f;
                float sc = 0.45f + (float)rnd.NextDouble() * 0.35f;   // low ranges with sea between them, not a wall
                go.transform.localScale = Vector3.one * sc;
                go.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
                var b = IthappyArt.WorldBounds(go);
                go.transform.position += new Vector3(Mathf.Cos(a) * r - b.center.x, -6f - b.min.y, Mathf.Sin(a) * r - b.center.z);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var mr in go.GetComponentsInChildren<Renderer>()) { mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; }
                foreach (var t in go.GetComponentsInChildren<Transform>()) GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }
            // two turbines on the island's far rim (tower_009 is 60 m tall; a third of that reads right from the play camera)
            var turbine = IthappyArt.Pack("tower_009");
            if (turbine != null)
                foreach (var (angle, yaw) in new[] { (150f, 20f), (-50f, -140f) })
                {
                    // walk in from the sea along this bearing to the first firm ground, then a little further in
                    float x = 0, z = 0, y = -1;
                    for (float r = 320f; r > 60f; r -= 4f)
                    {
                        x = Mathf.Cos(angle * Mathf.Deg2Rad) * r; z = Mathf.Sin(angle * Mathf.Deg2Rad) * r;
                        if (IslandShape.Inland(Chapters, x, z) > 30f) { y = IslandShape.Base(Chapters, x, z); if (y > 1f) break; }
                    }
                    if (y < 1f) { Debug.LogWarning($"[Island] no ground for a turbine at bearing {angle}"); continue; }
                    var go = IthappyArt.Inst(turbine, root.transform);
                    go.name = "Wind turbine";
                    go.transform.localScale = Vector3.one * 0.38f;
                    go.transform.rotation = Quaternion.Euler(0, yaw, 0);
                    var b = IthappyArt.WorldBounds(go);
                    go.transform.position += new Vector3(x - b.center.x, y - 0.5f - b.min.y, z - b.center.z);
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    Transform? rotor = null;
                    foreach (var t in go.GetComponentsInChildren<Transform>())
                    {
                        var n = t.name.ToLowerInvariant();
                        if (n.Contains("014") || n.Contains("rotor") || n.Contains("blade") || n.Contains("propeller")) { rotor = t; break; }
                    }
                    if (rotor != null) { rotor.name += "_rotor"; var sp = go.AddComponent<Spinner>(); IthappyArt.Set(sp, "localAxis", Vector3.forward); IthappyArt.Set(sp, "speed", 25f); }
                    else Debug.Log("[Island] turbine children: " + string.Join(", ", go.GetComponentsInChildren<Transform>().Select(t => t.name)));
                    Debug.Log($"[Island] turbine at {x:F0},{z:F0} (ground {y:F1})");
                }
        }

        /// <summary>A global post volume on the camera: mild bloom, ACES, a little extra saturation and warmth, a soft vignette.</summary>
        static void PostProcessing(GameObject camGo)
        {
            var path = $"{Root}/IslandPost.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            T2 Comp<T2>() where T2 : VolumeComponent
            {
                if (!profile.TryGet<T2>(out var c)) c = profile.Add<T2>(true);
                return c;
            }
            var bloom = Comp<UnityEngine.Rendering.Universal.Bloom>();
            bloom.threshold.Override(0.95f); bloom.intensity.Override(0.45f); bloom.scatter.Override(0.65f);
            var tone = Comp<UnityEngine.Rendering.Universal.Tonemapping>();
            tone.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.ACES);
            var grade = Comp<UnityEngine.Rendering.Universal.ColorAdjustments>();
            grade.postExposure.Override(0.25f); grade.contrast.Override(8f); grade.saturation.Override(14f);
            grade.colorFilter.Override(new Color(1f, 0.97f, 0.94f));
            var vignette = Comp<UnityEngine.Rendering.Universal.Vignette>();
            vignette.intensity.Override(0.2f); vignette.smoothness.Override(0.5f);
            EditorUtility.SetDirty(profile);
            var volume = camGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        static Material WaterMaterial()
        {
            var path = $"{Root}/Water.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Farmverse/Water")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_NormalTex", T(Root + "/Textures/water_normal.png"));
            m.SetTexture("_FoamTex", T(Root + "/Textures/foam.png"));
            // the packs' renders: a flat turquoise sea
            m.SetColor("_Shallow", new Color(0.46f, 0.90f, 0.82f));
            m.SetColor("_Mid", new Color(0.22f, 0.74f, 0.78f));
            m.SetColor("_Deep", new Color(0.12f, 0.52f, 0.66f));
            m.SetColor("_Sky", new Color(0.62f, 0.88f, 0.90f));
            EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh MistQuad(float half)
        {
            var m = new Mesh { name = "mist_quad" };
            m.vertices = new[] { new Vector3(-half, 0, -half), new Vector3(-half, 0, half), new Vector3(half, 0, half), new Vector3(half, 0, -half) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        static Material MistMaterial(int i, Texture2D mask, float density, float cover, float scale, float overLand, Vector2 wind)
        {
            var path = $"{Root}/Mist_{i + 1}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Farmverse/Mist")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_Noise", T(Root + "/Textures/macro_noise.png"));
            m.SetTexture("_Mask", mask);
            m.SetVector("_MaskRect", new Vector4(-Size / 2, -Size / 2, Size, Size));
            m.SetFloat("_Density", density);
            m.SetFloat("_Cover", cover);
            m.SetFloat("_Scale", scale);
            m.SetFloat("_OverLand", overLand);
            m.SetVector("_Wind", new Vector4(wind.x, wind.y, 0, 0));
            m.renderQueue = 3005 + i;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void SetArray(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

namespace Farmverse.Editor
{
    /// <summary>Import rules for the island helper textures (Content/Island/Textures).</summary>
    public sealed class IslandTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Farmverse/Content/Island/Textures")) return;
            var t = (TextureImporter)assetImporter;
            t.wrapMode = TextureWrapMode.Repeat;
            t.mipmapEnabled = true;
            t.anisoLevel = 2;
            if (assetPath.EndsWith("water_normal.png")) t.textureType = TextureImporterType.NormalMap;
            else t.sRGBTexture = false;
        }
    }
}
