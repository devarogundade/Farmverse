#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Domain.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Gameplay building blocks (gameplay.html's own CSS: .skill, .band, .bcard, .cat, .chip, .tag, .inslot,
    // .arrowR, .crop, .edge) and the pictures for goods, buildings and people.
    public sealed partial class GameUi
    {
        // ------------------------------------------------------------------ pictures

        private static readonly Dictionary<string, string> PlayGoods = new Dictionary<string, string>
        {
            ["wheat"] = "G_Wheat", ["flour"] = "G_Flour", ["tomato"] = "G_Tomato", ["sunflower"] = "G_Sunflower", ["carrot"] = "G_Carrot",
            ["milk"] = "G_Milk", ["stone"] = "G_Stone", ["wood"] = "G_Wood", ["water"] = "G_Water",
            ["egg"] = "Icon_Egg", ["pumpkin"] = "Icon_Pumkin", ["meat"] = "Icon_Food_Meat", ["coins"] = "ResourceBar_Icon_Gold",
            ["maize"] = "Paint_crop_maize", ["beans"] = "Paint_crop_beans", ["cabbage"] = "Paint_crop_onion",
            ["croissant"] = "Paint_good_bread", ["yogurt"] = "Paint_good_cheese", ["oil"] = "Paint_good_oil",
            ["juice"] = "Isl_g_juice", ["veg_box"] = "Isl_g_veg_box", ["feed"] = "Paint_res_grain", ["planks"] = "Isl_g_planks",
            ["charcoal"] = "Paint_res_charcoal", ["hay"] = "Paint_res_hay", ["manure"] = "Paint_res_manure", ["fertilizer"] = "Paint_res_fertilizer",
            ["oilcake"] = "Paint_res_oilcake", ["seed"] = "Paint_seed_wheat",
        };

        /// <summary>The picture for a good: the pack's or the painted icon, else the island's rendered model of it.</summary>
        private static string PlayGood(string item)
        {
            // the render of the pack model first (Farmverse ▸ World ▸ Render Island Pictures), then the kit's icons
            if (Resources.Load<Texture2D>("GameUi/Art/Isl_g_" + item) != null) return "Isl_g_" + item;
            if (PlayGoods.TryGetValue(item, out var a)) return a;
            return "Icon_Chest";
        }

        /// <summary>A building's picture: its rendered model.</summary>
        private static string PlayBuilding(string id) => "Isl_b_" + id;

        /// <summary>A townsperson's portrait.</summary>
        private static string PlayPerson(string who) => "Isl_p_" + (string.IsNullOrEmpty(who) ? "villager" : who);

        private static string PlayPretty(string item) => item.Length == 0 ? item : char.ToUpperInvariant(item[0]) + item.Substring(1).Replace('_', ' ');

        private static string PlayClock(double seconds)
        {
            var s = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            if (s >= 86400) return $"{s / 86400}d {s % 86400 / 3600}h";   // seasons and events last days
            return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }

        private static string PlayMinutes(double seconds)
        {
            var s = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return s >= 3600 ? $"{s / 3600}h {s % 3600 / 60}m" : s >= 60 ? $"{Mathf.CeilToInt(s / 60f)}m" : $"{s}s";
        }

        private static string PlaySeasonArt(string season) => season.ToLowerInvariant() switch
        {
            "autumn" => "G_Autumn",
            "spring" => "Paint_season_spring",
            "summer" => "Paint_season_summer",
            "winter" => "Paint_season_winter",
            _ => "G_Sunny",
        };

        private static (string art, string name) PlayWeather(GameplayRules.WeatherKind w) => w switch
        {
            GameplayRules.WeatherKind.Rain => ("G_Rain", "Rain"),
            GameplayRules.WeatherKind.Storm => ("G_Storm", "Storm"),
            GameplayRules.WeatherKind.Drought => ("Paint_wx_drought", "Drought"),
            _ => ("G_Sunny", "Sunny"),
        };

        // ------------------------------------------------------------------ shapes

        /// <summary>A round gradient (CSS radial-gradient(circle at 50% cy, a, b 70%)) for round buttons.</summary>
        private static Texture2D PlayDisc(string key, float cy, Color a, Color b, float bAt = .7f)
        {
            var reach = Mathf.Sqrt(.25f + Mathf.Max(cy, 1 - cy) * Mathf.Max(cy, 1 - cy));   // farthest corner
            return GameArt.Radial("pdisc_" + key + "_" + GameTheme.Current, .5f, cy, reach, reach, (0f, a), (bAt, b), (1f, b));
        }

        private static readonly Dictionary<string, Texture2D> PlayShapes = new Dictionary<string, Texture2D>();

        /// <summary>A solid triangle pointing right (CSS border triangle), anti-aliased.</summary>
        private static Texture2D PlayTriangle(Color c)
        {
            var key = "tri" + ColorUtility.ToHtmlStringRGBA(c);
            if (PlayShapes.TryGetValue(key, out var t) && t != null) return t;
            const int w = 64, h = 88;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var half = (h / 2f) * (1f - x / (float)w);   // the triangle's half height at this x
                var o = c;
                o.a *= Mathf.Clamp01(half - Mathf.Abs(y + .5f - h / 2f) + .5f);
                t.SetPixel(x, y, o);
            }
            t.Apply();
            PlayShapes[key] = t;
            return t;
        }

        // ------------------------------------------------------------------ parts

        /// <summary>An element at (x, y) from the left/bottom (panel px), w×h.</summary>
        private static VisualElement PlayAt(VisualElement parent, float left, float bottom, float w, float h, bool fromRight = false)
        {
            var e = PK.Box(parent);
            e.style.position = Position.Absolute;
            if (fromRight) e.style.right = left; else e.style.left = left;
            e.style.bottom = bottom;
            e.style.width = w;
            e.style.height = h;
            return e;
        }

        /// <summary>
        /// The round action button (.skill): blue disc with an ink edge, a faint inner ring and a drop shadow,
        /// the icon at half its size, the name hanging under it, an optional badge.
        /// </summary>
        private static VisualElement PlaySkill(VisualElement parent, Pt p, string icon, string? label, float x, float y, float s, Action onClick, string? badge = null, (Color a, Color b)? colours = null)
        {
            var size = p.P(s);
            var e = PlayAt(parent, x, y, size, size);
            PK.Centre(e);
            var shadow = PK.Inset(PK.Box(e), 0, p.P(6), 0, -p.P(6));
            Kit.Round(shadow, size);
            shadow.style.backgroundColor = GameTheme.Shade(.35f);
            var ring = PK.Inset(PK.Box(e));
            Kit.Round(ring, size);
            var (ca, cb) = colours ?? (GameTheme.T("#2a6fd0"), GameTheme.T("#123f7c"));
            Kit.Picture(ring, PlayDisc(ColorUtility.ToHtmlStringRGB(ca), .35f, ca, cb));
            Kit.Border(ring, p.U(5), PK.Ink);
            var inner = PK.Inset(PK.Box(ring));
            Kit.Round(inner, size);
            Kit.Border(inner, p.U(4), GameTheme.Light(.25f));
            var ic = PK.Img(e, icon, p.P(s * .5f));
            ic.style.position = Position.Relative;
            if (label != null)
            {
                var l = PK.T(e, p, label, 18, align: TextAnchor.MiddleCenter);
                PK.Abs(l, left: 0, right: 0, bottom: -p.P(14));
            }

            if (badge != null) PK.Abs(PK.Badge(e, p, badge, false, 28), right: p.P(4), top: p.P(4));
            PK.Tap(e, p, onClick);
            return e;
        }

        /// <summary>The coin or gem pill at the HUD's top right (.pill, design sizes in points), as wide as its value.</summary>
        private static (VisualElement pill, Label value) PlayPill(VisualElement? parent, Pt p, bool gem, Action? onPlus)
        {
            var pill = PK.Row(parent);
            pill.style.justifyContent = Justify.FlexEnd;
            pill.style.height = p.P(48);
            pill.style.flexShrink = 0;
            pill.style.paddingLeft = p.U(4) + p.P(40) + p.P(14);   // clear of the plus button
            pill.style.paddingRight = p.P(60);                      // clear of the hanging icon
            Kit.Round(pill, p.U(10));
            PK.Grad(pill, gem ? "ppillgem" : "ppillgold", true, gem
                ? new[] { (0f, GameTheme.C("#170a2c")), (1f, GameTheme.C("#221040")) }
                : new[] { (0f, GameTheme.C("#170d06")), (1f, GameTheme.C("#2b160a")) });
            var plus = PK.Img(pill, gem ? "ResourceBar_Btn_Single_Purple" : "ResourceBar_Btn_Single_Yellow", p.P(40));
            PK.Abs(plus, left: p.U(4), top: p.P(4));
            PK.Centre(plus);
            PK.Img(plus, "ResourceBar_Btn_Icon_Add", p.P(21));
            var t = PK.T(pill, p, "0", 23);
            var ico = PK.Img(pill, gem ? "ResourceBar_Icon_Gem" : "ResourceBar_Icon_Gold", p.P(58));
            PK.Abs(ico, right: -p.P(10), top: -p.P(5));
            if (onPlus != null) PK.Tap(plus, p, onPlus);
            return (pill, t);
        }

        /// <summary>The dark chip (.chip): navy see-through pill with an ink edge.</summary>
        private static VisualElement PlayChip(VisualElement? parent, Pt p, float h, float padL, float padR)
        {
            var c = PK.Row(parent);
            c.style.height = p.P(h);
            c.style.flexShrink = 0;
            PK.Pad(c, p.P(padL), 0, p.P(padR), 0);
            c.style.backgroundColor = GameTheme.T("#061a40", .82f);
            Kit.Border(c, p.U(3), PK.Ink);
            Kit.Round(c, p.P(h));
            return c;
        }

        /// <summary>Centred horizontally at the top (CSS left:50%; transform:translateX(-50%)).</summary>
        private static T PlayTopCentre<T>(T e, float top) where T : VisualElement
        {
            e.style.position = Position.Absolute;
            e.style.left = new Length(50, LengthUnit.Percent);
            e.style.top = top;
            e.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            return e;
        }

        /// <summary>The small tag (.tag): Nunito on a coloured rounded box.</summary>
        private static Label PlayTag(VisualElement? parent, Pt p, string text, float fs, Color bg, Color fg)
        {
            var l = PK.N(parent, p, text, fs, fg);
            l.style.backgroundColor = bg;
            PK.Pad(l, p.P(8), p.P(2), p.P(8), p.P(2));
            Kit.Round(l, p.U(6));
            l.style.alignSelf = Align.FlexStart;
            return l;
        }

        /// <summary>A goods slot (.inslot): ink square, the good, its count at the corner (red when short).</summary>
        private static (VisualElement slot, Label count) PlayInSlot(VisualElement? parent, Pt p, string art, string count, float s, float icon, bool shortOf = false, bool gold = false)
        {
            var e = PK.Centre(PK.Box(parent));
            PK.Size(e, p.P(s), p.P(s));
            e.style.backgroundColor = PK.Ink;
            Kit.Round(e, p.U(12));
            if (gold) Kit.Border(e, p.U(3), GameTheme.C("#ffd23a"));
            PK.Fit(e, art, p.P(icon), p.P(icon));
            var q = PK.T(e, p, count, 20, shortOf ? GameTheme.C("#ff7a8c") : (Color?)null);
            PK.Abs(q, right: p.P(6), bottom: p.P(2));
            return (e, q);
        }

        /// <summary>The yellow arrow (.arrowR) pointing right, w×h points.</summary>
        private static VisualElement PlayArrow(VisualElement? parent, Pt p, float w, float h)
        {
            var e = PK.Box(parent);
            PK.Size(e, p.P(w), p.P(h));
            Kit.Picture(e, PlayTriangle(GameTheme.C("#ffd23a")));
            return e;
        }

        /// <summary>
        /// The full-width bottom band (.band): navy with the pack's full-width frame, the close button at the
        /// top right. Returns the content box (a row filling the band).
        /// </summary>
        private VisualElement PlayBand(VisualElement root, Pt p, float h, Action? onClose, float padTop = 18, float side = 24, float bottom = 14)
        {
            var band = PK.Box(root);
            band.pickingMode = PickingMode.Position;
            PK.Abs(band, left: 0, right: 0, bottom: 0, height: p.P(h));
            band.style.backgroundColor = GameTheme.T("#0b2a52");
            Kit.Nine(band, GameArt.Get("Popup_FullWidth03_Single_Navy"), 4, 20, 4, 20, p.U(14) / 20f);
            PK.Pad(band, p.L + p.P(side), p.P(padTop), p.R + p.P(side), p.B + p.P(bottom));
            if (onClose != null)
            {
                var x = PK.Abs(PK.Centre(PK.Box(band)), top: -p.P(30), width: p.P(64), height: p.P(70), right: p.R + p.P(16));
                Kit.Picture(x, GameArt.Get("Button_Square02_Gray"));
                PK.Img(x, "Icon_PictoIcon_Close", p.P(64) * .52f * 53f / 49f, p.P(64) * .52f).style.marginTop = -p.P(5);
                PK.Tap(x, p, onClose, "whoosh");
            }

            var row = PK.Row(band);
            row.style.flexGrow = 1;
            row.style.alignItems = Align.Stretch;
            return row;
        }

        /// <summary>A gray square close button (in a flex row), w×h points.</summary>
        private static VisualElement PlayClose(VisualElement parent, Pt p, float w, float h, Action onClose)
        {
            var x = PK.Centre(PK.Box(parent));
            PK.Size(x, p.P(w), p.P(h));
            Kit.Picture(x, GameArt.Get("Button_Square02_Gray"));
            PK.Img(x, "Icon_PictoIcon_Close", p.P(w) * .52f * 53f / 49f, p.P(w) * .52f).style.marginTop = -p.P(w) * .08f;
            PK.Tap(x, p, onClose, "whoosh");
            return x;
        }

        /// <summary>A button with an icon before its label (the mockups' btn with an &lt;img&gt;).</summary>
        private static VisualElement PlayBtn(VisualElement? parent, Pt p, string colour, string? icon, string label, float w, float h, float fs, Action? onClick)
        {
            var b = PK.Btn(parent, p, colour, null, w, h, fs, onClick);
            if (icon != null)
            {
                var i = PK.Img(b, icon, p.P(h) * .62f);
                i.style.marginRight = p.P(fs * .3f);
            }

            PK.T(b, p, label, fs);
            return b;
        }

        /// <summary>Greys a button out (it stays where it is; presses do nothing).</summary>
        private static void PlayEnable(VisualElement e, bool on)
        {
            e.SetEnabled(on);
            e.style.opacity = on ? 1f : .5f;
        }

        /// <summary>A rounded bar (world-space health and progress): ink, a coloured fill. Returns the fill.</summary>
        private static VisualElement PlayThinBar(VisualElement parent, float w, float h, Color fill, float border = 0)
        {
            var bar = PK.Box(parent);
            PK.Size(bar, w, h);
            bar.style.backgroundColor = PK.Ink;
            Kit.Round(bar, h / 2f);
            if (border > 0) Kit.Border(bar, border, Color.black);
            PK.Clip(bar);
            var f = PK.Abs(PK.Box(bar), left: 0, top: 0, bottom: 0);
            f.style.backgroundColor = fill;
            return f;
        }
    }
}
