#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The pack's pictures for the new UI (Resources/GameUi/Art), with the theme's tinted copy
    /// (Resources/GameUi/Themes/&lt;theme&gt;) used first when there is one. Also the two fonts and
    /// the soft gradients the design draws with.
    /// </summary>
    public static class GameArt
    {
        private static readonly Dictionary<string, Texture2D?> Cache = new Dictionary<string, Texture2D?>();
        private static readonly Dictionary<string, Texture2D> Gradients = new Dictionary<string, Texture2D>();
        private static Font? _display, _body, _japanese;

        public static Texture2D? Get(string name)
        {
            if (Cache.TryGetValue(name, out var t)) return t;
            t = Resources.Load<Texture2D>("GameUi/Themes/" + GameTheme.Current + "/" + name) ?? Resources.Load<Texture2D>("GameUi/Art/" + name);
            if (t == null) Debug.LogWarning("[GameUi] missing picture " + name + " (Resources/GameUi/Art)");
            Cache[name] = t;
            return t;
        }

        public static void ClearCache()
        {
            Cache.Clear();
            foreach (var g in Gradients.Values) if (g != null) Object.Destroy(g);
            Gradients.Clear();
        }

        /// <summary>Lilita One: titles, numbers and buttons.</summary>
        public static Font? Display => Loc.Iso == "ja" ? Japanese : _display ??= Resources.Load<Font>("GameUi/LilitaOne");

        /// <summary>Japanese text: M PLUS Rounded 1c ExtraBold (Lilita One and Nunito have Latin letters only).</summary>
        private static Font? Japanese => _japanese ??= Resources.Load<Font>("GameUi/MPlusRounded1cExtraBold");

        /// <summary>Nunito ExtraBold: small body lines.</summary>
        public static Font? Body => Loc.Iso == "ja" ? Japanese : _body ??= Resources.Load<Font>("GameUi/NunitoExtraBold");

        /// <summary>A left-to-right (or top-to-bottom) gradient through colour stops (position 0..1).</summary>
        public static Texture2D Linear(string key, bool vertical, params (float at, Color c)[] stops)
        {
            if (Gradients.TryGetValue(key, out var t) && t != null) return t;
            const int n = 128;
            t = new Texture2D(vertical ? 2 : n, vertical ? n : 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (var i = 0; i < n; i++)
            {
                var c = Sample(stops, i / (n - 1f));
                for (var j = 0; j < 2; j++)
                    if (vertical) t.SetPixel(j, n - 1 - i, c); else t.SetPixel(i, j, c);
            }
            t.Apply();
            Gradients[key] = t;
            return t;
        }

        /// <summary>An elliptical glow centred at (cx, cy) of the box (0..1, y down), radius rx, ry of the box.</summary>
        public static Texture2D Radial(string key, float cx, float cy, float rx, float ry, params (float at, Color c)[] stops)
        {
            if (Gradients.TryGetValue(key, out var t) && t != null) return t;
            const int w = 192, h = 128;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float dx = (x / (w - 1f) - cx) / rx, dy = (y / (h - 1f) - cy) / ry;
                t.SetPixel(x, h - 1 - y, Sample(stops, Mathf.Sqrt(dx * dx + dy * dy)));
            }
            t.Apply();
            Gradients[key] = t;
            return t;
        }

        /// <summary>A flat colour cut on the right: full width at the top, <paramref name="bottomWidth"/> (0..1) at the bottom (CSS clip-path polygon).</summary>
        public static Texture2D Slant(string key, Color c, float bottomWidth)
        {
            if (Gradients.TryGetValue(key, out var t) && t != null) return t;
            const int w = 96, h = 80;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < h; y++)
            {
                var edge = w * Mathf.Lerp(1f, bottomWidth, y / (h - 1f));   // y from the top
                for (var x = 0; x < w; x++)
                {
                    var o = c;
                    o.a *= Mathf.Clamp01(edge - x);
                    t.SetPixel(x, h - 1 - y, o);
                }
            }
            t.Apply();
            Gradients[key] = t;
            return t;
        }

        /// <summary>The screen's dot pattern tile (a white dot in a 170×130 cell).</summary>
        public static Texture2D Dots()
        {
            if (Gradients.TryGetValue("dots", out var t) && t != null) return t;
            const int w = 170, h = 130;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var d = Mathf.Sqrt((x - w / 2f) * (x - w / 2f) + (y - h / 2f) * (y - h / 2f));
                t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(4f - d)));
            }
            t.Apply();
            Gradients["dots"] = t;
            return t;
        }

        private static Color Sample((float at, Color c)[] stops, float p)
        {
            if (p <= stops[0].at) return stops[0].c;
            for (var i = 1; i < stops.Length; i++)
                if (p <= stops[i].at) return Color.Lerp(stops[i - 1].c, stops[i].c, (p - stops[i - 1].at) / Mathf.Max(1e-5f, stops[i].at - stops[i - 1].at));
            return stops[stops.Length - 1].c;
        }
    }
}
