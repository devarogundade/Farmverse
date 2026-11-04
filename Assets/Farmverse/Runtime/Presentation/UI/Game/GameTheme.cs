#nullable enable
using System;
using UnityEngine;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The four colour themes from the design (Meadow, Harvest, Orchard, Mix; Orchard by default).
    /// The pack's blue chrome is swapped for tinted copies (Resources/GameUi/Themes/&lt;id&gt;) and the
    /// blue colours the screens use in code go through <see cref="Tint"/>, the same rule the design used.
    /// </summary>
    public static class GameTheme
    {
        public static readonly string[] Ids = { "meadow", "harvest", "orchard", "mix" };
        public static readonly string[] Names = { "Meadow", "Harvest", "Orchard", "Mix" };

        private const string Pref = "fv_theme";
        private static string? _current;

        /// <summary>Raised after the player picks another theme: open screens rebuild.</summary>
        public static event Action? Changed;

        public static string Current
        {
            get => _current ??= Normalise(PlayerPrefs.GetString(Pref, "orchard"));
            set
            {
                var id = Normalise(value);
                if (id == Current) return;
                _current = id;
                PlayerPrefs.SetString(Pref, id);
                PlayerPrefs.Save();
                GameArt.ClearCache();
                Changed?.Invoke();
            }
        }

        private static string Normalise(string id) => Array.IndexOf(Ids, id) >= 0 ? id : "orchard";

        // hue (degrees), saturation multiplier, lightness shift: the design's tint for each theme
        private static (float h, float s, float l) Params(string id) => id switch
        {
            "meadow" => (122f, .78f, -.02f),
            "harvest" => (27f, .72f, -.04f),
            "mix" => (27f, .72f, -.04f),
            _ => (170f, .85f, -.03f),
        };

        /// <summary>A colour from the design, tinted like the theme tints the pack's blue (blues only; everything else is kept).</summary>
        public static Color Tint(Color c)
        {
            RgbToHsl(c, out var h, out var s, out var l);
            if (!(h > 180f && h < 240f && s > .2f)) return c;
            var p = Params(Current);
            var o = HslToRgb(p.h, Mathf.Clamp01(s * p.s), Mathf.Clamp01(l + p.l));
            o.a = c.a;
            return o;
        }

        /// <summary>"#rrggbb" or "#rrggbbaa", tinted.</summary>
        public static Color T(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a *= alpha;
            return Blend(Tint(c));
        }

        /// <summary>
        /// The project renders in linear colour space, where a see-through colour blends weaker (dark veils) or
        /// stronger (white glows) than the same rgba in the design's CSS. This adjusts the alpha so the result
        /// looks like the design: dark colours get 1-(1-a)^2.2, light ones a^2.2.
        /// </summary>
        public static Color Blend(Color c)
        {
            if (c.a >= 1f || c.a <= 0f || QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            var lum = .3f * c.r + .59f * c.g + .11f * c.b;
            c.a = lum < .5f ? 1f - Mathf.Pow(1f - c.a, 2.2f) : Mathf.Pow(c.a, 2.2f);
            return c;
        }

        /// <summary>A CSS opacity for a light, see-through layer (a glow), adjusted for linear blending (a^2.2).</summary>
        public static float Fade(float cssOpacity) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.Pow(Mathf.Clamp01(cssOpacity), 2.2f) : cssOpacity;

        /// <summary>Black at a CSS alpha (shadows, veils), adjusted for linear blending.</summary>
        public static Color Shade(float alpha) => Blend(new Color(0, 0, 0, alpha));

        /// <summary>White at a CSS alpha (glows, highlights), adjusted for linear blending.</summary>
        public static Color Light(float alpha) => Blend(new Color(1, 1, 1, alpha));

        /// <summary>"#rrggbb", not tinted (gold, green, red: the same in every theme).</summary>
        public static Color C(string hex, float alpha = 1f)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            c.a *= alpha;
            return Blend(c);
        }

        private static void RgbToHsl(Color c, out float h, out float s, out float l)
        {
            float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)), d = mx - mn;
            l = (mx + mn) / 2f;
            if (d < 1e-6f) { h = 0; s = 0; return; }
            s = l < .5f ? d / (mx + mn) : d / (2f - mx - mn);
            if (mx == c.r) h = ((c.g - c.b) / d % 6f + 6f) % 6f;
            else if (mx == c.g) h = (c.b - c.r) / d + 2f;
            else h = (c.r - c.g) / d + 4f;
            h *= 60f;
        }

        private static Color HslToRgb(float h, float s, float l)
        {
            var c = (1 - Mathf.Abs(2 * l - 1)) * s;
            var x = c * (1 - Mathf.Abs(h / 60f % 2f - 1));
            var m = l - c / 2;
            float r, g, b;
            switch ((int)(h / 60f) % 6)
            {
                case 0: r = c; g = x; b = 0; break;
                case 1: r = x; g = c; b = 0; break;
                case 2: r = 0; g = c; b = x; break;
                case 3: r = 0; g = x; b = c; break;
                case 4: r = x; g = 0; b = c; break;
                default: r = c; g = 0; b = x; break;
            }
            return new Color(r + m, g + m, b + m, 1f);
        }
    }
}
