#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using Farmverse.Presentation.World;

namespace Farmverse.Editor
{
    /// <summary>
    /// Lets the editor be driven from outside: a file "Claude outputs/run/command.txt" holding a static
    /// method name (Namespace.Type.Method) is run on the next editor tick, with everything it logs written
    /// to log.txt and its outcome to result.txt. compiled.txt is rewritten whenever scripts compile and
    /// load; compile_errors.txt whenever a compile fails. Development only.
    /// </summary>
    [InitializeOnLoad]
    public static class ClaudeRunner
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Claude outputs", "run"));
        static readonly List<string> Log = new List<string>();
        static bool _running;
        static double _next;

        static ClaudeRunner()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(Path.Combine(Dir, "compiled.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " scripts loaded\n");
                var errs = Path.Combine(Dir, "compile_errors.txt");
                if (File.Exists(errs)) File.Delete(errs);
            }
            catch (Exception e) { Debug.LogWarning("[ClaudeRunner] " + e.Message); }
            EditorApplication.update += Poll;
            _keepPlaying = SessionState.GetBool("fv_keep_playing", false);
            Application.logMessageReceived += OnLog;
            CompilationPipeline.assemblyCompilationFinished += OnCompiled;
        }

        static void OnCompiled(string assembly, CompilerMessage[] messages)
        {
            var errors = messages.Where(m => m.type == CompilerMessageType.Error).ToList();
            if (errors.Count == 0) return;
            var lines = errors.Select(m => $"{m.file}({m.line},{m.column}): {m.message}");
            File.AppendAllText(Path.Combine(Dir, "compile_errors.txt"), $"[{DateTime.Now:HH:mm:ss}] {Path.GetFileName(assembly)}\n{string.Join("\n", lines)}\n");
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            if (!_running) return;
            Log.Add($"[{type}] {condition}" + (type == LogType.Exception || type == LogType.Error ? "\n" + stack : ""));
        }

        /// <summary>Play mode started by the runner keeps running while the editor is in the background.</summary>
        static bool _keepPlaying;

        static void Poll()
        {
            if (_keepPlaying && EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _next || _running) return;
            _next = EditorApplication.timeSinceStartup + 1.5;
            var cmd = Path.Combine(Dir, "command.txt");
            if (!File.Exists(cmd)) return;
            string text;
            try { text = File.ReadAllText(cmd).Trim(); File.Delete(cmd); }
            catch { return; }
            if (text.Length == 0) return;
            Run(text);
        }

        static void Run(string text)
        {
            _running = true;
            Log.Clear();
            var result = "ok";
            var t0 = DateTime.Now;
            try
            {
                foreach (var call in text.Split(new[] { '\n', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var name = call.Trim();
                    if (name.Length == 0) continue;
                    int dot = name.LastIndexOf('.');
                    var typeName = name.Substring(0, dot); var method = name.Substring(dot + 1);
                    var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(x => x != null);
                    if (type == null) throw new Exception("no type " + typeName);
                    var m = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        .FirstOrDefault(x => x.Name == method && x.GetParameters().Length == 0);
                    if (m == null) throw new Exception("no parameterless static method " + name);
                    Log.Add($"--- {name}");
                    m.Invoke(null, null);
                }
            }
            catch (Exception e)
            {
                var inner = e is TargetInvocationException ti && ti.InnerException != null ? ti.InnerException : e;
                result = "FAILED: " + inner.GetType().Name + ": " + inner.Message + "\n" + inner.StackTrace;
            }
            finally
            {
                _running = false;
                try
                {
                    File.WriteAllText(Path.Combine(Dir, "log.txt"), string.Join("\n", Log));
                    File.WriteAllText(Path.Combine(Dir, "result.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ({(DateTime.Now - t0).TotalSeconds:F0}s) {result}\n");
                }
                catch { }
            }
        }

        /// <summary>Run the EditMode tests; the outcome lands in tests.txt when they finish.</summary>
        public static void RunTests()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new TestLog());
            api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }));
        }

        sealed class TestLog : ICallbacks
        {
            readonly List<string> _fails = new List<string>();
            int _passed, _failed, _skipped;
            public void RunStarted(ITestAdaptor t) { File.WriteAllText(Path.Combine(Dir, "tests.txt"), "running\n"); }
            public void RunFinished(ITestResultAdaptor r)
            {
                File.WriteAllText(Path.Combine(Dir, "tests.txt"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} passed {_passed} failed {_failed} skipped {_skipped}\n{string.Join("\n", _fails)}\n");
            }
            public void TestStarted(ITestAdaptor t) { }
            public void TestFinished(ITestResultAdaptor r)
            {
                if (r.HasChildren) return;
                if (r.TestStatus == TestStatus.Passed) _passed++;
                else if (r.TestStatus == TestStatus.Failed) { _failed++; _fails.Add(r.FullName + "\n    " + (r.Message ?? "").Trim().Replace("\n", "\n    ")); }
                else _skipped++;
            }
        }

        /// <summary>Pick up files changed on disk (what focusing the editor window does).</summary>
        public static void Refresh() => AssetDatabase.Refresh();

        /// <summary>A screenshot of the Scene or Game view saved beside the run files, for looking at from outside.</summary>
        /// <summary>Enter Play mode (scripts must be compiled first).</summary>
        public static void Play() { PlayerSettings.runInBackground = true; SessionState.SetBool("fv_keep_playing", true); _keepPlaying = true; EditorApplication.isPlaying = true; }

        /// <summary>Leave Play mode.</summary>
        public static void Stop() { SessionState.SetBool("fv_keep_playing", false); EditorApplication.isPlaying = false; }

        /// <summary>Play mode: call a GameUi method by name with run/ui.txt's text as its one string argument (e.g. "Go play").</summary>
        public static void Ui()
        {
            var file = Path.Combine(Dir, "ui.txt");
            var ui = UnityEngine.Object.FindFirstObjectByType<Farmverse.Presentation.UI.Game.GameUi>();
            if (ui == null || !File.Exists(file)) { Debug.LogWarning("[ClaudeRunner] no GameUi or ui.txt"); return; }
            var parts = File.ReadAllText(file).Trim().Split(new[] { ' ' }, 2);
            var m = typeof(Farmverse.Presentation.UI.Game.GameUi).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(x => x.Name == parts[0] && x.GetParameters().Length >= (parts.Length > 1 ? 1 : 0)
                                     && (parts.Length == 1 || x.GetParameters()[0].ParameterType == typeof(string)));
            if (m == null) { Debug.LogWarning("[ClaudeRunner] no GameUi." + parts[0]); return; }
            var args = m.GetParameters().Select((p, i) => i == 0 && parts.Length > 1 ? (object)parts[1] : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            m.Invoke(ui, args);
            Debug.Log($"[ClaudeRunner] GameUi.{parts[0]}({(parts.Length > 1 ? parts[1] : "")})");
        }

        /// <summary>Play mode: log every HandReach (who, targets, weights).</summary>
        public static void Probe()
        {
            Debug.Log($"[Probe] frame {Time.frameCount} t {Time.time:0.0} scale {Time.timeScale} paused {EditorApplication.isPaused} keep {_keepPlaying}");
            foreach (var h in UnityEngine.Object.FindObjectsByType<HandReach>(FindObjectsSortMode.None))
            {
                var wl = typeof(HandReach).GetField("_wl", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(h);
                var an = typeof(HandReach).GetField("_anim", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(h) as Animator;
                var lh = an != null ? an.GetBoneTransform(HumanBodyBones.LeftHand) : null;
                Debug.Log($"[Probe] {h.name} L={(h.Left != null ? h.Left.name : "-")} R={(h.Right != null ? h.Right.name : "-")} wl={wl} anim={(an != null ? an.name + " human " + an.isHuman : "none")} hand={(lh != null ? lh.position.ToString() : "-")} target={(h.Left != null ? (h.Left.position + h.Left.up * h.LeftAlong).ToString() : "-")}");
            }
        }

        /// <summary>Play mode: select the first placed building whose id contains run/select.txt's text (as a tap would).</summary>
        public static void Select()
        {
            var s = UnityEngine.Object.FindFirstObjectByType<Farmverse.Presentation.World.Island.IslandSandbox>();
            var file = Path.Combine(Dir, "select.txt");
            if (s == null || !File.Exists(file)) return;
            var want = File.ReadAllText(file).Trim();
            var b = UnityEngine.Object.FindObjectsByType<Farmverse.Presentation.World.Island.PlacedBuilding>(FindObjectsSortMode.None).FirstOrDefault(x => x.id.Contains(want));
            if (b == null) { Debug.LogWarning("[ClaudeRunner] no building " + want); return; }
            s.Select(b);
            Debug.Log("[ClaudeRunner] selected " + b.id);
        }

        /// <summary>Play mode: the Ride button.</summary>
        public static void Ride()
        {
            var s = UnityEngine.Object.FindFirstObjectByType<Farmverse.Presentation.World.Island.IslandSandbox>();
            if (s == null) { Debug.LogWarning("[ClaudeRunner] no sandbox"); return; }
            s.ToggleRide();
            Debug.Log("[ClaudeRunner] riding: " + s.Riding);
        }

        /// <summary>Play mode: hold a tool in the farmer's hand for a look (run/tool.txt: tool id), via the host's PlayerTool.</summary>
        public static void PlayerTool()
        {
            var host = UnityEngine.Object.FindFirstObjectByType<Farmverse.Presentation.World.Island.IslandEconomyHost>();
            var file = Path.Combine(Dir, "tool.txt");
            if (host == null || !File.Exists(file)) return;
            var m = host.GetType().GetMethod("PlayerTool", BindingFlags.Instance | BindingFlags.NonPublic);
            m?.Invoke(host, new object?[] { File.ReadAllText(file).Trim() });
            var until = host.GetType().GetField("_playerToolUntil", BindingFlags.Instance | BindingFlags.NonPublic);
            until?.SetValue(host, Time.time + 30f);
        }

        /// <summary>Play mode: start a zombie night now (doesn't count as the day's).</summary>
        public static void RaidNow()
        {
            var life = UnityEngine.Object.FindFirstObjectByType<Farmverse.Presentation.World.Island.IslandLife>();
            if (life == null) { Debug.LogWarning("[ClaudeRunner] no IslandLife in play"); return; }
            life.RaidNow();
        }

        /// <summary>Play mode: raise the island to the level in run/level.txt (XP added).</summary>
        public static void SetLevel()
        {
            var host = UnityEngine.Object.FindFirstObjectByType<Farmverse.Presentation.World.Island.IslandEconomyHost>();
            var file = Path.Combine(Dir, "level.txt");
            if (host == null || !File.Exists(file) || !int.TryParse(File.ReadAllText(file).Trim(), out var level)) { Debug.LogWarning("[ClaudeRunner] no host or level.txt"); return; }
            var eco = host.Economy;
            var need = Farmverse.Domain.Gameplay.GameplayRules.XpAt(level) - eco.Xp;
            if (need > 0) eco.AddXp(need);
            Debug.Log($"[ClaudeRunner] island level now {eco.BestLevel}");
        }

        /// <summary>Play mode: point the island camera at the first scene object whose name contains run/look.txt's text.</summary>
        public static void LookAt()
        {
            var file = Path.Combine(Dir, "look.txt");
            var cam = UnityEngine.Object.FindFirstObjectByType<Farmverse.Presentation.World.Island.IslandCamera>();
            if (cam == null || !File.Exists(file)) { Debug.LogWarning("[ClaudeRunner] no camera or look.txt"); return; }
            var want = File.ReadAllText(file).Trim();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.Contains(want)) { cam.FocusOn(t.position); cam.Follow = t; cam.Distance = 18f; Debug.Log($"[ClaudeRunner] looking at {t.name} {t.position}"); return; }
            Debug.LogWarning("[ClaudeRunner] nothing called " + want);
        }

        /// <summary>Play mode: add to an island stat: run/stat.txt holds "name amount" (e.g. "raids_seen 6").</summary>
        public static void AddStat()
        {
            var host = UnityEngine.Object.FindFirstObjectByType<Farmverse.Presentation.World.Island.IslandEconomyHost>();
            var file = Path.Combine(Dir, "stat.txt");
            if (host == null || !File.Exists(file)) { Debug.LogWarning("[ClaudeRunner] no host or stat.txt"); return; }
            var parts = File.ReadAllText(file).Trim().Split(' ');
            host.Economy.Count(parts[0], parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 1);
            Debug.Log($"[ClaudeRunner] {parts[0]} now {host.Economy.Stat(parts[0])}");
        }

        public static void Screenshot()
        {
            var path = Path.Combine(Dir, "game.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[ClaudeRunner] screenshot requested: " + path);
        }

        /// <summary>Renders every tool, good and cart prefab (with its origin marked: red ball, green +Y, blue +Z) to Claude outputs/stage/items.</summary>
        public static void ItemSheet()
        {
            var outDir = Path.GetFullPath(Path.Combine(Dir, "..", "stage", "items"));
            Directory.CreateDirectory(outDir);
            var rig = IslandPictureMaker.Rig();
            try
            {
                foreach (var folder in new[] { IthappyItems.Tools, IthappyItems.Goods, IthappyItems.Carts })
                    foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        var holder = new GameObject(pf.name);
                        try
                        {
                            var inst = (GameObject)PrefabUtility.InstantiatePrefab(pf, holder.transform);
                            var b = IthappyArt.WorldBounds(inst);
                            var k = Mathf.Max(b.size.x, b.size.y, b.size.z);
                            if (pf.name == "Axe") Debug.Log("[Items] axe mesh asset: " + AssetDatabase.LoadAssetAtPath<Mesh>(IthappyItems.Tools + "/Axe_mesh.asset") + " all: " + string.Join(",", AssetDatabase.LoadAllAssetsAtPath(IthappyItems.Tools + "/Axe_mesh.asset").Select(o => o == null ? "null" : o.GetType().Name)));
                            if (pf.name == "Axe") Debug.Log($"[Items] axe bounds {b} renderers {inst.GetComponentsInChildren<Renderer>().Length} mesh {inst.GetComponentInChildren<MeshFilter>()?.sharedMesh?.vertexCount} mat {inst.GetComponentInChildren<MeshRenderer>()?.sharedMaterial?.shader?.name}");
                            void Mark(PrimitiveType t, Vector3 at, Vector3 size, Color c)
                            {
                                var m = GameObject.CreatePrimitive(t);
                                UnityEngine.Object.DestroyImmediate(m.GetComponent<Collider>());
                                m.transform.SetParent(holder.transform, false);
                                m.transform.localPosition = at; m.transform.localScale = size;
                                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mat.SetColor("_BaseColor", c);
                                m.GetComponent<Renderer>().sharedMaterial = mat;
                            }
                            if (folder == IthappyItems.Tools || folder == IthappyItems.Carts)
                            {
                                Mark(PrimitiveType.Sphere, Vector3.zero, Vector3.one * k * 0.08f, Color.red);
                                Mark(PrimitiveType.Cube, new Vector3(0, k * 0.2f, 0), new Vector3(k * 0.02f, k * 0.4f, k * 0.02f), Color.green);
                                Mark(PrimitiveType.Cube, new Vector3(0, 0, k * 0.2f), new Vector3(k * 0.02f, k * 0.02f, k * 0.4f), Color.blue);
                            }
                            var png = IslandPictureMaker.Render(rig.cam, holder, false);
                            if (png != null) File.WriteAllBytes(Path.Combine(outDir, pf.name + ".png"), png);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(holder); }
                    }
            }
            finally { UnityEngine.Object.DestroyImmediate(rig.root); }
            Debug.Log("[ClaudeRunner] item sheet in " + outDir);
        }

        /// <summary>
        /// Play mode: a line of farm workers at run/lineup.txt "x z" (default 0 -30), each holding one tool and
        /// working with it, then a worker with a barrow of goods, one with a cart and a townsperson with a mower.
        /// </summary>
        public static void ToolLineup()
        {
            var file = Path.Combine(Dir, "lineup.txt");
            float x0 = 0f, z0 = -30f;
            if (File.Exists(file)) { var f = File.ReadAllText(file).Split(' '); x0 = float.Parse(f[0]); z0 = float.Parse(f[1]); }
            var worker = AssetDatabase.LoadAssetAtPath<GameObject>(IthappyCharacters.People + "/char_farm_worker.prefab");
            var items = ItemCatalog.Load();
            if (worker == null || items == null) { Debug.LogWarning("[Lineup] missing worker or items"); return; }
            var old = GameObject.Find("Tool lineup"); if (old != null) UnityEngine.Object.Destroy(old);
            var root = new GameObject("Tool lineup");
            var anims = new Dictionary<string, string> { ["Axe"] = "Chop", ["Pickaxe"] = "Chop", ["Hoe"] = "Chop", ["Shovel"] = "Chop", ["Rake"] = "Dig", ["GardenShears"] = "Swing", ["Saw"] = "Swing", ["Bucket"] = "Lift", ["Wrench"] = "Machine", ["PipeWrench"] = "Machine", ["Crowbar"] = "Machine", ["FryingPan"] = "Lift" };
            var i = 0;
            GameObject Man(float x, float z, string state)
            {
                var y = Physics.Raycast(new Vector3(x, 200f, z), Vector3.down, out var h, 400f) ? h.point.y : 1.8f;
                var go = UnityEngine.Object.Instantiate(worker, new Vector3(x, y, z), Quaternion.Euler(0, 180f, 0), root.transform);
                foreach (var c in go.GetComponentsInChildren<Collider>()) c.enabled = false;
                var m = go.GetComponent<CharacterMotion>(); if (m != null) m.enabled = false;
                var a = go.GetComponentInChildren<Animator>(); if (a != null) a.Play(state, 0, 0.3f);
                return go;
            }
            foreach (var e in items.tools)
            {
                if (e.prefab == null) continue;
                var go = Man(x0 + (i % 9) * 2.4f, z0 - (i / 9) * 3.2f, anims.TryGetValue(e.id, out var st) ? st : "Idle");
                HandTool.Hold(go, e.prefab);
                i++;
            }
            var z1 = z0 - 7f;
            var w = Man(x0, z1, "Carry");
            if (items.barrows.Count > 0)
            {
                var b = HandTool.Push(w.transform, items.barrows[0], false, 0.35f);
                var bb = new Bounds(b.transform.position, Vector3.zero); foreach (var r in b.GetComponentsInChildren<Renderer>()) bb.Encapsulate(r.bounds);
                var k = 0;
                foreach (var g in new[] { "wheat", "pumpkin", "cabbage", "egg", "milk", "wood" })
                {
                    var pf = items.Good(g); if (pf == null) continue;
                    var gi = UnityEngine.Object.Instantiate(pf, b.transform);
                    gi.transform.position = new Vector3(bb.center.x, bb.max.y - bb.size.y * 0.25f, bb.center.z) + new Vector3((k % 2 - 0.5f) * 0.26f, (k / 2) * 0.2f, 0);
                    k++;
                }
            }
            var c2 = Man(x0 + 3f, z1, "Carry");
            if (items.carts.Count > 0) HandTool.Push(c2.transform, items.carts[2 % items.carts.Count], true, 0.45f);
            var m2 = Man(x0 + 6f, z1, "Carry");
            if (items.mowers.Count > 0) HandTool.Push(m2.transform, items.mowers[0], false, 0.3f);
            var h2 = Man(x0 + 9f, z1, "Carry");
            var pile = new GameObject("Load").transform; pile.SetParent(h2.transform, false); pile.localPosition = new Vector3(0, 0.85f, 0.32f);
            foreach (var g in new[] { "egg", "milk" }) { var pf = items.Good(g); if (pf != null) { var gi = UnityEngine.Object.Instantiate(pf, pile); gi.transform.localPosition = new Vector3(g == "egg" ? -0.13f : 0.13f, 0, 0); gi.transform.localScale *= 0.85f; } }
            Debug.Log($"[Lineup] {i} tools at {x0},{z0}");
        }

        /// <summary>Force-reimport the assets or folders listed in run/reimport.txt (stale references after an asset was remade).</summary>
        public static void Reimport()
        {
            var file = Path.Combine(Dir, "reimport.txt");
            if (!File.Exists(file)) return;
            foreach (var line in File.ReadAllLines(file))
            {
                var path = line.Trim(); if (path.Length == 0) continue;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[ClaudeRunner] reimported " + path);
            }
        }

        /// <summary>Moves the assets listed in run/trash.txt (one project path a line) to the OS recycle bin.</summary>
        public static void Trash()
        {
            var file = Path.Combine(Dir, "trash.txt");
            if (!File.Exists(file)) return;
            var paths = File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && AssetDatabase.IsValidFolder(l) || File.Exists(l)).ToArray();
            var failed = new List<string>();
            AssetDatabase.MoveAssetsToTrash(paths, failed);
            Debug.Log($"[ClaudeRunner] trashed {paths.Length - failed.Count} of {paths.Length}" + (failed.Count > 0 ? ": failed " + string.Join(", ", failed) : ""));
        }

        /// <summary>Play mode: log every Animator in the scene (name, parent, position, distance to the Player) to log.txt.</summary>
        public static void People()
        {
            var player = GameObject.Find("Player");
            var at = player != null ? player.transform.position : Vector3.zero;
            foreach (var a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var t = a.transform;
                var root = t.root;
                Debug.Log($"[People] {t.name} root={root.name} parent={(t.parent != null ? t.parent.name : "-")} active={a.gameObject.activeInHierarchy} pos={t.position} d={(t.position - at).magnitude:F1} layer={a.gameObject.layer} ctrl={(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "NONE")} human={a.isHuman} state={(a.runtimeAnimatorController != null ? a.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name).FirstOrDefault() : "")}");
            }
        }

        /// <summary>
        /// Edit mode (the Island scene open): render views of the island to PNGs without playing. run/shot.txt holds one
        /// view a line: "name x z yaw pitch distance [width height]" — the camera looks at ground point (x, z) from that
        /// bearing/pitch/distance with the scene's own sun, fog, sky and post volume. Output: Claude outputs/stage/shots/name.png.
        /// </summary>
        public static void Shots()
        {
            var file = Path.Combine(Dir, "shot.txt");
            if (!File.Exists(file)) { Debug.LogWarning("[ClaudeRunner] no shot.txt"); return; }
            var outDir = Path.GetFullPath(Path.Combine(Dir, "..", "stage", "shots"));
            Directory.CreateDirectory(outDir);
            var main = Camera.main;
            var go = new GameObject("Shot camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = main != null ? main.fieldOfView : 35f;
            cam.nearClipPlane = 0.5f; cam.farClipPlane = 3000f;
            cam.clearFlags = main != null ? main.clearFlags : CameraClearFlags.Skybox;
            cam.backgroundColor = RenderSettings.fogColor;
            cam.allowMSAA = true; cam.allowHDR = true;
            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = main != null && main.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(out var md) && md.renderPostProcessing;
            try
            {
                foreach (var raw in File.ReadAllLines(file))
                {
                    var f = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 6) continue;
                    var name = f[0];
                    float x, z;
                    if (f[1].StartsWith("@"))
                    {
                        // "@name index": the index-th scene object whose name contains name (people move: follow them)
                        var want = f[1].Substring(1); var nth = int.Parse(f[2]);
                        var hits = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.Contains(want)).OrderBy(t => t.position.x).ToList();
                        if (hits.Count == 0) { Debug.LogWarning("[ClaudeRunner] no " + want); continue; }
                        var t0 = hits[nth % hits.Count].position; x = t0.x; z = t0.z;
                    }
                    else { x = float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture); z = float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture); }
                    float yaw = float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture), pitch = float.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture), dist = float.Parse(f[5], System.Globalization.CultureInfo.InvariantCulture);
                    int w = f.Length > 6 ? int.Parse(f[6]) : 1600, h = f.Length > 7 ? int.Parse(f[7]) : 1000;
                    var at = new Vector3(x, 0, z);
                    var hit = Physics.Raycast(at + Vector3.up * 400f, Vector3.down, out var rh, 800f) ? rh.point.y : 1.8f;
                    if (f[1].StartsWith("@")) hit = 1.8f + 0.9f;
                    at.y = hit;
                    var rot = Quaternion.Euler(pitch, yaw, 0);
                    go.transform.rotation = rot;
                    go.transform.position = at - rot * Vector3.forward * dist;
                    var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                    cam.targetTexture = rt;
                    cam.Render();
                    var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                    RenderTexture.active = prev;
                    var png = Path.Combine(outDir, name + ".png");
                    File.WriteAllBytes(png, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex); cam.targetTexture = null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                    Debug.Log("[ClaudeRunner] shot " + png);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
