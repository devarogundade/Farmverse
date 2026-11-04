#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The mockups' shared kit (kit.js / kit.css) in design points (<see cref="Pt"/>): stroked text,
    /// pill buttons, the Popup03 popup with ribbon or title band, bars, item slots, tabs, the pack switch,
    /// badges, currency pills, the page header, list rows and panels, plus small flex helpers.
    /// Numbers are the mockups' own: c.p(n) → <see cref="Pt.P"/>, literal px → <see cref="Pt.U"/>.
    /// </summary>
    public static class PK
    {
        /// <summary>The design's ink (outline and shadow of text): #0b1d3a, themed.</summary>
        public static Color Ink => GameTheme.T("#0b1d3a");

        // ------------------------------------------------------------------ elements and flex

        /// <summary>An empty box (clicks pass through).</summary>
        public static VisualElement Box(VisualElement? parent = null, string? name = null)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (name != null) e.name = name;
            parent?.Add(e);
            return e;
        }

        /// <summary>A flex row (items centred vertically), children <paramref name="gap"/> panel px apart.</summary>
        public static VisualElement Row(VisualElement? parent, float gap = 0)
        {
            var e = Box(parent);
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            if (gap > 0) Gap(e, gap);
            return e;
        }

        /// <summary>A flex column, children <paramref name="gap"/> panel px apart.</summary>
        public static VisualElement Col(VisualElement? parent, float gap = 0)
        {
            var e = Box(parent);
            e.style.flexDirection = FlexDirection.Column;
            if (gap > 0) Gap(e, gap);
            return e;
        }

        /// <summary>CSS gap for a row or column: every child after the first gets a leading margin (also children added later).</summary>
        public static void Gap(VisualElement e, float gap)
        {
            e.userData = gap;
            e.RegisterCallback<GeometryChangedEvent>(_ => ApplyGap(e));
            e.schedule.Execute(() => ApplyGap(e));
        }

        private static void ApplyGap(VisualElement e)
        {
            if (!(e.userData is float gap)) return;
            var row = e.resolvedStyle.flexDirection == FlexDirection.Row || e.resolvedStyle.flexDirection == FlexDirection.RowReverse;
            var first = true;
            foreach (var c in e.Children())
            {
                if (c.resolvedStyle.position == Position.Absolute) continue;
                var m = first ? 0f : gap;
                if (row) { if (!Mathf.Approximately(c.resolvedStyle.marginLeft, m)) c.style.marginLeft = m; }
                else if (!Mathf.Approximately(c.resolvedStyle.marginTop, m)) c.style.marginTop = m;
                first = false;
            }
        }

        /// <summary>Wrapping grid: a row that wraps, with column and row gaps (panel px).</summary>
        public static VisualElement Grid(VisualElement? parent, float colGap, float rowGap)
        {
            var e = Box(parent);
            e.style.flexDirection = FlexDirection.Row;
            e.style.flexWrap = Wrap.Wrap;
            e.style.alignContent = Align.FlexStart;
            e.style.marginLeft = 0;
            e.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                foreach (var c in e.Children())
                {
                    c.style.marginRight = colGap;
                    c.style.marginBottom = rowGap;
                }
            });
            return e;
        }

        /// <summary>Absolute position (panel px). Pass NaN to leave a side unset.</summary>
        public static T Abs<T>(T e, float left = float.NaN, float top = float.NaN, float width = float.NaN, float height = float.NaN, float right = float.NaN, float bottom = float.NaN) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            if (!float.IsNaN(left)) e.style.left = left;
            if (!float.IsNaN(top)) e.style.top = top;
            if (!float.IsNaN(right)) e.style.right = right;
            if (!float.IsNaN(bottom)) e.style.bottom = bottom;
            if (!float.IsNaN(width)) e.style.width = width;
            if (!float.IsNaN(height)) e.style.height = height;
            return e;
        }

        /// <summary>Covers the parent (CSS inset), panel px.</summary>
        public static T Inset<T>(T e, float l = 0, float t = 0, float r = 0, float b = 0) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            e.style.left = l;
            e.style.top = t;
            e.style.right = r;
            e.style.bottom = b;
            return e;
        }

        public static T Size<T>(T e, float w, float h) where T : VisualElement
        {
            if (!float.IsNaN(w)) e.style.width = w;
            if (!float.IsNaN(h)) e.style.height = h;
            e.style.flexShrink = 0;
            return e;
        }

        public static T Pad<T>(T e, float l, float t, float r, float b) where T : VisualElement
        {
            e.style.paddingLeft = l;
            e.style.paddingTop = t;
            e.style.paddingRight = r;
            e.style.paddingBottom = b;
            return e;
        }

        public static T Centre<T>(T e) where T : VisualElement
        {
            e.style.alignItems = Align.Center;
            e.style.justifyContent = Justify.Center;
            return e;
        }

        /// <summary>A flexible spacer (CSS flex:1).</summary>
        public static VisualElement Grow(VisualElement parent)
        {
            var e = Box(parent);
            e.style.flexGrow = 1;
            return e;
        }

        /// <summary>Hides overflow and clips children.</summary>
        public static T Clip<T>(T e) where T : VisualElement
        {
            e.style.overflow = Overflow.Hidden;
            return e;
        }

        /// <summary>CSS "0 y 0 colour" drop shadow, done as a bottom border (keeps the radius).</summary>
        public static T DropEdge<T>(T e, float y, Color c) where T : VisualElement
        {
            e.style.borderBottomWidth = y;
            e.style.borderBottomColor = c;
            return e;
        }

        // ------------------------------------------------------------------ pictures

        /// <summary>A pack picture at height h (panel px), width from the picture's own shape (CSS img with only a height).</summary>
        public static VisualElement Img(VisualElement? parent, string art, float h, float w = float.NaN)
        {
            var e = Box(parent);
            var t = GameArt.Get(art);
            if (float.IsNaN(w)) w = t != null && t.height > 0 ? h * t.width / t.height : h;
            Size(e, w, h);
            Kit.Picture(e, t);
            return e;
        }

        /// <summary>A pack picture fitted into w×h.</summary>
        public static VisualElement Fit(VisualElement? parent, string art, float w, float h)
        {
            var e = Box(parent);
            Size(e, w, h);
            Kit.Contain(e, GameArt.Get(art));
            return e;
        }

        /// <summary>Nine-sliced pack picture: slices in picture pixels (top, right, bottom, left as CSS), drawn so the top slice is <paramref name="topPx"/> panel px.</summary>
        public static T Nine<T>(T e, string art, int top, int right, int bottom, int left, float topPx) where T : VisualElement
        {
            Kit.Nine(e, GameArt.Get(art), left, top, right, bottom, top > 0 ? topPx / top : 1f);
            return e;
        }

        /// <summary>A gradient background (vertical: top→bottom; else left→right). Keys must be unique per gradient.</summary>
        public static T Grad<T>(T e, string key, bool vertical, params (float at, Color c)[] stops) where T : VisualElement
        {
            Kit.Picture(e, GameArt.Linear(key + "_" + GameTheme.Current, vertical, stops));
            return e;
        }

        // ------------------------------------------------------------------ text

        /// <summary>
        /// The mockups' stroked text (.t): Lilita One, outline max(2.5, fs×.14) in ink, a drop shadow 0.7 of that
        /// below. <paramref name="stroke"/> false: plain coloured Lilita.
        /// </summary>
        public static Label T(VisualElement? parent, Pt p, string text, float fs, Color? colour = null, bool stroke = true, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var l = new Label(Loc.Of(text)) { pickingMode = PickingMode.Ignore };
            var font = GameArt.Display;
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            Plain(l, p.P(fs), colour ?? Color.white, align);
            if (stroke)
            {
                var sw = p.P(Mathf.Max(2.5f, fs * .14f));
                l.style.unityTextOutlineWidth = sw;
                l.style.unityTextOutlineColor = Ink;
                l.style.textShadow = new TextShadow { offset = new Vector2(0, sw * .7f), blurRadius = 0, color = Ink };
            }

            parent?.Add(l);
            return l;
        }

        /// <summary>Stroked text sized in literal design pixels (not scaled by k), e.g. the header's; stroke width in px too (default fs×.14).</summary>
        public static Label TL(VisualElement? parent, Pt p, string text, float fsPx, float strokePx = -1)
        {
            var l = T(parent, p, text, 1);
            l.style.fontSize = p.U(fsPx);
            var sw = p.U(strokePx > 0 ? strokePx : Mathf.Max(2.5f, fsPx * .14f));
            l.style.unityTextOutlineWidth = sw;
            l.style.textShadow = new TextShadow { offset = new Vector2(0, sw * .7f), blurRadius = 0, color = Ink };
            return l;
        }

        /// <summary>Body text (.n): Nunito ExtraBold, no outline. Wraps when <paramref name="wrap"/>.</summary>
        public static Label N(VisualElement? parent, Pt p, string text, float fs, Color? colour = null, TextAnchor align = TextAnchor.MiddleLeft, bool wrap = false)
        {
            var l = new Label(Loc.Of(text)) { pickingMode = PickingMode.Ignore };
            var font = GameArt.Body;
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            Plain(l, p.P(fs), colour ?? Color.white, align);
            if (wrap) l.style.whiteSpace = WhiteSpace.Normal;
            parent?.Add(l);
            return l;
        }

        private static void Plain(Label l, float size, Color c, TextAnchor align)
        {
            l.style.fontSize = size;
            l.style.color = c;
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
            l.style.flexShrink = 0;
        }

        // ------------------------------------------------------------------ pressing

        /// <summary>Makes it a button (sinks while held, runs on release over it).</summary>
        public static T Tap<T>(T e, Pt p, Action onClick, string sound = "pop") where T : VisualElement
        {
            e.pickingMode = PickingMode.Position;
            var sink = Mathf.Max(1f, p.U(2));
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

        // ------------------------------------------------------------------ pack parts

        /// <summary>
        /// The pill button (btn): green, blue, gray, purple, yellow (Button01_195_*) or yellow/purple/red small
        /// (Button01_175_*, pass "yellow175" etc.). w ≤ 0: as wide as the label plus fs×.8 each side.
        /// </summary>
        public static VisualElement Btn(VisualElement? parent, Pt p, string colour, string? label, float w, float h, float fs, Action? onClick = null)
            => Btn(parent, p, colour, label, w, h, fs, false, onClick);

        /// <summary>A button; while <paramref name="busy"/> it keeps its size and colour, shows a turning loader
        /// instead of its label and ignores taps (so a slow server call can't be sent twice).</summary>
        public static VisualElement Btn(VisualElement? parent, Pt p, string colour, string? label, float w, float h, float fs, bool busy, Action? onClick = null)
        {
            var (art, sl) = colour switch
            {
                "green" => ("Button01_195_Green", 96),
                "blue" => ("Button01_195_Blue", 96),
                "gray" => ("Button01_195_Gray", 96),
                "bluegray" => ("Button01_195_BlueGray", 96),
                "purple195" => ("Button01_195_Purple", 96),
                "yellow195" => ("Button01_195_Yellow", 96),
                "yellow" => ("Button01_175_Yellow", 86),
                "purple" => ("Button01_175_Purple", 86),
                "red" => ("Button01_175_Red", 86),
                _ => ("Button01_195_Blue", 96),
            };
            var b = Row(parent);
            b.style.justifyContent = Justify.Center;
            b.style.flexShrink = 0;
            if (w > 0) b.style.width = p.P(w);
            b.style.height = p.P(h);
            b.style.paddingLeft = p.P(fs * .8f);
            b.style.paddingRight = p.P(fs * .8f);
            Kit.Nine(b, GameArt.Get(art), 30, sl, 30, sl, p.P(h) / 2f / sl);
            if (busy)
            {
                Spinner(b, p, h * .52f);
                b.style.opacity = .9f;
                return b;
            }
            if (label != null) T(b, p, label, fs);
            if (onClick != null) Tap(b, p, onClick);
            return b;
        }

        /// <summary>The game's loader (Loading_Rotate), turning once a second; stops with its element.</summary>
        public static VisualElement Spinner(VisualElement? parent, Pt p, float size)
        {
            var img = Box(parent);
            img.style.width = p.P(size);
            img.style.height = p.P(size);
            img.style.flexShrink = 0;
            img.pickingMode = PickingMode.Ignore;
            Kit.Contain(img, GameArt.Get("Loading_Rotate"));
            img.schedule.Execute(() => img.style.rotate = new Rotate(new Angle(Time.unscaledTime * 360f % 360f, AngleUnit.Degree))).Every(16);
            return img;
        }

        /// <summary>
        /// The kit's popup (Popup03_Single, centred): a ribbon title (Title_Ribbon_Bg_Green/Orange/Yellow) or a
        /// title band, the close button. Returns the popup box: a flex column with the design's padding; add
        /// the body to it. h ≤ 0: as tall as its content.
        /// </summary>
        public static VisualElement Popup(VisualElement root, Pt p, float w, float h, string? title, Action? onClose, string? ribbon = null, float pad = 30, float ribbonW = 520)
        {
            var pop = Col(root);
            pop.pickingMode = PickingMode.Position;
            pop.style.position = Position.Absolute;
            pop.style.left = new Length(50, LengthUnit.Percent);
            pop.style.top = new Length(50, LengthUnit.Percent);
            pop.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            pop.style.width = p.P(w);
            if (h > 0) pop.style.height = p.P(h);
            Kit.Nine(pop, GameArt.Get("Popup03_Single"), 18, 24, 18, 30, p.P(22) / 24f);
            Pad(pop, p.P(pad), ribbon != null ? p.P(70) : 0, p.P(pad), p.P(pad + 26));

            if (ribbon != null && title != null)
            {
                var rb = Abs(Box(pop), left: (p.P(w) - p.P(ribbonW)) / 2f, top: p.P(-46), width: p.P(ribbonW), height: p.P(100));
                Kit.Picture(rb, GameArt.Get("Title_Ribbon_Bg_" + ribbon));
                Centre(rb);
                rb.style.paddingBottom = p.P(18);
                T(rb, p, title, 34);
            }
            else if (title != null)
            {
                var band = Centre(Box(pop));
                band.style.height = p.P(78);
                band.style.flexShrink = 0;
                band.style.marginLeft = -p.P(pad);
                band.style.marginRight = -p.P(pad);
                band.style.borderBottomWidth = p.U(3);
                band.style.borderBottomColor = GameTheme.Shade(.18f);
                T(band, p, title, 36);
            }

            if (onClose != null)
            {
                var x = Abs(Box(pop), top: p.P(-22), width: p.P(70), height: p.P(77), right: p.P(-24));
                Kit.Picture(x, GameArt.Get("Button_Square02_Gray"));
                Centre(x);
                var ic = Img(x, "Icon_PictoIcon_Close", p.P(70) * .52f * 53f / 49f, p.P(70) * .52f);
                ic.style.marginTop = -p.P(70) * .08f;
                Tap(x, p, onClose, "whoosh");
            }

            return pop;
        }

        /// <summary>The kit's dark veil (.dim) over a scene.</summary>
        public static VisualElement Dim(VisualElement root, float alpha = .72f)
        {
            var d = Inset(Box(root));
            d.style.backgroundColor = GameTheme.T("#030c20", alpha);
            return d;
        }

        /// <summary>The kit's screen background (.screen): blue radial glow, optional dots. Covers the root and takes presses.</summary>
        public static VisualElement Screen(VisualElement root, bool dots = false)
        {
            var s = Inset(Box(root));
            s.pickingMode = PickingMode.Position;
            Kit.Picture(s, GameArt.Radial("screen_" + GameTheme.Current, .5f, .3f, .7f, .6f,
                (0f, GameTheme.T("#1572c6")), (.5f, GameTheme.T("#0d5aa4")), (1f, GameTheme.T("#0a4888"))));
            if (dots)
            {
                var d = Inset(Box(s));
                d.style.opacity = .12f;
                Kit.Picture(d, GameArt.Dots());
                d.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
                d.style.backgroundSize = new BackgroundSize(new Length(170 * (root.layout.width > 0 ? root.layout.width / 1366f : 1f)), new Length(130 * (root.layout.width > 0 ? root.layout.width / 1366f : 1f)));
            }

            return s;
        }

        /// <summary>The progress bar (.bar): navy, ink edge, yellow (or blue) fill and a stroked label. Returns (bar, fill, label).</summary>
        public static (VisualElement bar, VisualElement fill, Label label) Bar(VisualElement? parent, Pt p, float w, float h, float pct, string label, bool blue = false)
        {
            var bar = Box(parent);
            Size(bar, w > 0 ? p.P(w) : float.NaN, p.P(h));
            bar.style.backgroundColor = GameTheme.T("#0d1d4f");
            Kit.Border(bar, p.U(3), Ink);
            Kit.Round(bar, p.U(8));
            Clip(bar);
            var fill = Abs(Box(bar), left: 0, top: 0, bottom: 0);
            Kit.Nine(fill, GameArt.Get(blue ? "Slider_Level01_Fill_Blue" : "Slider_Basic02_Fill_Yellow"), 16, 18, 16, 18, p.U(6) / 18f);
            SetPct(fill, pct);
            var l = T(Inset(Centre(Box(bar))), p, label, h * .58f);
            l.parent.style.alignItems = Align.Center;
            return (bar, fill, l);
        }

        public static void SetPct(VisualElement fill, float pct) => fill.style.width = new Length(Mathf.Clamp(pct, 0, 100), LengthUnit.Percent);

        /// <summary>The item slot (.slot): ItemFrame01_Single_<colour> with an icon and a stroked count; gold frame when selected.</summary>
        public static VisualElement Slot(VisualElement? parent, Pt p, string? icon, string? count, string colour = "Blue", float s = 120, bool sel = false, Action? onClick = null)
        {
            var e = Centre(Box(parent));
            Size(e, p.P(s), p.P(s * 1.06f));
            Kit.Nine(e, GameArt.Get("ItemFrame01_Single_" + colour), 40, 40, 40, 46, p.P(s * .2f) / 40f);
            if (icon != null)
            {
                var ic = Box(e);
                Size(ic, p.P(s * .72f), p.P(s * .72f));
                Kit.Contain(ic, GameArt.Get(icon));
            }

            if (!string.IsNullOrEmpty(count))
                Abs(T(e, p, count!, s * .22f), right: p.P(s) * .06f, bottom: p.P(s) * .04f);
            if (sel)
            {
                var f = Inset(Box(e), -p.P(s) * .06f, -p.P(s) * .06f, -p.P(s) * .06f, -p.P(s) * .06f);
                Kit.Picture(f, GameArt.Get("ItemFrame01_f"));
            }

            if (onClick != null) Tap(e, p, onClick);
            return e;
        }

        /// <summary>The tab strip (.seg): navy, light-blue labels, the chosen one white with a white bar under it.</summary>
        public static VisualElement Seg(VisualElement? parent, Pt p, (string id, string label)[] tabs, string on, Action<string> pick, float fs = 26, float h = 60)
        {
            var seg = Row(parent);
            seg.style.height = p.P(h);
            seg.style.flexShrink = 0;
            seg.style.backgroundColor = GameTheme.T("#08284f");
            Kit.Round(seg, p.U(12));
            DropEdge(seg, p.U(4), GameTheme.Shade(.25f));
            foreach (var (id, label) in tabs)
            {
                var tab = Centre(Box(seg));
                tab.style.flexGrow = 1;
                tab.style.flexBasis = 0;
                tab.style.height = new Length(100, LengthUnit.Percent);
                var sel = id == on;
                T(tab, p, label, fs, sel ? Color.white : GameTheme.T("#3cb4ff"));
                if (sel)
                {
                    var u = Box(tab);
                    u.style.position = Position.Absolute;
                    u.style.bottom = 0;
                    u.style.left = new Length(28, LengthUnit.Percent);
                    u.style.right = new Length(28, LengthUnit.Percent);
                    u.style.height = p.U(4);
                    u.style.backgroundColor = Color.white;
                    Kit.Round(u, p.U(6), p.U(6), 0, 0);
                }

                var tid = id;
                Tap(tab, p, () => pick(tid));
            }

            return seg;
        }

        /// <summary>The pack switch (.sw): Switch_Bg_On/Off and the handle; flips on tap.</summary>
        public static VisualElement Switch(VisualElement? parent, Pt p, bool on, Action<bool> changed, float w = 96, float h = 50)
        {
            var e = Box(parent);
            Size(e, p.P(w), p.P(h));
            var bg = Inset(Box(e));
            var hd = Abs(Box(e), top: -p.P(h) * .12f, width: p.P(h) * 1.24f * 96f / 115f, height: p.P(h) * 1.24f);

            void Show()
            {
                Kit.Nine(bg, GameArt.Get(on ? "Switch_Bg_On" : "Switch_Bg_Off"), 32, 30, 32, 30, p.P(h) / 2f / 30f);
                Kit.Picture(hd, GameArt.Get(on ? "Switch_Handle_On" : "Switch_Handle_Off"));
                hd.style.left = on ? p.P(w) - p.P(h) * 1.24f * 96f / 115f + p.P(w) * .04f : -p.P(w) * .04f;
            }

            Show();
            Tap(e, p, () =>
            {
                on = !on;
                Show();
                changed(on);
            });
            return e;
        }

        /// <summary>A round count badge (.badge): red or green with a white edge.</summary>
        public static VisualElement Badge(VisualElement? parent, Pt p, string text, bool green, float s)
        {
            var b = Centre(Box(parent));
            b.style.position = Position.Absolute;
            b.style.minWidth = p.P(s);
            b.style.height = p.P(s);
            Pad(b, p.P(5), 0, p.P(5), 0);
            b.style.backgroundColor = green ? GameTheme.C("#2fbf2a") : GameTheme.C("#e8253f");
            Kit.Round(b, p.P(s));
            Kit.Border(b, p.U(2), Color.white);
            T(b, p, text, s * .6f);
            return b;
        }

        /// <summary>
        /// The currency pill (.pill): dark gradient, a plus button on the left, the value, the coin or gem
        /// icon hanging off the right. As wide as its value (every pill in the game fits its number the same
        /// way). Sizes are the header's (k = header scale). Returns (pill, value label).
        /// </summary>
        public static (VisualElement pill, Label value) Currency(VisualElement? parent, Pt p, bool gem, string value, float k, Action? onPlus = null)
        {
            var pill = Row(parent);
            pill.style.justifyContent = Justify.FlexEnd;
            pill.style.height = p.U(50 * k);
            pill.style.flexShrink = 0;
            pill.style.paddingLeft = p.U(4) + p.U(42 * k) + p.U(14 * k);   // clear of the plus button
            pill.style.paddingRight = p.U(62 * k);                          // clear of the hanging icon
            Kit.Round(pill, p.U(10));
            Grad(pill, gem ? "pillgem" : "pillgold", true, gem
                ? new[] { (0f, GameTheme.C("#170a2c")), (1f, GameTheme.C("#221040")) }
                : new[] { (0f, GameTheme.C("#170d06")), (1f, GameTheme.C("#2b160a")) });
            var plus = Img(pill, gem ? "ResourceBar_Btn_Single_Purple" : "ResourceBar_Btn_Single_Yellow", p.U(42 * k));
            Abs(plus, left: p.U(4), top: (p.U(50 * k) - p.U(42 * k)) / 2f);
            Centre(plus);
            Img(plus, "ResourceBar_Btn_Icon_Add", p.U(22 * k));
            var t = TL(pill, p, value, 25 * k);
            var ico = Img(pill, gem ? "ResourceBar_Icon_Gem" : "ResourceBar_Icon_Gold", p.U(60 * k));
            Abs(ico, right: -p.U(10 * k), top: (p.U(50 * k) - p.U(60 * k)) / 2f);
            if (onPlus != null) Tap(plus, p, onPlus);
            return (pill, t);
        }

        /// <summary>
        /// The page header (hdr): back button flush left, title, optional extra, coins, gems, home. Height 96
        /// on tablet, 52 on phone (literal px). Returns the coins and gems labels for live updates.
        /// </summary>
        public static (Label coins, Label gems) Header(VisualElement root, Pt p, string title, Action back, Action? home, string coins, string gems, VisualElement? extra = null, bool pills = true, Action? addCoins = null, Action? addGems = null)
        {
            var H = p.Phone ? 52f : 96f;
            var k = H / 96f;
            var bar = Row(root);
            Abs(bar, left: 0, top: 0, right: 0, height: p.U(H));
            bar.style.paddingRight = p.R + p.U(14);
            bar.pickingMode = PickingMode.Ignore;

            var backBtn = Centre(Box(bar));
            Size(backBtn, p.U(H * 1.25f) + p.L, p.U(H * .8f));
            backBtn.style.paddingLeft = p.L;
            Kit.Flush(backBtn, true);
            Img(backBtn, "Icon_PictoIcon_Back", p.U(H * .8f) * .52f);
            Tap(backBtn, p, back, "whoosh");

            var t = TL(bar, p, title, H * .46f, Mathf.Max(3, H * .06f));
            t.style.marginLeft = p.U(H * .32f);
            Grow(bar);
            if (extra != null) bar.Add(extra);

            Label c = null!, g = null!;
            if (pills)
            {
                var (cp, cl) = Currency(bar, p, false, coins, k, addCoins);
                cp.style.marginLeft = p.U(14 * k);
                var (gp, gl) = Currency(bar, p, true, gems, k, addGems);
                gp.style.marginLeft = p.U(22 * k);
                c = cl;
                g = gl;
            }

            if (home != null)
            {
                var h = Img(bar, "Icon_PictoIcon_Home", p.U(H * .6f));
                h.style.marginLeft = p.U(22 * k);
                Tap(h, p, home, "whoosh");
            }

            return (c, g);
        }

        /// <summary>A list row (.row): blue gradient with its darker lower edge; brighter when highlighted, greyed when done.</summary>
        public static VisualElement ListRow(VisualElement? parent, Pt p, bool hi = false, bool done = false)
        {
            var r = Row(parent);
            r.style.flexShrink = 0;
            Kit.Round(r, p.U(12));
            if (hi) Grad(r, "rowhi", true, (0f, GameTheme.T("#27a9ff")), (1f, GameTheme.T("#1a8de8")));
            else Grad(r, "row", true, (0f, GameTheme.T("#1677cf")), (1f, GameTheme.T("#0f65b5")));
            DropEdge(r, p.U(5), hi ? GameTheme.T("#0f6cc0") : GameTheme.T("#0a4c8f"));
            if (done) r.style.opacity = .75f;
            return r;
        }

        /// <summary>A navy panel (.panelN).</summary>
        public static VisualElement Panel(VisualElement? parent, Pt p)
        {
            var e = Box(parent);
            e.style.backgroundColor = GameTheme.T("#08284f");
            Kit.Round(e, p.U(16));
            DropEdge(e, p.U(5), GameTheme.Shade(.25f));
            return e;
        }

        /// <summary>Number with thousands commas.</summary>
        public static string Num(long n) => n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
