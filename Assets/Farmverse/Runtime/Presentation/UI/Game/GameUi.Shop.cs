#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Farmverse.Contracts;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // The Shop page (design: index.html, the pack's Shop screens): header, the tab bar (Offers · Gems · Coins ·
    // Build · Boosts) and one sideways strip of section panels with card grids. Tablet: two card rows; phone:
    // one. The design's sizes are literal points per device (no k), so everything here goes through Pt.U.
    public sealed partial class GameUi
    {
        private static readonly bool RegShop_Page = Register("shop", UiLayer.Page, (ui, c) => ui.ShopPage(c));

        private int _shopLiveFrame = -10;
        private float _shopScroll;

        /// <summary>The design's per-device numbers (points).</summary>
        private sealed class ShopCfg
        {
            public float hh, th, tfs, safeL, safeR, safeB, pad, sgap, spad, hfs, hlh, gap, cw, cwTall, nfs, ph, bw, r, pgap;
            public int rows;
            public bool tabIcons;
        }

        private static ShopCfg ShopCfgFor(Pt p) => p.Phone
            ? new ShopCfg { hh = 52, th = 40, tfs = 17, tabIcons = false, safeL = 0, safeR = 0, safeB = 0, pad = 10, sgap = 12, spad = 10, hfs = 13, hlh = 26, rows = 1, gap = 10, cw = 150, cwTall = 190, nfs = 25, ph = 40, bw = 14, r = 12, pgap = 14 }
            : new ShopCfg { hh = 96, th = 70, tfs = 30, tabIcons = true, safeL = 0, safeR = 24, safeB = 0, pad = 24, sgap = 20, spad = 18, hfs = 22, hlh = 46, rows = 2, gap = 16, cw = 236, cwTall = 300, nfs = 40, ph = 64, bw = 22, r = 16, pgap = 22 };

        /// <summary>One card of the strip.</summary>
        private sealed class ShopItem
        {
            public string kind = "card";   // card | hero | tall
            public string frame = "b";     // y p b g5 p5
            public string? head, name, top, sub, art, qty, price, coin, timer, ribbon, locked;
            public int? gem;
            public bool glow, render, pop, crown, info, free, brk;
            public Texture2D? picture;
            public (string icon, string value)[]? chips;
            public Action? tap;
        }

        private sealed class ShopSection
        {
            public string id = "", tab = "", icon = "", title = "";
            public int dot;
            public List<ShopItem> items = new List<ShopItem>();
        }

        // ------------------------------------------------------------------ page

        private void ShopPage(Ctx c)
        {
            var p = c.P;
            var cfg = ShopCfgFor(p);
            float U(float n) => p.U(n);
            var L = p.L + U(cfg.safeL);
            var R = p.R + U(cfg.safeR);
            var B = p.B + U(cfg.safeB);
            var sections = ShopSections();

            // background (.screen: radial blue, faint dots)
            var screen = PK.Inset(PK.Box(c.Root));
            screen.pickingMode = PickingMode.Position;
            Kit.Picture(screen, GameArt.Radial("shopbg_" + GameTheme.Current, .5f, .1f, 1.2f, .9f,
                (0f, GameTheme.T("#1572c6")), (.45f, GameTheme.T("#0d5aa4")), (1f, GameTheme.T("#0a4888"))));
            var dots = PK.Inset(PK.Box(screen));
            dots.style.opacity = .10f;
            Kit.Picture(dots, GameArt.Dots());
            dots.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
            dots.style.backgroundSize = new BackgroundSize(new Length(U(120)), new Length(U(120)));
            screen.style.flexDirection = FlexDirection.Column;

            // header
            var (coins, gems) = ShopHeader(screen, p, cfg, L, R,
                () => ShopScrollTo("coins"), () => ShopScrollTo("gems"));
            c.Live(() =>
            {
                coins.text = Loc.Of(PK.Num(Economy?.Coins ?? 0));
                gems.text = Loc.Of(PK.Num(Wallet.Gems));
            });

            // tabs
            var tabs = PK.Row(screen);
            tabs.style.height = U(cfg.th) + U(3);
            tabs.style.flexShrink = 0;
            tabs.style.backgroundColor = GameTheme.T("#0b3e74");
            tabs.style.borderBottomWidth = U(3);
            tabs.style.borderBottomColor = GameTheme.T("#082b52");
            tabs.style.paddingLeft = L;
            tabs.style.paddingRight = R;
            var tabParts = new List<(string id, Label label, VisualElement? icon, VisualElement line)>();
            foreach (var s in sections)
            {
                var tab = PK.Centre(PK.Row(tabs));
                tab.style.flexGrow = 1;
                tab.style.flexBasis = 0;
                tab.style.height = new Length(100, LengthUnit.Percent);
                VisualElement? icon = null;
                if (cfg.tabIcons)
                {
                    icon = PK.Img(tab, s.icon, U(cfg.th * .62f));
                    icon.style.marginRight = U(8);
                }

                var label = PK.TL(tab, p, s.tab, cfg.tfs, 4);
                if (s.dot > 0)
                {
                    var dot = PK.Centre(PK.Box(tab));
                    PK.Size(dot, U(cfg.th * .3f), U(cfg.th * .3f));
                    dot.style.top = -U(cfg.tfs * .55f);
                    dot.style.marginLeft = -U(6) + U(8);
                    dot.style.backgroundColor = GameTheme.C("#ff3a52");
                    Kit.Round(dot, U(cfg.th));
                    Kit.Border(dot, U(2), Color.white);
                    var dl = PK.TL(dot, p, s.dot.ToString(CultureInfo.InvariantCulture), cfg.th * .2f, 0.01f);
                    dl.style.unityTextOutlineWidth = 0;
                    dl.style.textShadow = new TextShadow();
                }

                var line = PK.Abs(PK.Box(tab), bottom: -U(3), height: Mathf.Max(U(4), U(cfg.th * .09f)));
                line.style.left = new Length(24, LengthUnit.Percent);
                line.style.right = new Length(24, LengthUnit.Percent);
                line.style.backgroundColor = Color.white;
                Kit.Round(line, U(6), U(6), 0, 0);
                var id = s.id;
                PK.Tap(tab, p, () => ShopScrollTo(id));
                tabParts.Add((s.id, label, icon, line));
            }

            // the strip
            var bodyH = p.H - U(cfg.hh) - U(cfg.th) - U(3) - U(cfg.pad) * 2 - B;
            // the phone layout keeps one card per column, but a browser window is wide *and* tall enough
            // for two: stack them whenever each would still be ~120 points high
            var rows = cfg.rows;
            if (p.Phone && bodyH / p.Unit - cfg.hlh - cfg.spad >= 2 * 120 + cfg.gap) rows = 2;
            var sv = ShopScroller(screen, true, p.P(12));
            var strip = sv.contentContainer;
            strip.style.flexDirection = FlexDirection.Row;
            strip.style.paddingLeft = L + U(cfg.pad);
            strip.style.paddingRight = R + U(cfg.pad);
            strip.style.paddingTop = U(cfg.pad);
            strip.style.paddingBottom = U(cfg.pad) + B;
            var secEls = new List<(string id, VisualElement el)>();
            for (var i = 0; i < sections.Count; i++)
            {
                var s = sections[i];
                var sec = PK.Col(strip);
                sec.style.flexShrink = 0;
                sec.style.height = bodyH;
                sec.style.backgroundColor = GameTheme.T("#052656", .45f);
                Kit.Round(sec, U(cfg.r * 1.4f));
                PK.Pad(sec, U(cfg.spad), 0, U(cfg.spad), U(cfg.spad));
                if (i > 0) sec.style.marginLeft = U(cfg.sgap);
                var h3 = PK.Centre(PK.Box(sec));
                h3.style.height = U(cfg.hlh);
                h3.style.flexShrink = 0;
                PK.TL(h3, p, s.title, cfg.hfs, 3).style.color = GameTheme.T("#cfe3ff");
                var grid = PK.Row(sec);
                grid.style.alignItems = Align.Stretch;
                grid.style.flexGrow = 1;
                var first = true;
                foreach (var col in ShopColumns(s.items, rows))
                {
                    var colEl = PK.Col(grid);
                    colEl.style.flexShrink = 0;
                    colEl.style.width = U(col.tall ? cfg.cwTall : cfg.cw);
                    if (!first) colEl.style.marginLeft = U(cfg.gap);
                    first = false;
                    for (var k = 0; k < col.items.Count; k++)
                    {
                        var card = ShopCard(colEl, p, cfg, col.items[k]);
                        if (k > 0) card.style.marginTop = U(cfg.gap);
                    }
                }

                secEls.Add((s.id, sec));
            }

            var tail = PK.Box(strip);
            PK.Size(tail, 1, 1);

            // tabs follow the strip; a fresh open jumps to the asked tab, a rebuild keeps the place
            void ScrollTo(string id)
            {
                var hit = secEls.FirstOrDefault(x => x.id == id);
                if (hit.el != null) sv.scrollOffset = new Vector2(Mathf.Max(0, hit.el.layout.x - (L + U(cfg.pad))), 0);
            }

            _shopScrollTo = ScrollTo;
            var fresh = Time.frameCount - _shopLiveFrame > 2;   // not on screen just before: a new visit
            if (fresh) foreach (var (_, secEl) in secEls) Kit.Stagger(secEl, U(30), 6);   // the sections slide up in turn
            var want = c.Args as string ?? "offers";
            sv.schedule.Execute(() =>
            {
                if (fresh) ScrollTo(want);
                else sv.scrollOffset = new Vector2(Mathf.Max(0, _shopScroll), 0);
            }).ExecuteLater(1);

            c.Live(() =>
            {
                _shopLiveFrame = Time.frameCount;
                if (sv.contentViewport.layout.width <= 0) return;
                _shopScroll = sv.scrollOffset.x;
                var cur = sections[0].id;
                foreach (var (id, el) in secEls)
                    if (el.layout.x - sv.scrollOffset.x < sv.contentViewport.layout.width * .45f) cur = id;
                if (sv.scrollOffset.x + sv.contentViewport.layout.width >= strip.layout.width - 4) cur = sections[sections.Count - 1].id;
                foreach (var (id, label, icon, line) in tabParts)
                {
                    var on = id == cur;
                    label.style.color = on ? Color.white : GameTheme.T("#3cb4ff");
                    line.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                    if (icon != null) ShopDull(icon, !on);
                }
            });
        }

        private Action<string>? _shopScrollTo;

        private void ShopScrollTo(string id) => _shopScrollTo?.Invoke(id);

        /// <summary>The shop's own header (taller pills than the kit's): back, title, coins, gems, home. Returns the value labels.</summary>
        private (Label coins, Label gems) ShopHeader(VisualElement screen, Pt p, ShopCfg cfg, float L, float R, Action plusCoins, Action plusGems)
        {
            float U(float n) => p.U(n);
            var hh = cfg.hh;
            var bar = PK.Row(screen);
            bar.style.height = U(hh);
            bar.style.flexShrink = 0;
            bar.style.paddingRight = R;
            PK.Grad(bar, "shophdr", true, (0f, GameTheme.T("#0c3f78")), (1f, GameTheme.T("#0b3a6e")));

            var back = PK.Centre(PK.Box(bar));
            PK.Size(back, U(hh * 1.25f) + L, U(hh * .8f));
            back.style.paddingLeft = L;
            Kit.Flush(back, true);
            PK.Img(back, "Icon_PictoIcon_Back", U(hh * .8f) * .52f);
            PK.Tap(back, p, Back, "whoosh");

            PK.TL(bar, p, "Shop", hh * .48f, 5).style.marginLeft = U(hh * .35f);
            PK.Grow(bar);

            Label Pill(bool gem, Action plus)
            {
                var pill = PK.Row(bar);
                pill.style.height = U(hh * .56f);
                pill.style.flexShrink = 0;
                pill.style.marginLeft = U(cfg.pgap);
                PK.Pad(pill, U(hh * .66f), 0, U(hh * .62f), 0);
                Kit.Round(pill, U(10));
                PK.Grad(pill, gem ? "shoppillgem" : "shoppillgold", true, gem
                    ? new[] { (0f, GameTheme.C("#170a2c")), (1f, GameTheme.C("#221040")) }
                    : new[] { (0f, GameTheme.C("#170d06")), (1f, GameTheme.C("#2b160a")) });
                var pl = PK.Img(pill, gem ? "ResourceBar_Btn_Single_Purple" : "ResourceBar_Btn_Single_Yellow", U(hh * .46f));
                PK.Abs(pl, left: U(4), top: (U(hh * .56f) - U(hh * .46f)) / 2f);
                PK.Centre(pl);
                PK.Img(pl, "ResourceBar_Btn_Icon_Add", U(hh * .22f));
                PK.Tap(pl, p, plus);
                var v = PK.TL(pill, p, "0", hh * .3f, 3);
                var ico = PK.Img(pill, gem ? "ResourceBar_Icon_Gem" : "ResourceBar_Icon_Gold", U(hh * .7f));
                PK.Abs(ico, right: -U(hh * .18f), top: (U(hh * .56f) - U(hh * .7f)) / 2f);
                return v;
            }

            var coins = Pill(false, plusCoins);
            var gems = Pill(true, plusGems);
            var home = PK.Img(bar, "Icon_PictoIcon_Home", U(hh * .6f));
            home.style.marginLeft = U(cfg.pgap * 1.1f);
            PK.Tap(home, p, () => Go("home"), "whoosh");
            return (coins, gems);
        }

        /// <summary>The design's column packing: runs of small cards fill columns of <paramref name="rows"/>; hero/tall cards get a column each.</summary>
        private static List<(bool tall, List<ShopItem> items)> ShopColumns(List<ShopItem> items, int rows)
        {
            var output = new List<(bool, List<ShopItem>)>();
            var run = new List<ShopItem>();

            void Flush()
            {
                if (run.Count == 0) return;
                var nc = (run.Count + rows - 1) / rows;
                for (var c = 0; c < nc; c++)
                {
                    var st = new List<ShopItem>();
                    for (var r = 0; r < rows; r++)
                    {
                        var ix = r * nc + c;
                        if (ix < run.Count) st.Add(run[ix]);
                    }

                    output.Add((false, st));
                }

                run = new List<ShopItem>();
            }

            foreach (var it in items)
            {
                if (it.brk) Flush();
                if (it.kind != "card")
                {
                    Flush();
                    output.Add((true, new List<ShopItem> { it }));
                }
                else run.Add(it);
            }

            Flush();
            return output;
        }

        // ------------------------------------------------------------------ card

        private VisualElement ShopCard(VisualElement col, Pt p, ShopCfg cfg, ShopItem it)
        {
            float U(float n) => p.U(n);
            var bw = cfg.bw;
            var nfs = cfg.nfs;
            var hero = it.frame == "g5" || it.frame == "p5";
            var card = PK.Col(col);
            card.style.flexGrow = 1;
            card.style.flexBasis = 0;
            card.pickingMode = PickingMode.Position;
            var frameArt = it.frame switch
            {
                "y" => "CardFrame06_Bg_Yellow",
                "p" => "CardFrame06_Bg_Purple",
                "g5" => "CardFrame05_Bg_Green",
                "p5" => "CardFrame05_Bg_Purple",
                _ => "CardFrame06_Bg_Blue",
            };
            if (hero) Kit.Nine(card, GameArt.Get(frameArt), 27, 60, 27, 22, U(bw * 2.2f) / 60f);
            else Kit.Nine(card, GameArt.Get(frameArt), 28, 40, 28, 20, U(bw * 1.4f) / 40f);

            // .in
            var inn = PK.Inset(PK.Col(card));
            PK.Pad(inn, U(bw * .3f), hero ? U(bw * 2.2f) : U(bw * .35f), U(bw * .3f), U(bw * .32f));
            if (it.info)
            {
                var info = PK.Img(inn, "Icon_ImageIcon_Info", float.NaN, U(nfs * 1.05f));
                var t = GameArt.Get("Icon_ImageIcon_Info");
                info.style.height = U(nfs * 1.05f) * (t != null && t.width > 0 ? t.height / (float)t.width : 1f);
                PK.Abs(info, right: U(bw * .35f), top: U(bw * 2.4f));
            }

            if (it.top != null)
            {
                var top = PK.TL(inn, p, it.top, nfs, 5);
                top.style.unityTextAlign = TextAnchor.MiddleCenter;
                top.style.paddingTop = U(bw * .25f);
            }

            if (it.name != null)
            {
                var name = PK.TL(inn, p, it.name, nfs * .72f, 4);
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                name.style.paddingTop = U(bw * .3f);
            }

            if (it.sub != null)
            {
                // wraps inside the card (a long line used to run out over the cards next to it)
                var sub = PK.N(inn, p, it.sub, 1, GameTheme.Light(.92f), TextAnchor.MiddleCenter, wrap: true);
                sub.style.fontSize = U(nfs * .42f);
                sub.style.whiteSpace = WhiteSpace.Normal;
                sub.style.maxWidth = new Length(100, LengthUnit.Percent);
                sub.style.flexShrink = 0;
                sub.style.paddingLeft = U(bw * .3f);
                sub.style.paddingRight = U(bw * .3f);
                sub.style.marginTop = U(2);
                sub.style.textShadow = new TextShadow { offset = new Vector2(0, U(1)), color = GameTheme.Shade(.35f) };
            }

            // art
            var art = PK.Box(inn);
            art.style.flexGrow = 1;
            art.style.minHeight = 0;
            if (it.glow)
            {
                var glow = PK.Abs(PK.Box(art));
                Kit.Contain(glow, GameArt.Get("CardFrame06_Glow"));
                glow.style.opacity = GameTheme.Fade(.95f);
                if (it.frame == "y") glow.style.unityBackgroundImageTintColor = new Color(1f, .82f, .35f, 1f);
                art.RegisterCallback<GeometryChangedEvent>(e =>
                {
                    var s = e.newRect.width * 1.5f;
                    glow.style.width = s;
                    glow.style.height = s;
                    glow.style.left = (e.newRect.width - s) / 2f;
                    glow.style.top = (e.newRect.height - s) / 2f;
                });
            }

            if (it.render && it.picture == null)
            {
                var slot = PK.Box(art);
                slot.style.position = Position.Absolute;
                slot.style.left = new Length(12, LengthUnit.Percent);
                slot.style.right = new Length(12, LengthUnit.Percent);
                slot.style.top = new Length(6, LengthUnit.Percent);
                slot.style.bottom = new Length(6, LengthUnit.Percent);
                Kit.Round(slot, U(14));
                Kit.Picture(slot, GameArt.Radial("shopslot", .5f, .4f, .7f, .7f, (0f, GameTheme.Light(.28f)), (1f, GameTheme.Light(.04f))));
                Kit.Border(slot, U(2), GameTheme.Light(.35f));
                var tag = PK.N(art, p, "3D RENDER", 1, GameTheme.C("#0b3d74"));
                tag.style.fontSize = U(nfs * .3f);
                tag.style.backgroundColor = GameTheme.Light(.85f);
                Kit.Round(tag, U(5));
                PK.Pad(tag, U(5), U(1), U(5), U(1));
                tag.style.position = Position.Absolute;
                tag.style.right = new Length(6, LengthUnit.Percent);
                tag.style.top = new Length(4, LengthUnit.Percent);
            }

            if (it.art != null || it.picture != null)
            {
                var main = PK.Box(art);
                main.style.position = Position.Absolute;
                var mw = it.render && it.picture == null ? 52f : 80f;
                var mh = it.render && it.picture == null ? 62f : 88f;
                main.style.left = new Length((100 - mw) / 2, LengthUnit.Percent);
                main.style.right = new Length((100 - mw) / 2, LengthUnit.Percent);
                main.style.top = new Length((100 - mh) / 2, LengthUnit.Percent);
                main.style.bottom = new Length((100 - mh) / 2, LengthUnit.Percent);
                if (it.picture != null) Kit.Contain(main, it.picture);
                else Kit.Contain(main, GameArt.Get(it.art!));
                if (it.render) main.style.opacity = .95f;
            }

            if (it.qty != null)
            {
                var q = PK.TL(art, p, it.qty, nfs * .8f, 5);
                q.style.position = Position.Absolute;
                q.style.right = new Length(10, LengthUnit.Percent);
                q.style.bottom = new Length(6, LengthUnit.Percent);
            }

            // chips
            if (it.chips != null)
            {
                var chips = PK.Row(inn);
                chips.style.justifyContent = Justify.Center;
                chips.style.flexWrap = Wrap.Wrap;
                PK.Pad(chips, U(4), 0, U(4), U(bw * .35f));
                var firstChip = true;
                foreach (var (icon, value) in it.chips)
                {
                    var chip = PK.Row(chips);
                    if (!firstChip) chip.style.marginLeft = U(bw * .4f);
                    firstChip = false;
                    chip.style.backgroundColor = GameTheme.Shade(.18f);
                    Kit.Round(chip, U(8));
                    PK.Pad(chip, U(4), U(3), U(8), U(3));
                    PK.Img(chip, icon, U(nfs * .85f)).style.marginRight = U(3);
                    PK.TL(chip, p, value, nfs * .58f, 3);
                }
            }

            // price strip
            if (it.price != null || it.gem != null || it.coin != null || it.timer != null || it.free)
            {
                var price = PK.Centre(PK.Row(inn));
                price.style.height = U(cfg.ph);
                price.style.flexShrink = 0;
                price.style.marginLeft = -U(bw * .08f);
                price.style.marginRight = -U(bw * .08f);
                Kit.Round(price, 0, 0, U(cfg.r * .7f), U(cfg.r * .7f));
                var green = it.free;
                if (green) PK.Grad(price, "shopfree", true, (0f, GameTheme.C("#58d600")), (1f, GameTheme.C("#3fae00")));
                else
                {
                    price.style.backgroundColor = it.frame == "y" ? new Color(214 / 255f, 110 / 255f, 0, .22f) : GameTheme.Shade(.10f);
                    price.style.borderTopWidth = U(2);
                    price.style.borderTopColor = GameTheme.Light(.12f);
                }

                var ph = U(cfg.ph);
                if (it.price != null) PK.TL(price, p, it.price, nfs * .9f, 5);
                else if (it.gem != null)
                {
                    PK.Img(price, "ResourceBar_Icon_Gem", ph * .7f).style.marginRight = U(6);
                    PK.TL(price, p, it.gem.Value.ToString(CultureInfo.InvariantCulture), nfs * .95f, 5);
                }
                else if (it.coin != null)
                {
                    PK.Img(price, "ResourceBar_Icon_Gold", ph * .62f).style.marginRight = U(6);
                    PK.TL(price, p, it.coin, nfs * .95f, 5);
                }
                else if (it.free) PK.TL(price, p, "FREE", nfs * .95f, 5);
                else if (it.timer != null)
                {
                    PK.Img(price, "Icon_PictoIcon_Timer", ph * .55f).style.marginRight = U(6);
                    PK.TL(price, p, it.timer, nfs * .65f, 5);
                }
            }

            // stickers over the card
            if (it.head != null)
            {
                var head = PK.Centre(PK.Row(card));
                PK.Abs(head, left: 0, top: 0, right: 0, height: U(bw * 2.05f));
                var hl = PK.TL(head, p, it.head, nfs * .5f, 3);
                hl.enableRichText = true;
            }

            if (it.pop)
            {
                var b = PK.Img(card, "Icon_ImageIcon_Badge_Best", float.NaN, U(nfs * 1.9f));
                var t = GameArt.Get("Icon_ImageIcon_Badge_Best");
                b.style.height = U(nfs * 1.9f) * (t != null && t.width > 0 ? t.height / (float)t.width : 1f);
                PK.Abs(b, left: -U(nfs * .6f), top: -U(nfs * .6f));
            }

            if (it.crown)
            {
                var b = PK.Img(card, "Icon_ImageIcon_Badge_Popular", float.NaN, U(nfs * 1.9f));
                var t = GameArt.Get("Icon_ImageIcon_Badge_Popular");
                b.style.height = U(nfs * 1.9f) * (t != null && t.width > 0 ? t.height / (float)t.width : 1f);
                PK.Abs(b, left: -U(nfs * .45f), top: -U(nfs * .4f));
            }

            if (it.ribbon != null)
            {
                var rb = PK.Row(card);
                PK.Abs(rb, left: -U(bw * .45f), bottom: U(cfg.ph) + U(bw * .2f) + U(bw * .32f), height: U(nfs * 1.15f));
                PK.Pad(rb, U(nfs * .4f), 0, U(nfs * .55f), U(nfs * .12f));
                Kit.Nine(rb, GameArt.Get("Label_Ribbon_Single_Orange"), 12, 14, 24, 26, U(8) / 14f);
                PK.TL(rb, p, it.ribbon, nfs * .55f, 3);
            }

            if (it.locked != null)
            {
                var lk = PK.Centre(PK.Inset(PK.Col(card)));
                lk.pickingMode = PickingMode.Position;
                lk.style.backgroundColor = GameTheme.T("#0a142d", .35f);
                Kit.Round(lk, U(cfg.r));
                var lw = U((it.kind == "card" ? cfg.cw : cfg.cwTall) * .24f);
                var t = GameArt.Get("Icon_ImageIcon_Lock01_s");
                PK.Img(lk, "Icon_ImageIcon_Lock01_s", lw * (t != null && t.width > 0 ? t.height / (float)t.width : 1f), lw).style.marginBottom = U(4);
                PK.TL(lk, p, it.locked, nfs * .62f, 3);
            }
            else if (it.tap != null)
            {
                var tap = it.tap;
                card.RegisterCallback<PointerDownEvent>(_ => card.style.scale = new Scale(new Vector2(.97f, .97f)), TrickleDown.TrickleDown);
                card.RegisterCallback<PointerUpEvent>(_ => card.style.scale = new Scale(Vector2.one), TrickleDown.TrickleDown);
                card.RegisterCallback<PointerLeaveEvent>(_ => card.style.scale = new Scale(Vector2.one));
                var builtAt = TapGuard.Serial;
                card.AddManipulator(new Clickable(() =>
                {
                    if (!TapGuard.Allow(builtAt)) return;
                    IslandAudio.Play("pop");
                    tap();
                }));
            }

            return card;
        }

        // ------------------------------------------------------------------ what's on sale

        private List<ShopSection> ShopSections()
        {
            var list = new List<ShopSection>();
            var now = IslandEconomyHost.Clock;

            // offers: everything here is bought with gems (no real-money products on the web build)
            var offers = new ShopSection { id = "offers", tab = "OFFERS", icon = "Icon_Gift", title = "SPECIAL OFFERS" };
            // the daily deal: a catalogue offer, once a day (the server keeps count and says what was bought today)
            var deal = Wallet.Offer("deal_daily");
            var dealBought = (Wallet.Service?.BoughtOn("deal_daily", ShopUtcDay) ?? 0) >= (deal?.PerDay > 0 ? deal.PerDay : long.MaxValue);
            if (deal != null)
            {
                var dealBoost = deal.Boosts.Keys.FirstOrDefault() ?? "fert";
                offers.items.Add(new ShopItem
                {
                    kind = "hero", frame = "g5", head = "<size=76%>Resets in:</size> <color=#ff5a6e>" + ShopUntilMidnight() + "</color>", crown = true, info = true,
                    name = "DAILY DEAL", art = BoostArt(dealBoost), qty = "x" + (deal.Boosts.TryGetValue(dealBoost, out var dealN) ? dealN : 1),
                    sub = deal.Subtitle ?? (ShopBoosts.FirstOrDefault(x => x.id == dealBoost).name ?? dealBoost), gem = deal.Gems, ribbon = deal.Ribbon,
                    locked = dealBought ? "Bought" : null,
                    tap = () => ShopBuy("deal_daily"),
                });
            }
            // the Farm Pass's premium track: shown, but locked (it becomes an NFT pass later; nothing to buy here)
            if (Wallet.Shop?.Pass is not { PremiumActive: true })
            {
                var perkGems = Wallet.Prices.Pass.Tiers.Sum(t => t.Premium.Gems);
                offers.items.Add(new ShopItem
                {
                    kind = "hero", frame = "p5", head = "<size=76%>" + Wallet.Prices.Pass.Pitch + "</size>", name = "PREMIUM FARM PASS", art = "ShopItem_SpecialChest_Green", glow = true,
                    chips = perkGems > 0 ? new[] { ("Icon_ImageIcon_Gem01_s", "+" + PK.Num(perkGems)) } : null, locked = "Coming soon",
                });
            }
            offers.dot = deal != null && !dealBought ? 1 : 0;
            list.Add(offers);

            // gems: earned in play (no real-money gem packs on the web build)
            var gems = new ShopSection { id = "gems", tab = "GEMS", icon = "Icon_Gems", title = "EARN GEMS" };
            gems.items.Add(new ShopItem { frame = "b", name = "Earn gems", art = "Icon_Gems", sub = "Achievements, daily rewards, the Lucky Wheel and the Farm Pass" });
            list.Add(gems);

            // coins: the free gift, then packs bought with gems
            var coins = new ShopSection { id = "coins", tab = "COINS", icon = "Icon_Golds", title = "COIN PACK" };
            // the free gift: the catalogue's coins every few hours, timed by the server (state/shop)
            var gift = Wallet.Prices.FreeGift;
            var giftAt = (Wallet.Shop?.NextGiftAt ?? 0) / 1000.0;
            var giftReady = Wallet.Shop?.NextGiftAt != null && now >= giftAt;
            coins.items.Add(new ShopItem
            {
                frame = "b", name = "Free Gift", art = "Icon_ImageIcon_GoldPouch",
                sub = giftReady ? gift.Coins + " coins · ready!" : Wallet.Shop?.NextGiftAt == null ? gift.Coins + " coins · online" : "Next in " + ShopSpan(giftAt - now),
                free = giftReady, timer = giftReady || Wallet.Shop?.NextGiftAt == null ? null : ShopSpan(giftAt - now),
                tap = ShopClaimGift,
            });
            // coin packs bought with gems: the catalogue's coins_* offers, cheapest first
            var coinPacks = Wallet.Prices.GemOffers.Where(kv => kv.Key.StartsWith("coins_", StringComparison.Ordinal) && kv.Value.Coins > 0)
                .OrderBy(kv => kv.Value.Gems).ToList();
            for (var i = 0; i < coinPacks.Count; i++)
            {
                var (offerId, pack) = (coinPacks[i].Key, coinPacks[i].Value);
                var last = i == coinPacks.Count - 1 && coinPacks.Count > 1;
                coins.items.Add(new ShopItem
                {
                    kind = last ? "tall" : "card", frame = "y", top = PK.Num(pack.Coins), art = "ShopItem_Coin_" + Mathf.Min(i + 1, 5), gem = pack.Gems, brk = i == 0,
                    pop = i == 2, glow = last, ribbon = last ? "Best Value" : null,
                    tap = () => ShopBuy(offerId),
                });
            }

            list.Add(coins);

            // buildings: the island's real catalogue
            var build = new ShopSection { id = "build", tab = "BUILD", icon = "Icon_Hammer", title = "BUILDINGS" };
            if (Sandbox != null)
            {
                var level = Sandbox.IslandLevel;
                var pics = ShopPictures();
                foreach (var e in Sandbox.ShopEntries
                             .OrderBy(e => level >= GameplayRules.UnlockLevel(e.id) ? 0 : GameplayRules.UnlockLevel(e.id))
                             .ThenBy(e => e.category, StringComparer.Ordinal).ThenBy(e => e.price))
                {
                    var entry = e;
                    var need = GameplayRules.UnlockLevel(e.id);
                    // the pack model's render: the picture list if it's loaded, else the copy in Resources (always in the build)
                    var pic = (pics != null ? pics.Building(e.id) : null) ?? Resources.Load<Texture2D>("GameUi/Art/Isl_b_" + e.id);
                    build.items.Add(new ShopItem
                    {
                        frame = "b", name = e.displayName, art = ShopBuildingIcon(e), render = true, picture = pic, coin = PK.Num(e.price),
                        locked = level >= need ? null : "Lv " + need,
                        tap = () =>
                        {
                            if (Sandbox == null) return;
                            if (Sandbox.CantBuy(entry) is { } why)
                            {
                                IslandAudio.Play("error");
                                Say(why);
                                return;
                            }
                            if (Sandbox.CoinsNow < entry.price)
                            {
                                IslandAudio.Play("error");
                                Say("Not enough coins");
                                return;
                            }

                            Go("play");
                            Sandbox.Buy(entry);
                        },
                    });
                }
            }

            list.Add(build);

            // boosts: bought with gems (the server's inventory, used from Storage)
            var boosts = new ShopSection { id = "boosts", tab = "BOOSTS", icon = "Icon_Booster", title = "BOOSTS" };
            foreach (var b in ShopBoosts.Where(b => Wallet.Price("boost_" + b.id) > 0))
            {
                var boost = b;
                boosts.items.Add(new ShopItem
                {
                    frame = "b", name = b.name, art = b.art, sub = b.sub, gem = Wallet.Price("boost_" + b.id), pop = b.id == "shield",
                    tap = () => ShopBuy("boost_" + boost.id),
                });
            }

            list.Add(boosts);
            return list;
        }

        private bool _shopBuying;

        /// <summary>
        /// Buy a catalogue offer with gems (wallet-spend: the server checks price and balance), then add
        /// what it gives: coins to the island, boosts to Storage.
        /// </summary>
        private async void ShopBuy(string offerId, Action? bought = null)
        {
            if (_shopBuying) return;
            if (!Wallet.CanSpend || Wallet.Service == null)
            {
                IslandAudio.Play("error");
                Say("Connect to the internet to buy with gems");
                return;
            }
            if (Wallet.Price(offerId) > Wallet.Gems) { ShopNoGems(); return; }
            _shopBuying = true;
            var result = await Wallet.Service.Spend(offerId);
            _shopBuying = false;
            if (result.IsFailure)
            {
                switch (result.Error.Code)
                {
                    case ErrorCode.NotEnoughGems: ShopNoGems(); break;
                    case ErrorCode.OfferSoldOut:
                        bought?.Invoke();   // already bought today (maybe on another device)
                        Say("Sold out for today — come back tomorrow");
                        break;
                    case ErrorCode.OfferUnknown: Say("This offer has ended"); break;
                    default: Say("Couldn't buy it — check your connection and try again"); break;
                }
                MarkDirty();
                return;
            }
            bought?.Invoke();
            if (result.Value.Fresh) ShopGive(result.Value);
            MarkDirty();
        }

        /// <summary>What a purchase gives: coins to the island, boosts to Storage.</summary>
        private void ShopGive(WalletSpendResponse got)
        {
            var said = new List<string>();
            if (got.Coins > 0)
            {
                Economy?.AddCoins((int)got.Coins);
                said.Add("+" + PK.Num(got.Coins) + " coins");
            }
            foreach (var kv in got.Boosts)   // already in the server's Storage
            {
                if (kv.Value <= 0) continue;
                var name = ShopBoosts.FirstOrDefault(b => b.id == kv.Key).name ?? kv.Key;
                said.Add((kv.Value > 1 ? kv.Value + " " : "") + name + " added to Storage");
            }
            IslandAudio.Play("coin");
            if (said.Count > 0) Say(string.Join(" · ", said));
        }

        /// <summary>The free gift (shop-claimGift): the server checks the timer; the coins go to the island.</summary>
        private async void ShopClaimGift()
        {
            var shop = Wallet.Shop;
            if (shop == null || !shop.Online)
            {
                Say("Connect to the internet to collect the gift");
                return;
            }
            if (_shopBuying) return;
            _shopBuying = true;
            var result = await shop.ClaimGift();
            _shopBuying = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code == ErrorCode.GiftNotReady ? "The next gift is on its way" : "Couldn't collect it — check your connection and try again");
                MarkDirty();
                return;
            }
            if (result.Value.Fresh && result.Value.Coins > 0)
            {
                Economy?.AddCoins((int)result.Value.Coins);
                IslandAudio.Play("coin");
                Say("+" + PK.Num(result.Value.Coins) + " coins");
            }
            MarkDirty();
        }

        private void ShopNoGems()
        {
            IslandAudio.Play("error");
            Say("Not enough gems");
            ShopScrollTo("gems");
        }

        /// <summary>A stand-in picture for a building while there's no render (by id, else by category).</summary>
        private static string ShopBuildingIcon(BuildingCatalogue.Entry e) => e.id switch
        {
            "field" => "Icon_Pumkin",
            "well" => "G_Water",
            "chicken_coop" => "Icon_Egg",
            "warehouse" or "farmhouse" => "Icon_Chest",
            "windmill" => "Icon_GearWheels",
            "market_stall" => "Icon_Shop",
            _ => e.category switch
            {
                "Farm" => "G_Wheat",
                "Animals" => "Icon_Egg",
                "Factories" => "Icon_GearWheels",
                "Town" => "G_House",
                "Shops" => "Icon_Shop",
                "Sea" => "G_Water",
                "Orchard" => "G_Tree",
                "Mine" => "G_Mine",
                "Defence" => "Icon_Shield",
                _ => "Icon_Hammer",
            },
        };

        private static IslandPictures? _shopPictures;

        /// <summary>The island's rendered building pictures, if they're loaded (else the cards keep the design's render slot).</summary>
        private static IslandPictures? ShopPictures()
        {
            if (_shopPictures != null) return _shopPictures;
            _shopPictures = Resources.FindObjectsOfTypeAll<IslandPictures>().FirstOrDefault();
            return _shopPictures;
        }
    }
}
