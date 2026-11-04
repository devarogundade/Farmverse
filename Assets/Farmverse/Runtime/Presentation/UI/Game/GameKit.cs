#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The screenshots' reference units. Pack popups are laid out on a 2560×1440 page: one unit is
    /// <see cref="S"/> panel pixels, the smaller of width/2560 and height/1440, so a popup keeps its
    /// shape on a 4:3 tablet and a 19.5:9 phone.
    /// </summary>
    public sealed class Ref
    {
        public readonly float W, H, S;

        public Ref(float w, float h)
        {
            W = w;
            H = h;
            S = Mathf.Min(w / 2560f, h / 1440f);
        }

        public float Q(float n) => n * S;
    }

    /// <summary>
    /// Stops one press acting twice: a press that changes the screen rebuilds it, and the same press (its
    /// mouse and touch twins, or the compatibility mouse events) must not then tap the new button that
    /// appeared under the finger. A tap is ignored when it comes within 0.4 s of an earlier tap and its
    /// button was built after that tap.
    /// The guard starts fresh with every run: the Editor's Play without a domain reload keeps statics, while
    /// the clock starts again at 0, and the last run's tap time would block every tap until the clock passed it.
    /// </summary>
    public static class TapGuard
    {
        /// <summary>Raised by every rebuild (GameUi).</summary>
        public static int Serial;
        private static float _lastAt = -10f;
        private static int _lastSerial = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Serial = 0;
            _lastAt = -10f;
            _lastSerial = -1;
        }

        /// <summary>True when a tap on a button built at <paramref name="builtAt"/> should run now.</summary>
        public static bool Allow(int builtAt)
        {
            var now = Time.unscaledTime;
            if (now < _lastAt) _lastAt = -10f;   // the clock went back (a new run): the old tap means nothing
            if (now - _lastAt < .4f && builtAt > _lastSerial) return false;
            _lastAt = now;
            _lastSerial = Serial;
            return true;
        }
    }

    /// <summary>
    /// Building blocks with the pack's look: absolutely placed boxes, pictures (stretched, contained or
    /// nine-sliced), Lilita text with the black outline and drop shadow, pressable buttons, the navy
    /// popup, the switch and the tick slider. Everything is placed in reference units (<see cref="Ref"/>).
    /// </summary>
    public static class Kit
    {
        // ------------------------------------------------------------------ boxes

        /// <summary>A box at (x, y) of size w×h (reference units) inside its parent. Clicks pass through it.</summary>
        public static VisualElement Box(VisualElement parent, Ref r, float x, float y, float w, float h)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            Place(e, r.Q(x), r.Q(y), r.Q(w), r.Q(h));
            parent.Add(e);
            return e;
        }

        /// <summary>A box covering its parent, inset by l, t, r, b (reference units).</summary>
        public static VisualElement Fill(VisualElement parent, Ref r, float l = 0, float t = 0, float rt = 0, float b = 0)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = r.Q(l);
            e.style.top = r.Q(t);
            e.style.right = r.Q(rt);
            e.style.bottom = r.Q(b);
            parent.Add(e);
            return e;
        }

        /// <summary>Absolute position and size in panel pixels.</summary>
        public static void Place(VisualElement e, float left, float top, float width, float height)
        {
            e.style.position = Position.Absolute;
            e.style.left = left;
            e.style.top = top;
            e.style.width = width;
            e.style.height = height;
        }

        public static void Colour(VisualElement e, Color c) => e.style.backgroundColor = c;

        public static void Round(VisualElement e, float px) => Round(e, px, px, px, px);

        /// <summary>
        /// Corner radii (top-left, top-right, bottom-right, bottom-left). Like CSS, radii larger than the box
        /// shrink together so the corners never overlap: a huge radius gives a true capsule or circle
        /// (UI Toolkit on its own would draw an oval).
        /// </summary>
        public static void Round(VisualElement e, float tl, float tr, float br, float bl)
        {
            var info = Radii.GetOrCreateValue(e);
            var first = !info.Set;
            info.Set = true;
            info.TL = tl;
            info.TR = tr;
            info.BR = br;
            info.BL = bl;
            RoundFit(e, info);
            if (first) e.RegisterCallback<GeometryChangedEvent>(_ => RoundFit(e, info));
        }

        private sealed class RadiusInfo
        {
            public bool Set;
            public float TL, TR, BR, BL;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, RadiusInfo> Radii =
            new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, RadiusInfo>();

        private static void RoundFit(VisualElement e, RadiusInfo r)
        {
            var w = e.layout.width;
            var h = e.layout.height;
            var f = 1f;
            if (!float.IsNaN(w) && !float.IsNaN(h) && w > 0 && h > 0)
            {
                // CSS: scale every radius by the smallest side / (sum of the two radii on that side)
                if (r.TL + r.TR > 0) f = Mathf.Min(f, w / (r.TL + r.TR));
                if (r.BL + r.BR > 0) f = Mathf.Min(f, w / (r.BL + r.BR));
                if (r.TL + r.BL > 0) f = Mathf.Min(f, h / (r.TL + r.BL));
                if (r.TR + r.BR > 0) f = Mathf.Min(f, h / (r.TR + r.BR));
            }

            Set(e, r.TL * f, r.TR * f, r.BR * f, r.BL * f);
        }

        private static void Set(VisualElement e, float tl, float tr, float br, float bl)
        {
            if (Mathf.Abs(e.resolvedStyle.borderTopLeftRadius - tl) > .01f) e.style.borderTopLeftRadius = tl;
            if (Mathf.Abs(e.resolvedStyle.borderTopRightRadius - tr) > .01f) e.style.borderTopRightRadius = tr;
            if (Mathf.Abs(e.resolvedStyle.borderBottomRightRadius - br) > .01f) e.style.borderBottomRightRadius = br;
            if (Mathf.Abs(e.resolvedStyle.borderBottomLeftRadius - bl) > .01f) e.style.borderBottomLeftRadius = bl;
        }

        public static void Border(VisualElement e, float px, Color c)
        {
            e.style.borderLeftWidth = px;
            e.style.borderTopWidth = px;
            e.style.borderRightWidth = px;
            e.style.borderBottomWidth = px;
            e.style.borderLeftColor = c;
            e.style.borderTopColor = c;
            e.style.borderRightColor = c;
            e.style.borderBottomColor = c;
        }

        // ------------------------------------------------------------------ pictures

        /// <summary>The picture stretched over the whole box (CSS "100% 100%").</summary>
        public static void Picture(VisualElement e, Texture2D? t)
        {
            if (t == null) return;
            e.style.backgroundImage = Background.FromTexture2D(t);
            e.style.backgroundSize = new BackgroundSize(new Length(100, LengthUnit.Percent), new Length(100, LengthUnit.Percent));
        }

        /// <summary>The picture fitted inside the box, keeping its shape (CSS "contain").</summary>
        public static void Contain(VisualElement e, Texture2D? t)
        {
            if (t == null) return;
            e.style.backgroundImage = Background.FromTexture2D(t);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
        }

        /// <summary>The picture cropped to fill the box, keeping its shape (CSS "cover").</summary>
        public static void Cover(VisualElement e, Texture2D? t)
        {
            if (t == null) return;
            e.style.backgroundImage = Background.FromTexture2D(t);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
        }

        /// <summary>
        /// A nine-sliced picture (CSS border-image): the slices are in the picture's pixels, drawn
        /// <paramref name="scale"/> panel pixels per picture pixel.
        /// </summary>
        public static void Nine(VisualElement e, Texture2D? t, int left, int top, int right, int bottom, float scale)
        {
            if (t == null) return;
            e.style.backgroundImage = Background.FromTexture2D(t);
            e.style.unitySliceLeft = left;
            e.style.unitySliceTop = top;
            e.style.unitySliceRight = right;
            e.style.unitySliceBottom = bottom;
            var info = NineSizes.GetOrCreateValue(e);
            var first = info.Scale == 0f;
            info.Scale = scale;
            info.H = left + right;
            info.V = top + bottom;
            NineFit(e, info);
            if (first) e.RegisterCallback<GeometryChangedEvent>(_ => NineFit(e, info));
        }

        /// <summary>
        /// The pack's flush buttons (Button_FlushLeft_Gray / Button_FlushRight_Gray: flat on the screen edge, a
        /// slanted rounded end). Their slant runs the full height, so they stretch only sideways: the slanted
        /// end keeps its shape at any height and the flat part grows to the width.
        /// </summary>
        public static void Flush(VisualElement e, bool flatLeft)
        {
            var t = GameArt.Get(flatLeft ? "Button_FlushLeft_Gray" : "Button_FlushRight_Gray");
            if (t == null) return;
            e.style.backgroundImage = Background.FromTexture2D(t);
            e.style.unitySliceTop = 0;
            e.style.unitySliceBottom = 0;
            e.style.unitySliceLeft = flatLeft ? 4 : 30;
            e.style.unitySliceRight = flatLeft ? 30 : 4;

            void Fit()
            {
                var h = e.layout.height;
                if (float.IsNaN(h) || h <= 0) return;
                var k = h / t.height;
                if (Mathf.Abs(e.resolvedStyle.unitySliceScale - k) > .0001f) e.style.unitySliceScale = k;
            }

            e.RegisterCallback<GeometryChangedEvent>(_ => Fit());
            Fit();
        }

        private sealed class NineInfo
        {
            public float Scale;
            public int H, V;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, NineInfo> NineSizes =
            new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, NineInfo>();

        /// <summary>
        /// Like CSS border-image: when the box is smaller than its slices (a short button on a phone), the
        /// slices shrink together so they still fit, instead of overlapping and breaking the picture.
        /// </summary>
        private static void NineFit(VisualElement e, NineInfo info)
        {
            var k = info.Scale;
            var r = e.layout;
            if (!float.IsNaN(r.width) && r.width > 0 && info.H > 0) k = Mathf.Min(k, r.width / info.H);
            if (!float.IsNaN(r.height) && r.height > 0 && info.V > 0) k = Mathf.Min(k, r.height / info.V);
            if (k <= 0f) k = info.Scale;
            if (Mathf.Abs(e.resolvedStyle.unitySliceScale - k) > .0001f) e.style.unitySliceScale = k;
        }

        /// <summary>A picture in its own box at (x, y), w×h, stretched.</summary>
        public static VisualElement Image(VisualElement parent, Ref r, string art, float x, float y, float w, float h, bool contain = true)
        {
            var e = Box(parent, r, x, y, w, h);
            if (contain) Contain(e, GameArt.Get(art)); else Picture(e, GameArt.Get(art));
            return e;
        }

        // ------------------------------------------------------------------ text

        /// <summary>
        /// Pack text: Lilita One, white, a black outline of max(4, size×.075) and a black drop shadow of
        /// size×.07 below. <paramref name="stroke"/> false gives the plain coloured label (no outline, no shadow).
        /// </summary>
        public static Label Text(Ref r, string text, float size, Color? colour = null, bool stroke = true, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var l = new Label(Loc.Of(text)) { pickingMode = PickingMode.Ignore };
            var font = GameArt.Display;
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            l.style.fontSize = r.Q(size);
            l.style.color = colour ?? Color.white;
            l.style.unityTextAlign = align;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            l.style.marginLeft = 0;
            l.style.marginRight = 0;
            l.style.marginTop = 0;
            l.style.marginBottom = 0;
            l.style.paddingLeft = 0;
            l.style.paddingRight = 0;
            l.style.paddingTop = 0;
            l.style.paddingBottom = 0;
            l.style.overflow = Overflow.Visible;
            if (stroke)
            {
                l.style.unityTextOutlineWidth = r.Q(Mathf.Max(4f, size * .075f));
                l.style.unityTextOutlineColor = Color.black;
                l.style.textShadow = new TextShadow { offset = new Vector2(0, r.Q(size * .07f)), blurRadius = 0, color = Color.black };
            }
            return l;
        }

        /// <summary>
        /// Text whose line (one size tall, like the design's line-height 1) starts at (x, y).
        /// Width 0 means it grows to the right; otherwise <paramref name="align"/> places it in that width.
        /// </summary>
        public static Label TextAt(VisualElement parent, Ref r, float x, float y, string text, float size, Color? colour = null, bool stroke = true, float width = 0, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var l = Text(r, text, size, colour, stroke, align);
            l.style.position = Position.Absolute;
            l.style.left = r.Q(x);
            l.style.top = r.Q(y);
            l.style.height = r.Q(size);
            if (width > 0) l.style.width = r.Q(width);
            parent.Add(l);
            return l;
        }

        /// <summary>Text centred in its parent (optionally lifted by <paramref name="lift"/> units).</summary>
        public static Label TextIn(VisualElement parent, Ref r, string text, float size, Color? colour = null, bool stroke = true, float lift = 0)
        {
            var l = Text(r, text, size, colour, stroke, TextAnchor.MiddleCenter);
            l.style.position = Position.Absolute;
            l.style.left = 0;
            l.style.right = 0;
            l.style.top = 0;
            l.style.bottom = r.Q(lift);
            parent.Add(l);
            return l;
        }

        /// <summary>Text across the parent's full width, centred, its line starting at y.</summary>
        public static Label TextRow(VisualElement parent, Ref r, float y, string text, float size, Color? colour = null, bool stroke = true)
        {
            var l = Text(r, text, size, colour, stroke, TextAnchor.MiddleCenter);
            l.style.position = Position.Absolute;
            l.style.left = 0;
            l.style.right = 0;
            l.style.top = r.Q(y);
            l.style.height = r.Q(size);
            parent.Add(l);
            return l;
        }

        // ------------------------------------------------------------------ pressing

        /// <summary>Makes the box a button: it sinks 2 units while held and runs <paramref name="onClick"/> when released over it.</summary>
        public static VisualElement Press(VisualElement e, Ref r, Action onClick, string sound = "pop")
        {
            e.pickingMode = PickingMode.Position;
            var sink = Mathf.Max(1f, r.Q(4));
            Kit.Sink(e, sink);
            var builtAt = TapGuard.Serial;
            e.AddManipulator(new Clickable(() =>
            {
                if (!TapGuard.Allow(builtAt)) return;
                if (!string.IsNullOrEmpty(sound)) Farmverse.Presentation.World.Island.IslandAudio.Play(sound);
                onClick();
            }));
            return e;
        }

        /// <summary>
        /// The pressed look: the button sinks while held. It keeps the element's own translate (centring with
        /// -50%, for example) and only adds the sink, so a centred chip doesn't jump when tapped.
        /// </summary>
        public static void Sink(VisualElement e, float sink)
        {
            var down = false;
            StyleTranslate rest = default;

            void Press()
            {
                if (down) return;
                down = true;
                rest = e.style.translate;
                var t = rest.keyword == StyleKeyword.Undefined ? rest.value : new Translate(0, 0);
                if (t.y.unit == LengthUnit.Pixel) e.style.translate = new Translate(t.x, new Length(t.y.value + sink, LengthUnit.Pixel));
                // a little squeeze while held, eased: the button feels pressed
                Ease(e, 0.07f, EasingMode.EaseOut, "scale");
                e.style.scale = new Scale(new Vector3(0.95f, 0.95f, 1f));
            }

            void Release()
            {
                if (!down) return;
                down = false;
                e.style.translate = rest;
                Ease(e, 0.16f, EasingMode.EaseOutBack, "scale");   // springs back
                e.style.scale = new Scale(Vector3.one);
            }

            e.RegisterCallback<PointerDownEvent>(_ => Press(), TrickleDown.TrickleDown);
            e.RegisterCallback<PointerUpEvent>(_ => Release(), TrickleDown.TrickleDown);
            e.RegisterCallback<PointerLeaveEvent>(_ => Release());
            e.RegisterCallback<PointerCancelEvent>(_ => Release());
        }

        /// <summary>Animate these style properties of <paramref name="e"/> over <paramref name="seconds"/> from now on.</summary>
        public static void Ease(VisualElement e, float seconds, EasingMode easing, params string[] properties)
        {
            var names = new System.Collections.Generic.List<StylePropertyName>();
            var times = new System.Collections.Generic.List<TimeValue>();
            var curves = new System.Collections.Generic.List<EasingFunction>();
            foreach (var pr in properties) { names.Add(new StylePropertyName(pr)); times.Add(new TimeValue(seconds, TimeUnit.Second)); curves.Add(new EasingFunction(easing)); }
            e.style.transitionProperty = names;
            e.style.transitionDuration = times;
            e.style.transitionTimingFunction = curves;
        }

        /// <summary>
        /// A screen coming in: a popup pops up from 90% and fades in (a little overshoot), a page slides in from the
        /// right, a base screen fades. Only when it first opens, not on every rebuild.
        /// </summary>
        public static void Enter(VisualElement e, string kind, float width)
        {
            e.style.opacity = 0f;
            if (kind == "popup") e.style.scale = new Scale(new Vector3(0.9f, 0.9f, 1f));
            else if (kind == "page") e.style.translate = new Translate(width * 0.06f, 0);
            e.schedule.Execute(() =>
            {
                Ease(e, kind == "popup" ? 0.24f : 0.22f, kind == "popup" ? EasingMode.EaseOutBack : EasingMode.EaseOutCubic, "opacity", "scale", "translate");
                e.style.opacity = 1f;
                e.style.scale = new Scale(Vector3.one);
                e.style.translate = new Translate(0, 0);
            }).ExecuteLater(10);
        }

        /// <summary>Children of a list or grid come in one after another (a quick fade and rise each).</summary>
        public static void Stagger(VisualElement container, float rise, int max = 16)
        {
            var i = 0;
            foreach (var child in container.Children())
            {
                if (i >= max) break;
                var c = child;
                c.style.opacity = 0f;
                c.style.translate = new Translate(0, rise);
                var delay = 20 + i * 35;
                c.schedule.Execute(() =>
                {
                    Ease(c, 0.22f, EasingMode.EaseOutCubic, "opacity", "translate");
                    c.style.opacity = 1f;
                    c.style.translate = new Translate(0, 0);
                }).ExecuteLater(delay);
                i++;
            }
        }

        // ------------------------------------------------------------------ pack parts

        /// <summary>Button01_195 in a colour (Blue, BlueGray, Green): the pill button, nine-sliced 96/30/96/30.</summary>
        public static VisualElement Button(VisualElement parent, Ref r, string colour, float x, float y, float w, float h, Action? onClick, string? label = null, float size = 54)
        {
            var b = Box(parent, r, x, y, w, h);
            Nine(b, GameArt.Get("Button01_195_" + colour), 30, 96, 30, 96, r.Q(h) / 2f / 96f);
            if (label != null) TextIn(b, r, label, size, lift: 8);
            if (onClick != null) Press(b, r, onClick);
            return b;
        }

        /// <summary>The gray square close button with the cross, its top-right corner at (right, top) of the parent.</summary>
        public static VisualElement Close(VisualElement parent, Ref r, float parentWidth, Action onClick)
        {
            var b = Box(parent, r, parentWidth - 90, -14, 100, 92);
            Picture(b, GameArt.Get("Button_Square02_Gray"));
            var icon = Box(b, r, 24, 18 - 6, 52, 52 * 53f / 49f);
            Picture(icon, GameArt.Get("Icon_PictoIcon_Close"));
            Press(b, r, onClick, "whoosh");
            return b;
        }

        /// <summary>
        /// The navy popup (Popup01_Single_Navy) w×h centred on the page, with the lighter glow at the top
        /// right, a title band when <paramref name="title"/> is set, and the close button. Returns the body
        /// (the popup's own box: place the content in it in reference units).
        /// </summary>
        public static VisualElement Popup(VisualElement layer, Ref r, float w, float h, string? title, Action? onClose, float band = 136)
        {
            var pop = new VisualElement { pickingMode = PickingMode.Position };   // presses on the popup stay on it
            Place(pop, (r.W - r.Q(w)) / 2f, (r.H - r.Q(h)) / 2f, r.Q(w), r.Q(h));
            layer.Add(pop);

            var frame = Fill(pop, r);
            Nine(frame, GameArt.Get("Popup01_Single_Navy"), 16, 16, 16, 20, .75f * r.S);

            var glow = Fill(pop, r, 4, 4, 4, 14);
            Round(glow, r.Q(10));
            Picture(glow, GameArt.Radial("popglow_" + GameTheme.Current, .75f, 0f, 1.2f, .9f,
                (0f, GameTheme.T("#0462a6", .95f)), (.35f, GameTheme.T("#035091", .6f)), (.7f, GameTheme.T("#023875", 0f))));

            if (!string.IsNullOrEmpty(title))
            {
                var head = Box(pop, r, 4, 4, w - 8, band - 4);
                Round(head, r.Q(10), r.Q(10), 0, 0);
                Picture(head, GameArt.Linear("popband_" + GameTheme.Current, false,
                    (0f, GameTheme.T("#024687", 0f)), (.6f, GameTheme.T("#0460a5", .55f)), (1f, GameTheme.T("#0462a6", .7f))));
                TextIn(head, r, title!, 66);
            }

            var body = Fill(pop, r);
            if (onClose != null) Close(pop, r, w, onClose);
            return body;
        }

        /// <summary>The dark veil behind a pack popup; it takes the presses so the island underneath doesn't.</summary>
        public static VisualElement Veil(VisualElement layer)
        {
            var v = new VisualElement { pickingMode = PickingMode.Position };
            v.style.position = Position.Absolute;
            v.style.left = 0;
            v.style.top = 0;
            v.style.right = 0;
            v.style.bottom = 0;
            v.style.backgroundColor = GameTheme.T("#030a16", .86f);
            layer.Add(v);
            return v;
        }

        /// <summary>The pack switch, 178×94, centred on yc: orange track and white knob when on.</summary>
        public static VisualElement Switch(VisualElement parent, Ref r, float x, float yc, bool on, Action<bool> changed)
        {
            var root = Box(parent, r, x, yc - 47, 178, 94);
            var track = Fill(root, r, 10, 16, 4, 94 - 16 - 62);
            Round(track, r.Q(31));
            var knob = Box(root, r, 0, 0, 94, 94);
            Round(knob, r.Q(47));
            Border(knob, r.Q(3), Color.black);
            var shade = Fill(knob, r, 0, 60, 0, 0);   // the knob's darker lower edge
            Round(shade, 0, 0, r.Q(44), r.Q(44));

            void Show()
            {
                Colour(track, on ? GameTheme.C("#f6a814") : GameTheme.T("#554c71"));
                knob.style.left = on ? r.Q(84) : 0;
                Colour(knob, on ? Color.white : GameTheme.T("#7d70a6"));
                Colour(shade, on ? GameTheme.T("#c9d2f2") : GameTheme.T("#5d527f"));
            }

            Show();
            Press(root, r, () =>
            {
                on = !on;
                Show();
                changed(on);
            }, "");
            return root;
        }

        /// <summary>The pack's tick slider: 16 ticks (orange up to the handle), the handle dragged along them. Value 0..1.</summary>
        public static VisualElement TickSlider(VisualElement parent, Ref r, float x, float yc, float value, Action<float> changed, Action<float>? released = null)
        {
            const int n = 16;
            const float step = 20.5f, tw = 9.5f, th = 59f;
            var root = Box(parent, r, x, yc - th / 2f, n * step, th);
            root.pickingMode = PickingMode.Position;
            var hit = Fill(root, r, -20, -40, -20, -40);   // a bigger target than the thin ticks
            hit.pickingMode = PickingMode.Position;
            var ticks = new VisualElement[n];
            for (var i = 0; i < n; i++)
            {
                ticks[i] = Box(root, r, i * step, 0, tw, th);
                Round(ticks[i], r.Q(5));
            }

            var handle = Box(root, r, 0, -32, 61, 124);
            Picture(handle, GameArt.Get("Slider_Handle_Icon"));

            void Show()
            {
                var hx = Mathf.RoundToInt(Mathf.Clamp01(value) * (n - 1));
                for (var i = 0; i < n; i++) Colour(ticks[i], i < hx ? GameTheme.C("#f6a814") : GameTheme.T("#03305e"));
                handle.style.left = r.Q(hx * step - 26);
            }

            void At(Vector2 local)
            {
                var v = Mathf.Clamp01(Mathf.Round(local.x / r.Q((n - 1) * step) * (n - 1)) / (n - 1));
                if (Mathf.Approximately(v, value)) return;
                value = v;
                Show();
                changed(value);
            }

            var dragging = false;
            hit.RegisterCallback<PointerDownEvent>(e =>
            {
                dragging = true;
                hit.CapturePointer(e.pointerId);
                At(root.WorldToLocal(e.position));
                e.StopPropagation();
            });
            hit.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (dragging && hit.HasPointerCapture(e.pointerId)) At(root.WorldToLocal(e.position));
            });
            hit.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!dragging) return;
                dragging = false;
                hit.ReleasePointer(e.pointerId);
                released?.Invoke(value);
            });
            Show();
            return root;
        }
    }
}
