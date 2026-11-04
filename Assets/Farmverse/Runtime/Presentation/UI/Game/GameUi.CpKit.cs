#nullable enable
using System;
using Farmverse.Contracts;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// What a reward popup shows: pack icon + count text per item. Open("reward", new CpReward(...)).
    /// The reveal only shows; whoever opens it has already granted the reward (the server pays; coins go to the island).
    /// </summary>
    public sealed class CpReward
    {
        public readonly List<(string art, string text)> Items = new List<(string, string)>();
        public string Title = "REWARDS";

        public CpReward() { }

        public CpReward(params (string art, string text)[] items) => Items.AddRange(items);
    }

    /// <summary>How a catalogue prize (Bundle) shows: coins, gems and boosts as reward tiles.</summary>
    public static class CpPrize
    {
        public static List<(string art, string text)> Tiles(Bundle b)
        {
            var list = new List<(string, string)>();
            if (b.Coins > 0) list.Add(("ResourceBar_Icon_Gold", PK.Num(b.Coins)));
            if (b.Gems > 0) list.Add(("ResourceBar_Icon_Gem", PK.Num(b.Gems)));
            foreach (var kv in b.Boosts) list.Add((GameUi.BoostArt(kv.Key), "×" + kv.Value));
            if (b.Art != null) list.Add((b.Art, b.Label ?? ""));
            return list;
        }

        /// <summary>One tile for the prize (a grid cell, a wheel slice): its own picture, else its first part.</summary>
        public static (string art, string text) Tile(Bundle b)
        {
            if (b.Art != null) return (b.Art, b.Label ?? "");
            var tiles = Tiles(b);
            return tiles.Count > 0 ? tiles[0] : ("Icon_Gift", "");
        }

        /// <summary>What a paid answer gave (coins, gems, boosts).</summary>
        public static CpReward Reward(long coins, long gems, Dictionary<string, long> boosts, string? art = null, string? label = null)
        {
            var r = new CpReward();
            if (coins > 0) r.Items.Add(("ResourceBar_Icon_Gold", PK.Num(coins)));
            if (gems > 0) r.Items.Add(("ResourceBar_Icon_Gem", PK.Num(gems)));
            foreach (var kv in boosts) if (kv.Value > 0) r.Items.Add((GameUi.BoostArt(kv.Key), "×" + kv.Value));
            if (art != null) r.Items.Add((art, label ?? ""));
            return r;
        }
    }

    /// <summary>Textures the coupons, pass and rewards screens draw with (generated once per theme).</summary>
    public static class CpArt
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        private static string Key(string k) => k + "_" + GameTheme.Current;

        /// <summary>A 135° gradient (top-left → bottom-right), CSS linear-gradient(135deg, a, b).</summary>
        public static Texture2D Diag(string key, Color a, Color b)
        {
            key = Key("diag_" + key);
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            const int n = 64;
            t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var top = n - 1 - y;   // y from the top
                t.SetPixel(x, y, Color.Lerp(a, b, (x + top) / (2f * (n - 1))));
            }

            t.Apply();
            Cache[key] = t;
            return t;
        }

        /// <summary>
        /// The lucky wheel's disc: 8 segments alternating two blues (the first starting at 12 o'clock and
        /// going clockwise), ink spokes and an ink rim <paramref name="rim"/> (0..1 of the radius).
        /// </summary>
        public static Texture2D Wheel(float rim, float spoke)
        {
            var key = Key("wheel");
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            const int n = 512;
            var a = GameTheme.T("#1f8de8");
            var b = GameTheme.T("#1569c4");
            var ink = PK.Ink;
            t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var r0 = n / 2f;
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                float dx = x + .5f - r0, dy = (n - 1 - y) + .5f - r0;   // dy down
                var d = Mathf.Sqrt(dx * dx + dy * dy) / r0;
                var alpha = Mathf.Clamp01((1f - d) * r0);
                var ang = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;   // 0 at top, clockwise
                if (ang < 0) ang += 360f;
                var seg = (int)(ang / 45f);
                var c = seg % 2 == 0 ? a : b;
                var off = Mathf.Abs(Mathf.DeltaAngle(ang, Mathf.Round(ang / 45f) * 45f)) * Mathf.Deg2Rad * d * r0;   // px from a spoke
                if (off < spoke * r0 / 2f) c = ink;
                if (d > 1f - rim) c = ink;
                c.a = alpha;
                t.SetPixel(x, y, c);
            }

            t.Apply();
            Cache[key] = t;
            return t;
        }

        /// <summary>The level-up rays: white 8° wedges every 22° (CSS repeating-conic-gradient), 10% white.</summary>
        public static Texture2D Rays()
        {
            var key = "rays";
            if (Cache.TryGetValue(key, out var t) && t != null) return t;
            const int n = 512;
            t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                float dx = x - n / 2f, dy = y - n / 2f;
                var ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg + 360f;
                var m = ang % 22f;
                t.SetPixel(x, y, new Color(1, 1, 1, m < 8f ? .10f : 0f));
            }

            t.Apply();
            Cache[key] = t;
            return t;
        }

        /// <summary>A soft round glow (CSS radial-gradient(rgba(255,255,255,.35), transparent 70%)).</summary>
        public static Texture2D SoftDisc() => GameArt.Radial("cp_softdisc", .5f, .5f, .5f, .5f, (0f, GameTheme.Light(.35f)), (.7f, GameTheme.Light(0f)));

        public static void Clear()
        {
            foreach (var t in Cache.Values) if (t != null) UnityEngine.Object.Destroy(t);
            Cache.Clear();
        }
    }

    public sealed partial class GameUi
    {
        /// <summary>Literal-pixel body text (Nunito, no outline) for the screens laid out in fixed px.</summary>
        private static Label CpN(VisualElement? parent, Pt p, string text, float fsPx, Color colour, TextAnchor align = TextAnchor.MiddleLeft, bool wrap = false)
        {
            var l = PK.N(parent, p, text, 1, colour, align, wrap);
            l.style.fontSize = p.U(fsPx);
            return l;
        }

        /// <summary>The coupon icon: the pack's gold ticket with a stroked "%" (w in panel px).</summary>
        private static VisualElement CpTicketIcon(VisualElement? parent, float w)
        {
            var h = w * 242f / 399f;
            var e = PK.Centre(PK.Box(parent));
            PK.Size(e, w, h);
            Kit.Picture(e, GameArt.Get("Icon_ImageIcon_Pass_Golden"));
            var l = new Label("%") { pickingMode = PickingMode.Ignore };
            var font = GameArt.Display;
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            l.style.fontSize = h * .62f;
            l.style.color = Color.white;
            l.style.unityTextOutlineWidth = Mathf.Max(2f, h * .09f);
            l.style.unityTextOutlineColor = GameTheme.C("#b25b00");
            l.style.marginTop = -h * .06f;
            l.style.paddingLeft = 0;
            l.style.paddingRight = 0;
            l.style.paddingTop = 0;
            l.style.paddingBottom = 0;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            e.Add(l);
            return e;
        }

        /// <summary>A gradient (vertical) box with an ink border, radius and a drop edge, all in panel px.</summary>
        private static VisualElement CpGradBox(VisualElement? parent, string key, Color top, Color bottom, float border, float radius, float drop, float dropAlpha = .25f)
        {
            var e = PK.Box(parent);
            PK.Grad(e, "cp_" + key, true, (0f, top), (1f, bottom));
            Kit.Border(e, border, PK.Ink);
            Kit.Round(e, radius);
            if (drop > 0) e.style.borderBottomWidth = border + drop;
            return e;
        }

        /// <summary>Slowly pulses an element's scale (CSS .pulse: 1 → 1.06 → 1 every 1.2 s).</summary>
        private static void CpPulse(Ctx c, VisualElement e, float phase = 0)
        {
            c.Live(() =>
            {
                var s = 1f + .03f * (1f - Mathf.Cos((Time.unscaledTime + phase) / 1.2f * Mathf.PI * 2f));
                e.style.scale = new Scale(new Vector3(s, s, 1));
            });
        }

        /// <summary>Spins an element (CSS .glowRot: once every 12 s, or <paramref name="period"/>).</summary>
        private static void CpSpin(Ctx c, VisualElement e, float period = 12f)
        {
            c.Live(() => e.style.rotate = new Rotate(new Angle(Time.unscaledTime / period * 360f % 360f, AngleUnit.Degree)));
        }

        /// <summary>Local midnight → seconds until then, as "14h 22m".</summary>
        private static string CpUntilMidnight()
        {
            var now = DateTime.Now;
            var left = now.Date.AddDays(1) - now;
            return $"{(int)left.TotalHours}h {left.Minutes}m";
        }

        /// <summary>Today as yyyymmdd (device time).</summary>
        private static int CpToday() => int.Parse(DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
    }
}
