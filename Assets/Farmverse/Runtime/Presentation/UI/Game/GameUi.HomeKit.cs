#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Small helpers shared by the Home, Loading, System and profile-extra screens (prefix "Home").
    public sealed partial class GameUi
    {
        /// <summary>Stroked text with the mockup's explicit stroke width (--sw, design points × k).</summary>
        private static Label HomeTS(VisualElement? parent, Pt p, string text, float fs, float sw, Color? colour = null)
        {
            var l = PK.T(parent, p, text, fs, colour);
            var w = p.P(sw);
            l.style.unityTextOutlineWidth = w;
            l.style.textShadow = sw <= 0
                ? new TextShadow { offset = Vector2.zero, blurRadius = 0, color = Color.clear }
                : new TextShadow { offset = new Vector2(0, w * .7f), blurRadius = 0, color = PK.Ink };
            l.enableRichText = true;
            return l;
        }

        /// <summary>Text line placed at its top-left (line-height 1, like the mockups' absolute spans).</summary>
        private static Label HomeTAt(VisualElement parent, Pt p, float left, float top, string text, float fs, float sw, Color? colour = null)
        {
            var l = HomeTS(parent, p, text, fs, sw, colour);
            PK.Abs(l, left: left, top: top, height: p.P(fs));
            return l;
        }

        /// <summary>The system popups' message (.msg): Nunito 900, centred, wrapping; &lt;b&gt; is gold, &lt;br&gt; a new line.</summary>
        private static Label HomeMsg(VisualElement parent, Pt p, string html, float fs, string colour = "#dff1ff")
        {
            var l = PK.N(parent, p, HomeRich(html), fs, GameTheme.T(colour), TextAnchor.MiddleCenter, wrap: true);
            l.enableRichText = true;
            return l;
        }

        /// <summary>Mockup HTML to UI Toolkit rich text.</summary>
        private static string HomeRich(string html) => html
            .Replace("<br>", "\n")
            .Replace("<b>", "<color=#ffd23a>").Replace("</b>", "</color>");

        /// <summary>A box nine-sliced with literal design pixels for the borders (CSS border-image "/ Npx").</summary>
        private static T HomeNine<T>(T e, Pt p, string art, int top, int right, int bottom, int left, float topPx) where T : VisualElement
        {
            Kit.Nine(e, GameArt.Get(art), left, top, right, bottom, p.U(topPx) / top);
            return e;
        }

        /// <summary>The pack spinner (Loading_Rotate on a navy disc), turning.</summary>
        private static VisualElement HomeSpinner(VisualElement? parent, Pt p, float size, Ctx c)
        {
            var s = PK.Box(parent);
            PK.Size(s, size, size);
            s.style.backgroundColor = GameTheme.T("#1a3878");
            Kit.Round(s, size / 2f);
            Kit.Border(s, p.U(3), PK.Ink);
            var img = PK.Inset(PK.Box(s), size * .2f - p.U(3), size * .2f - p.U(3), size * .2f - p.U(3), size * .2f - p.U(3));
            Kit.Contain(img, GameArt.Get("Loading_Rotate"));
            c.Live(() => img.style.rotate = new Rotate(new Angle(Time.unscaledTime * 360f % 360f, AngleUnit.Degree)));
            return s;
        }

        /// <summary>Gentle pulse (CSS @keyframes pulse: scale to <paramref name="amount"/> and back every <paramref name="period"/> s).</summary>
        private static void HomePulse(Ctx c, VisualElement e, float amount = 1.06f, float period = 1.2f)
        {
            c.Live(() =>
            {
                var t = (1f - Mathf.Cos(Time.unscaledTime / period * Mathf.PI * 2f)) / 2f;
                var s = Mathf.Lerp(1f, amount, t);
                e.style.scale = new Scale(new Vector3(s, s, 1));
            });
        }

        /// <summary>A plain dark veil of its own (where the design's dim differs from the popup veil).</summary>
        private static VisualElement HomeDim(VisualElement root, string colour, float alpha)
        {
            var d = PK.Inset(PK.Box(root));
            d.style.backgroundColor = GameTheme.T(colour, alpha);
            return d;
        }

        /// <summary>Today's login reward is still to collect (the server's state/daily).</summary>
        private static bool HomeDailyReady => Wallet.Shop?.Daily is not { } d || d.LoginOn != ShopUtcDay;

        /// <summary>m:ss under an hour, else h:mm.</summary>
        private static string HomeClock(double seconds)
        {
            var s = Math.Max(0, (int)seconds);
            if (s >= 86400) return $"{s / 86400}d {s % 86400 / 3600}h";
            return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        private static Texture2D? _homeTri;

        /// <summary>A right-pointing triangle (the speech bubble's "next" arrow), white: tint with unityBackgroundImageTintColor.</summary>
        private static Texture2D HomeTri()
        {
            if (_homeTri != null) return _homeTri;
            const int w = 48, h = 64;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var half = (1f - x / (float)w) * h / 2f;   // the triangle's half-height at this x
                var a = Mathf.Clamp01(half - Mathf.Abs(y + .5f - h / 2f) + .5f);
                t.SetPixel(x, y, new Color(1, 1, 1, a));
            }
            t.Apply();
            _homeTri = t;
            return t;
        }
    }
}
