#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>Where a screen goes: the base screen (home, play, visit, loading), a full page over it, or a popup over both.</summary>
    public enum UiLayer { Base, Page, Popup }

    /// <summary>
    /// What a screen builds with: its root (a full-screen box), the design units for this device (points
    /// <see cref="P"/> for the game screens, the 2560×1440 reference <see cref="R"/> for the pack popups),
    /// the arguments it was opened with, live updates and rebuild triggers.
    /// </summary>
    public sealed class Ctx
    {
        public readonly GameUi Ui;
        public readonly string Id;
        public readonly VisualElement Root;
        /// <summary>Base screens only: a layer under the HUD for labels pinned to the world (null otherwise).</summary>
        public readonly VisualElement? World;
        public readonly Pt P;
        public readonly Ref R;
        public readonly object? Args;
        internal readonly List<Action> Lives = new List<Action>();
        internal readonly List<Func<object?>> Keys = new List<Func<object?>>();
        internal readonly List<object?> Last = new List<object?>();

        internal Ctx(GameUi ui, string id, VisualElement root, VisualElement? world, Pt p, Ref r, object? args)
        {
            Ui = ui;
            Id = id;
            Root = root;
            World = world;
            P = p;
            R = r;
            Args = args;
        }

        /// <summary>Runs now and then every frame while the screen is up (keep it cheap: set texts, widths).</summary>
        public void Live(Action update)
        {
            update();
            Lives.Add(update);
        }

        /// <summary>The screen is rebuilt whenever this value changes (checked every frame).</summary>
        public void Watch(Func<object?> key)
        {
            Keys.Add(key);
            Last.Add(key());
        }
    }

    /// <summary>
    /// The game's screen, built from scratch to the design (UI Toolkit, the Layer Lab pack's art, the four
    /// colour themes). Layers, bottom to top: world labels, the base screen (home / play / visit / loading),
    /// the top page (shop, storage, …), the top popup behind a dark veil, and the toast.
    /// Screens register themselves (<see cref="Register"/>) from their own partial files and are rebuilt
    /// when the screen changes size, the theme changes, the navigation changes or a watched value changes.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed partial class GameUi : MonoBehaviour
    {
        [SerializeField, Tooltip("The island being played (its economy and life feed every screen).")] private IslandSandbox? sandbox;
        [SerializeField, Tooltip("The base screen shown first.")] private string startScreen = "loading";

        // ------------------------------------------------------------------ registry

        private sealed class Entry
        {
            public UiLayer Layer;
            public Action<GameUi, Ctx> Build = null!;
            public float PhoneK;
            public bool Covers;
        }

        private static Dictionary<string, Entry>? _registry;

        // An explicit static constructor makes the type initialise precisely (not "before field init"): every
        // screen file's Register(...) field runs before anything reads the registry, so none is lost.
        static GameUi() { }
        private static Dictionary<string, Entry> Registry => _registry ??= new Dictionary<string, Entry>();

        /// <summary>
        /// Adds a screen. Call it from a static field in the screen's partial file:
        /// <c>private static readonly bool RegShop = Register("shop", UiLayer.Page, (ui, c) =&gt; ui.Shop(c));</c>
        /// <paramref name="phoneK"/> is the design's phone scale (points × k); <paramref name="covers"/>: a base
        /// screen that hides the island (home, loading) so presses never reach the world.
        /// </summary>
        private static bool Register(string id, UiLayer layer, Action<GameUi, Ctx> build, float phoneK = .55f, bool covers = false)
        {
            Registry[id] = new Entry { Layer = layer, Build = build, PhoneK = phoneK, Covers = covers };
            return true;
        }

        public static bool Has(string id) => Registry.ContainsKey(id);

        /// <summary>Popups drawn over the live game with no dark veil (the island growing shows through).</summary>
        private static readonly System.Collections.Generic.HashSet<string> Unveiled = new System.Collections.Generic.HashSet<string> { "growtour" };

        // ------------------------------------------------------------------ state

        private UIDocument? _doc;
        private VisualElement? _root, _world, _base, _page, _pop, _toastLayer;
        private Vector2 _size;
        private string _baseId = "play";
        private object? _baseArgs;
        private readonly List<(string id, object? args)> _pages = new List<(string, object?)>();
        private readonly List<(string id, object? args)> _popups = new List<(string, object?)>();
        private readonly List<Ctx> _live = new List<Ctx>();
        private bool _dirty;

        public IslandSandbox? Sandbox => sandbox;
        public IslandEconomyHost? Host => sandbox != null ? sandbox.Host : null;
        public IslandEconomy? Economy => Host != null ? Host.Economy : null;
        public IslandLife? Life => Host != null ? Host.Life : null;

        /// <summary>The base screen (loading, home, play, visit).</summary>
        public string BaseScreen => _baseId;
        public string? TopPage => _pages.Count > 0 ? _pages[_pages.Count - 1].id : null;
        public string? TopPopup => _popups.Count > 0 ? _popups[_popups.Count - 1].id : null;

        /// <summary>The island is behind a page, a popup or a covering base screen: presses don't reach it.</summary>
        public bool Covering => _pages.Count > 0 || _popups.Count > 0 || (Registry.TryGetValue(_baseId, out var e) && e.Covers);

        private void OnEnable()
        {
            if (sandbox == null) sandbox = FindAnyObjectByType<IslandSandbox>();
            IslandBoosts.Running = ShopBoostRunning;
            IslandLife.Words = Loc.Of;
            PK.HeaderFarm = FarmPill;
            Loc.Use(LanguageIso);
            _doc = GetComponent<UIDocument>();
            _root = _doc.rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            Fill(_root);
            _root.Clear();
            _world = Layer("game-world");
            _base = Layer("game-base");
            _page = Layer("game-page");
            _pop = Layer("game-popup");
            _toastLayer = Layer("game-toast");
            _toast = null;
            _root.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            GameTheme.Changed += MarkDirty;
            Wallet.ShopChanged += MarkDirty;
            IslandHud.GameCovering = () => Covering;
            IslandHud.OverGame = Over;
            _baseId = Registry.ContainsKey(startScreen) ? startScreen : "play";
            Rebuild();
        }

        private void OnDisable()
        {
            _root?.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            GameTheme.Changed -= MarkDirty;
            Wallet.ShopChanged -= MarkDirty;
            IslandHud.GameCovering = null;
            IslandHud.OverGame = null;
            _live.Clear();
        }

        private void Update()
        {
            DragTick();
            BoostSync();
            LinkTick();
            CrewTick();
            SystemTick();
            RouteTick();
            TrainTick();
            ReferralTick();
            ProfileTick();
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && (_popups.Count > 0 || _pages.Count > 0)) Back();

            if (!_dirty)
                foreach (var c in _live)
                {
                    for (var i = 0; i < c.Keys.Count && !_dirty; i++)
                        if (!Equals(c.Keys[i](), c.Last[i])) _dirty = true;
                    if (_dirty) break;
                }

            if (_dirty)
            {
                _dirty = false;
                Rebuild();
                return;
            }

            foreach (var c in _live)
                foreach (var a in c.Lives)
                    try { a(); }
                    catch (Exception ex) { Debug.LogException(ex); }
        }

        private static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0;
            e.style.top = 0;
            e.style.right = 0;
            e.style.bottom = 0;
        }

        private VisualElement Layer(string name)
        {
            var e = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            Fill(e);
            _root!.Add(e);
            return e;
        }

        private void OnGeometry(GeometryChangedEvent e)
        {
            var s = new Vector2(e.newRect.width, e.newRect.height);
            if ((s - _size).sqrMagnitude < .25f) return;
            _size = s;
            Rebuild();
        }

        // ------------------------------------------------------------------ navigation

        /// <summary>Changes the base screen (loading, home, play, visit); closes pages and popups.</summary>
        public void Go(string id, object? args = null)
        {
            // leaving another player's island (any way out): this island comes back
            if (_baseId == "visit" && id != "visit" && id != "visittravel") Sandbox?.EndVisit();
            _baseId = id;
            _baseArgs = args;
            _pages.Clear();
            _popups.Clear();
            MarkDirty();
        }

        /// <summary>Opens a page or popup on top (its layer comes from its registration).</summary>
        public void Open(string id, object? args = null)
        {
            if (!Registry.TryGetValue(id, out var e))
            {
                Debug.LogWarning("[GameUi] no screen called " + id);
                return;
            }

            switch (e.Layer)
            {
                case UiLayer.Base: Go(id, args); return;
                case UiLayer.Page:
                    _popups.Clear();
                    _pages.RemoveAll(p => p.id == id);
                    _pages.Add((id, args));
                    break;
                default:
                    _popups.Add((id, args));
                    break;
            }

            MarkDirty();
        }

        /// <summary>Swaps the top popup (or page) for another, without stacking.</summary>
        public void Replace(string id, object? args = null)
        {
            if (Registry.TryGetValue(id, out var e) && e.Layer == UiLayer.Popup && _popups.Count > 0) _popups.RemoveAt(_popups.Count - 1);
            else if (e != null && e.Layer == UiLayer.Page && _pages.Count > 0) _pages.RemoveAt(_pages.Count - 1);
            Open(id, args);
        }

        /// <summary>Closes the top popup, else the top page.</summary>
        public void Back()
        {
            if (_popups.Count > 0) _popups.RemoveAt(_popups.Count - 1);
            else if (_pages.Count > 0) _pages.RemoveAt(_pages.Count - 1);
            MarkDirty();
        }

        /// <summary>Closes every popup (the pages stay).</summary>
        public void ClosePopups()
        {
            _popups.Clear();
            MarkDirty();
        }

        /// <summary>Closes every page and popup, back to the base screen.</summary>
        public void CloseAll()
        {
            _pages.Clear();
            _popups.Clear();
            MarkDirty();
        }

        /// <summary>Rebuilds at the end of this frame (cheap to call often).</summary>
        public void MarkDirty() => _dirty = true;

        private void Rebuild()
        {
            _dirty = false;
            TapGuard.Serial++;
            FitLooked.Clear();
            FitShift.Clear();
            if (_root == null || _base == null || _page == null || _pop == null || _world == null) return;
            var w = _root.layout.width;
            var h = _root.layout.height;
            _world.Clear();
            _base.Clear();
            _page.Clear();
            _pop.Clear();
            _live.Clear();
            if (float.IsNaN(w) || float.IsNaN(h) || w < 10 || h < 10) return;
            _size = new Vector2(w, h);
            var r = new Ref(w, h);

            var baseHolder = Build(_baseId, _baseArgs, _base, _world, r);
            if (baseHolder != null && _shownBase != _baseId) Kit.Enter(baseHolder, "base", w);
            _shownBase = _baseId;
            string? pageId = null;
            if (_pages.Count > 0)
            {
                var (id, args) = _pages[_pages.Count - 1];
                pageId = id;
                var holder = Build(id, args, _page, null, r);
                if (holder != null && _shownPage != id) Kit.Enter(holder, "page", w);
            }
            _shownPage = pageId;

            var bare = false;
            if (_popups.Count > 0)
            {
                var (id, args) = _popups[_popups.Count - 1];
                bare = Unveiled.Contains(id);
                if (!bare) Kit.Veil(_pop);
                var holder = Build(id, args, _pop, null, r);
                if (_shownPopup != id)
                {
                    if (!bare && _pop.childCount > 0) Kit.Enter(_pop[0], "base", w);   // the veil fades in
                    if (holder != null) Kit.Enter(holder, "popup", w);
                }
                _shownPopup = id;
            }
            else _shownPopup = null;
            // over the island growing, the HUD steps aside too
            _base.style.display = bare ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // what each layer showed last time: a screen animates in only when it's new
        private string? _shownBase, _shownPage, _shownPopup;

        private VisualElement? Build(string id, object? args, VisualElement layer, VisualElement? world, Ref r)
        {
            if (!Registry.TryGetValue(id, out var e))
            {
                Debug.LogWarning("[GameUi] no screen called " + id);
                return null;
            }

            var holder = new VisualElement { name = "screen-" + id, pickingMode = PickingMode.Ignore };
            Fill(holder);
            layer.Add(holder);
            var c = new Ctx(this, id, holder, world, new Pt(r.W, r.H, e.PhoneK), r, args);
            try { e.Build(this, c); }
            catch (Exception ex) { Debug.LogException(ex); }
            _live.Add(c);
            if (e.Layer == UiLayer.Popup || DialogPages.Contains(id)) FitDialogs(holder, r.H, r.W - c.P.L - c.P.R);
            else if (c.P.T > .5f) SafeTop(holder, c.P.T);
            return holder;
        }

        /// <summary>
        /// The top safe inset (a camera cutout or status bar above the screen): every piece of a base screen or
        /// page that starts inside it (top bars, header buttons) moves down by the inset. Full-screen pieces
        /// (backgrounds, veils) still fill the screen; the pieces inside a full-screen wrapper are checked too.
        /// Left, right and bottom insets are applied by each screen's own layout (Pt.L, Pt.R, Pt.B).
        /// </summary>
        private static void SafeTop(VisualElement holder, float top, int depth = 0)
        {
            foreach (var child in holder.Children())
            {
                var b = child;
                var done = false;

                void Check()
                {
                    if (done || b.parent == null) return;
                    var rect = b.layout;
                    if (float.IsNaN(rect.height) || rect.height <= 0) return;
                    done = true;
                    var full = rect.height >= b.parent.layout.height - 1f;
                    if (full)
                    {
                        if (depth < 2) SafeTop(b, top, depth + 1);
                        return;
                    }

                    if (rect.y < top) b.style.marginTop = b.resolvedStyle.marginTop + top;
                }

                b.RegisterCallback<GeometryChangedEvent>(_ => Check());
                b.schedule.Execute(Check);   // in case it was laid out already
            }
        }

        /// <summary>Pages drawn as a dialog over a veil (their box obeys the dialog height limit too (92vh)).</summary>
        private static readonly HashSet<string> DialogPages = new HashSet<string> { "inbox" };

        /// <summary>A dialog is never taller than this share of the screen (92vh).</summary>
        public const float DialogMaxHeight = .92f;

        /// <summary>
        /// Keeps every dialog box within 92% of the screen height: a taller one is scaled down about its
        /// centre (its layout and proportions stay the design's). Full-screen pieces (veils) are left alone.
        /// </summary>
        private static void FitDialogs(VisualElement holder, float screenH, float safeW, int depth = 0)
        {
            foreach (var box in holder.Children())
            {
                var b = box;
                b.RegisterCallback<GeometryChangedEvent>(_ => FitDialog(b, screenH, safeW, depth));
                b.schedule.Execute(() => FitDialog(b, screenH, safeW, depth));   // in case it was laid out already
            }
        }

        private static readonly HashSet<VisualElement> FitLooked = new HashSet<VisualElement>();
        private static readonly Dictionary<VisualElement, float> FitShift = new Dictionary<VisualElement, float>();

        private static void FitDialog(VisualElement b, float screenH, float safeW, int depth)
        {
            var rect = b.layout;
            var parent = b.parent;
            if (parent == null || float.IsNaN(rect.height) || rect.height <= 0) return;
            var full = rect.width >= parent.layout.width - 1f && rect.height >= parent.layout.height - 1f;
            if (full)
            {
                // a full-screen wrapper (veil, centring box): the dialog is inside it
                if (depth < 2 && FitLooked.Add(b)) FitDialogs(b, screenH, safeW, depth + 1);

                return;
            }

            // what sticks out of the box (a title ribbon, the close button) counts as part of the dialog:
            // it is fitted with the box and the whole thing is centred, so the ribbon doesn't push it off-centre
            var now = b.resolvedStyle.scale.value.x;
            if (now <= 0f) now = 1f;
            var wb = b.worldBound;
            float top = wb.yMin, bottom = wb.yMax;
            foreach (var kid in b.Children())
            {
                if (kid.resolvedStyle.display == DisplayStyle.None) continue;
                var kb = kid.worldBound;
                if (kb.width <= 0 || kb.height <= 0) continue;
                top = Mathf.Min(top, kb.yMin);
                bottom = Mathf.Max(bottom, kb.yMax);
            }

            var overTop = (wb.yMin - top) / now;          // in the dialog's own (unscaled) units
            var overBottom = (bottom - wb.yMax) / now;
            var visual = rect.height + overTop + overBottom;

            var max = screenH * DialogMaxHeight;
            var k = visual <= max ? 1f : max / visual;
            if (rect.width > safeW && safeW > 0) k = Mathf.Min(k, safeW / rect.width);   // clear of the notch too
            if (Mathf.Abs(now - k) > .001f) b.style.scale = new Scale(new Vector3(k, k, 1f));

            // move the box down (or up) by half the difference so the whole visual is centred
            var shift = (overTop - overBottom) / 2f * k;
            var had = FitShift.TryGetValue(b, out var old) ? old : 0f;
            if (Mathf.Abs(shift - had) > .5f)
            {
                FitShift[b] = shift;
                b.style.marginTop = b.resolvedStyle.marginTop - had + shift;
            }
        }

        /// <summary>Is this screen point (pixels, y up) over one of our buttons or panels?</summary>
        private bool Over(Vector2 screen)
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            var at = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
            var picked = panel.Pick(at);
            for (var e = picked; e != null; e = e.parent)
                if (e == _root) return picked!.pickingMode == PickingMode.Position && picked.resolvedStyle.display != DisplayStyle.None;
            return false;
        }

        // ------------------------------------------------------------------ toast

        private Label? _toast;
        private IVisualElementScheduledItem? _toastHide;

        /// <summary>Toast lines longer than this read better as a dialog (<see cref="Tell"/>).</summary>
        private const int LongSay = 72;

        /// <summary>A message too long for the toast: a dialog with a title, the text and OK.</summary>
        public void Tell(string title, string text)
        {
            var args = new HomeMessageArgs { Title = title, Text = text };
            // a second message replaces the one showing instead of stacking up
            if (_popups.Count > 0 && _popups[_popups.Count - 1].id == "message") Replace("message", args);
            else Open("message", args);
        }

        /// <summary>
        /// A line over everything (the pack's dark pill), gone after a moment. Never a dialog: play goes on while it
        /// shows (a long line wraps onto two and stays a little longer). Dialogs are only for <see cref="Tell"/>.
        /// </summary>
        public void Say(string text, float seconds = 2.2f)
        {
            if (text.Length > LongSay) seconds = Mathf.Max(seconds, 3.5f);
            if (_toastLayer == null) return;
            if (_toast == null)
            {
                _toast = new Label { pickingMode = PickingMode.Ignore };
                _toast.style.alignSelf = Align.Center;
                _toastLayer.style.alignItems = Align.Center;
                _toastLayer.Add(_toast);
            }

            var p = new Pt(_size.x, _size.y, .55f);
            var fs = Mathf.Max(12f, p.P(26));
            var font = GameArt.Display;
            if (font != null) _toast.style.unityFontDefinition = FontDefinition.FromFont(font);
            _toast.text = Loc.Of(text);
            _toast.style.backgroundColor = GameTheme.T("#061e46", .95f);
            _toast.style.marginTop = _size.y * .16f;
            _toast.style.fontSize = fs;
            _toast.style.color = Color.white;
            _toast.style.unityTextOutlineWidth = Mathf.Max(1f, fs * .12f);
            _toast.style.unityTextOutlineColor = GameTheme.T("#0b1d3a");
            _toast.style.paddingLeft = fs * 1.1f;
            _toast.style.paddingRight = fs * 1.1f;
            _toast.style.paddingTop = fs * .4f;
            _toast.style.paddingBottom = fs * .4f;
            _toast.style.whiteSpace = WhiteSpace.Normal;          // a long line wraps
            _toast.style.maxWidth = _size.x * .62f;
            _toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            Kit.Round(_toast, fs * 2f);
            Kit.Border(_toast, Mathf.Max(1f, p.U(3)), GameTheme.T("#0b1d3a"));
            _toast.style.display = DisplayStyle.Flex;
            _toastHide?.Pause();
            _toastHide = _toast.schedule.Execute(() => { if (_toast != null) _toast.style.display = DisplayStyle.None; });
            _toastHide.ExecuteLater((long)(seconds * 1000));
        }
    }

    /// <summary>
    /// The design's points for the game screens: the tablet frame is 1366 pt wide (k = 1); the phone frame
    /// 844 pt wide drawn at scale k (0.5–0.55 by screen). <see cref="P"/> converts design numbers that were
    /// scaled by k; <see cref="U"/> converts literal pixel numbers of the design. Insets are the device's
    /// safe area in panel pixels (the phone design assumed 47 pt left/right and 21 pt at the bottom).
    /// </summary>
    public sealed class Pt
    {
        public readonly float W, H, Unit, K, L, R, B, T;
        public readonly bool Phone;

        public Pt(float w, float h, float phoneK)
        {
            W = w;
            H = h;
            Phone = w / h > 1.6f;
            // the phone frame is 844 × 390 (landscape): fit it to whichever runs out first, so a screen wider than the
            // frame (an ultrawide monitor, a short browser window) keeps the designed spacing instead of overlapping
            Unit = Phone ? Mathf.Min(w / 844f, h / 390f) : w / 1366f;
            K = Phone ? Unit * phoneK : Unit;
            var safe = Screen.safeArea;
            var px = Screen.width > 0 ? w / Screen.width : 1f;
            // never closer than a small margin to the screen's sides, even where the device reports no inset
            var side = Phone ? Unit * 24f : Unit * 12f;
            L = Mathf.Max(side, safe.xMin * px);
            R = Mathf.Max(side, (Screen.width - safe.xMax) * px);
            B = Mathf.Max(0, safe.yMin * px);
            T = Mathf.Max(0, (Screen.height - safe.yMax) * px);
        }

        /// <summary>A design number scaled by k (the mockups' c.p(n)).</summary>
        public float P(float n) => n * K;

        /// <summary>A literal design pixel (unscaled in the mockups: borders, fixed paddings).</summary>
        public float U(float n) => n * Unit;
    }
}
