#nullable enable
using System;
using Farmverse.Contracts;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Coupons (coupons.html): replaces the pack's Stage screen. Daily tasks / Goals as stage cards in a
    // carousel with ◀ ▶, My codes as a list, and the coupon popup (ticket, code, copy, redeem, steps).
    // Laid out in the mockup's literal px per device (tablet 1366 frame / phone 844 frame).
    public sealed partial class GameUi
    {
        private static readonly bool RegCpCoupons = Register("coupons", UiLayer.Page, (ui, c) => ui.CpCoupons(c), .55f);
        private static readonly bool RegCpCoupon = Register("coupon", UiLayer.Popup, (ui, c) => ui.CpCouponPopup(c), .55f);

        private static string CpCouponTab = "daily";
        private static object? CpCouponArgs;

        /// <summary>The mockup's per-device numbers (CSS variables), literal px.</summary>
        private sealed class CpCfg
        {
            public float hh, th, tfs, pad, r, gapY, infoFs, cw, ch, cg, artTop, artH, nfs, floor, aw, topPad, rh, rgap, pw, pp, cs, tl, vbS;
            public bool phone;

            public static CpCfg For(Pt p) => p.Phone
                ? new CpCfg { hh = 52, th = 36, tfs = 16, pad = 10, r = 10, gapY = 2, infoFs = 12, cw = 150, ch = 140, cg = 16, artTop = 14, artH = 96, nfs = 19, floor = 64, aw = 40, topPad = 4, rh = 58, rgap = 8, pw = 600, pp = 12, cs = 40, tl = 110, vbS = 14, phone = true }
                : new CpCfg { hh = 96, th = 58, tfs = 26, pad = 28, r = 14, gapY = 14, infoFs = 20, cw = 300, ch = 400, cg = 34, artTop = 50, artH = 250, nfs = 36, floor = 130, aw = 96, topPad = 10, rh = 92, rgap = 14, pw = 760, pp = 26, cs = 64, tl = 170, vbS = 24 };
        }

        /// <summary>A coupon type's round logo (white or coloured disc, ink edge, the logo picture or glyph). s in panel px.</summary>
        private static VisualElement CpLogo(VisualElement? parent, Pt p, CpCouponType t, float s, float border = 3, bool drop = true)
        {
            var e = PK.Centre(PK.Box(parent));
            PK.Size(e, s, s);
            e.style.backgroundColor = GameTheme.C(t.logoBg);
            Kit.Round(e, s / 2f);
            Kit.Border(e, p.U(border), PK.Ink);
            if (drop) e.style.borderBottomWidth = p.U(border + 3);
            if (!string.IsNullOrEmpty(t.logoArt)) PK.Img(e, t.logoArt, s * .56f);
            else
            {
                var l = PK.TL(e, p, t.logoGlyph, 1, .01f);
                l.style.fontSize = s * .6f;
                l.style.unityTextOutlineWidth = 0;
                l.style.textShadow = new TextShadow();
            }

            return e;
        }

        private void CpCoupons(Ctx c)
        {
            var p = c.P;
            var k = CpCfg.For(p);
            float U(float n) => p.U(n);
            var eco = Economy;
            if (!ReferenceEquals(c.Args, CpCouponArgs))
            {
                CpCouponArgs = c.Args;
                if (c.Args is string tab && (tab == "daily" || tab == "goals" || tab == "codes")) CpCouponTab = tab;
            }

            c.Watch(() => CpCouponStore.ReadyCount(Economy));
            c.Watch(() => CpCouponStore.Codes.Count);
            c.Watch(() =>
            {
                var sum = 0;
                foreach (var t in CpCouponStore.Tasks) sum = sum * 31 + t.Progress(Economy);
                return sum;
            });

            // background, dots and the dark floor band
            PK.Screen(c.Root, true);
            var floor = PK.Abs(PK.Box(c.Root), left: 0, right: 0, bottom: 0, height: U(k.floor));
            floor.style.backgroundColor = GameTheme.T("#0c2148");

            var col = PK.Inset(PK.Col(c.Root));

            // header: back · ticket "Coupons" · spacer · "N codes ready" · home
            var hdr = PK.Row(col);
            PK.Size(hdr, float.NaN, U(k.hh));
            hdr.style.paddingRight = p.R + U(14);
            var back = PK.Centre(PK.Box(hdr));
            PK.Size(back, U(k.hh * 1.25f) + p.L, U(k.hh * .8f));
            back.style.paddingLeft = p.L;
            Kit.Flush(back, true);
            PK.Img(back, "Icon_PictoIcon_Back", U(k.hh * .8f) * .52f);
            PK.Tap(back, p, Back, "whoosh");
            var title = PK.Row(hdr, U(k.hh * .18f));
            title.style.marginLeft = U(k.hh * .32f);
            CpTicketIcon(title, U(k.phone ? 36 : 64));
            PK.TL(title, p, "Coupons", k.hh * .46f, 4);
            PK.Grow(hdr);
            var ready = CpCouponStore.ReadyCount(eco);
            var pill = PK.Row(hdr, U(8));
            pill.style.height = U(k.hh * .56f);
            pill.style.backgroundColor = GameTheme.T("#041634", .75f);
            Kit.Round(pill, U(k.hh));
            PK.Pad(pill, U(6), 0, U(k.hh * .25f), 0);
            pill.style.marginRight = U(k.hh * .3f);
            CpTicketIcon(pill, U(k.phone ? 26 : 44));
            PK.TL(pill, p, $"<color=#7dff6a>{ready}</color> {(ready == 1 ? "code" : "codes")} ready", k.hh * .26f, 3);
            PK.Tap(pill, p, () => { CpCouponTab = "codes"; MarkDirty(); });
            var home = PK.Img(hdr, "Icon_PictoIcon_Home", U(k.hh * .6f));
            PK.Tap(home, p, () => Go("home"), "whoosh");

            // info line (tablet only)
            if (!k.phone)
            {
                var info = CpN(col, p, "Finish <color=#7dff6a>daily tasks</color> and <color=#7dff6a>goals</color> to earn coupons. Each coupon shows its value and how to redeem it.", k.infoFs, GameTheme.T("#bfe6ff"));
                info.style.marginLeft = p.L + U(k.pad);
                info.style.marginRight = p.R + U(k.pad);
                info.style.textShadow = new TextShadow { offset = new Vector2(0, U(2)), color = GameTheme.Shade(.3f) };
            }

            // tabs
            var seg = PK.Row(col);
            seg.style.height = U(k.th);
            seg.style.flexShrink = 0;
            seg.style.backgroundColor = GameTheme.T("#08284f");
            Kit.Round(seg, U(k.r));
            PK.DropEdge(seg, U(4), GameTheme.Shade(.25f));
            seg.style.marginTop = U(k.gapY);
            seg.style.marginLeft = p.L + U(k.pad);
            seg.style.marginRight = p.R + U(k.pad);
            foreach (var (id, label) in new[] { ("daily", "Daily tasks"), ("goals", "Goals"), ("codes", "My codes") })
            {
                var tab = PK.Centre(PK.Row(seg, U(8)));
                tab.style.flexGrow = 1;
                tab.style.flexBasis = 0;
                tab.style.height = new Length(100, LengthUnit.Percent);
                var on = id == CpCouponTab;
                var tl = PK.TL(tab, p, label, k.tfs, 3.5f);
                tl.style.color = on ? Color.white : GameTheme.T("#3cb4ff");
                if (id == "daily")
                {
                    var small = CpN(tab, p, "resets " + CpUntilMidnight(), k.tfs * .55f, GameTheme.C("#ff7a8c"));
                    c.Live(() => small.text = Loc.Of("resets " + CpUntilMidnight()));
                }

                if (on)
                {
                    var u = PK.Box(tab);
                    u.style.position = Position.Absolute;
                    u.style.bottom = 0;
                    u.style.left = new Length(30, LengthUnit.Percent);
                    u.style.right = new Length(30, LengthUnit.Percent);
                    u.style.height = U(4);
                    u.style.backgroundColor = Color.white;
                    Kit.Round(u, U(6), U(6), 0, 0);
                }

                var tid = id;
                PK.Tap(tab, p, () => { CpCouponTab = tid; MarkDirty(); });
            }

            if (CpCouponTab == "codes")
            {
                var list = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
                list.style.flexGrow = 1;
                list.contentContainer.style.paddingTop = U(k.topPad);
                list.contentContainer.style.paddingLeft = p.L + U(k.pad);
                list.contentContainer.style.paddingRight = p.R + U(k.pad);
                list.contentContainer.style.paddingBottom = U(8) + U(k.floor);
                col.Add(list);
                var first = true;
                foreach (var code in CpCouponStore.Codes)
                {
                    var row = CpCodeRow(list.contentContainer, p, k, code);
                    if (!first) row.style.marginTop = U(k.rgap);
                    first = false;
                }

                if (first)
                {
                    var none = CpN(list.contentContainer, p, "No codes yet. Finish a daily task or a goal to earn one.", k.infoFs, GameTheme.T("#bfe6ff"), TextAnchor.MiddleCenter);
                    none.style.marginTop = U(k.ch * .3f);
                }

                return;
            }

            // carousel of stage cards
            var car = new ScrollView(ScrollViewMode.Horizontal) { horizontalScrollerVisibility = ScrollerVisibility.Hidden, verticalScrollerVisibility = ScrollerVisibility.Hidden };
            car.style.flexGrow = 1;
            car.mode = ScrollViewMode.Horizontal;
            var cc = car.contentContainer;
            cc.style.flexDirection = FlexDirection.Row;
            cc.style.alignItems = Align.Center;
            cc.style.paddingTop = U(k.topPad) + U(k.nfs * .6f);
            cc.style.paddingLeft = p.L + U(k.pad);
            cc.style.paddingRight = p.R + U(k.pad);
            col.Add(car);
            var daily = CpCouponTab == "daily";
            var i = 0;
            foreach (var t in CpCouponStore.Tasks)
            {
                if (t.Daily != daily) continue;
                var card = CpCard(cc, c, k, t);
                if (i++ > 0) card.style.marginLeft = U(k.cg);
            }

            // ◀ ▶ under the cards, in the floor band
            var arrows = PK.Row(col, U(k.cg));
            arrows.style.justifyContent = Justify.Center;
            arrows.style.height = U(k.floor);
            arrows.style.flexShrink = 0;
            arrows.style.paddingBottom = p.B;
            foreach (var dir in new[] { -1, 1 })
            {
                var a = PK.Centre(PK.Box(arrows));
                PK.Size(a, U(k.aw), U(k.aw * .95f));
                Kit.Nine(a, GameArt.Get("Button_Square03_Navy"), 20, 20, 20, 24, U(12) / 20f);
                PK.Img(a, dir < 0 ? "Icon_PictoIcon_Prev01" : "Icon_PictoIcon_Next01", U(k.aw * .95f) * .52f);
                var d = dir;
                PK.Tap(a, p, () =>
                {
                    var o = car.scrollOffset;
                    o.x = Mathf.Max(0, o.x + d * car.layout.width * .75f);
                    car.scrollOffset = o;
                });
            }
        }

        /// <summary>One stage-style card: panel with glow, art, value chip, stamp or padlock; name; bar / Get code / Show code / Required Level.</summary>
        private VisualElement CpCard(VisualElement parent, Ctx c, CpCfg k, CpCouponTask t)
        {
            var p = c.P;
            float U(float n) => p.U(n);
            var eco = Economy;
            var type = CpCouponStore.Type(t.TypeId);
            var st = CpCouponStore.StateOf(t, eco);
            var locked = st == CpCouponStore.State.Locked;
            var done = st == CpCouponStore.State.Done;

            var card = PK.Col(parent);
            card.style.alignItems = Align.Center;
            PK.Size(card, U(k.cw), float.NaN);

            var panel = PK.Clip(CpGradBox(card, "cardpanel", GameTheme.T("#123f7c"), GameTheme.T("#0e3366"), U(3), U(k.r * 1.3f), 0));
            PK.Size(panel, U(k.cw), U(k.ch));
            panel.style.borderBottomWidth = U(3);
            panel.style.overflow = Overflow.Visible;
            var shade = PK.Abs(PK.Box(panel), left: 0, right: 0, bottom: 0, height: U(10));   // inset 0 -10px 0 rgba(0,0,0,.18)
            shade.style.backgroundColor = GameTheme.Shade(.18f);
            Kit.Round(shade, 0, 0, U(k.r * 1.3f - 3), U(k.r * 1.3f - 3));
            var drop = PK.Abs(PK.Box(card), left: 0, top: U(5), width: U(k.cw), height: U(k.ch));   // 0 5px 0 rgba(0,0,0,.25)
            drop.style.backgroundColor = GameTheme.Shade(.25f);
            Kit.Round(drop, U(k.r * 1.3f));
            drop.SendToBack();

            var gw = U(k.cw) * 1.2f;
            var glow = PK.Abs(PK.Box(panel), left: (U(k.cw) - U(6) - gw) / 2f, top: U(k.ch) * .44f - gw / 2f, width: gw, height: gw);
            Kit.Contain(glow, GameArt.Get("CardFrame06_Glow"));
            glow.style.opacity = GameTheme.Fade(.35f);
            var artTex = GameArt.Get(t.Art);
            var ah = U(k.artH);
            var aw = artTex != null && artTex.height > 0 ? ah * artTex.width / artTex.height : ah;
            var art = PK.Abs(PK.Box(panel), left: (U(k.cw) - U(6) - aw) / 2f, top: U(k.artTop), width: aw, height: ah);
            Kit.Picture(art, artTex);
            if (locked)
            {
                art.style.unityBackgroundImageTintColor = new Color(.45f, .45f, .45f, 1);
                glow.style.unityBackgroundImageTintColor = new Color(.45f, .45f, .45f, 1);
            }

            // value chip (top right): logo + "10 B3TR"
            var chip = PK.Row(panel, U(5));
            PK.Abs(chip, top: U(8), right: U(8));
            chip.style.backgroundColor = PK.Ink;
            Kit.Round(chip, U(k.nfs));
            PK.Pad(chip, U(3), U(3), U(9), U(3));
            CpLogo(chip, p, type, U(k.vbS), 2, false);
            var cv = PK.TL(chip, p, CpAmount.Of(t.Amount, type), k.nfs * .5f);
            cv.style.unityTextOutlineWidth = 0;
            cv.style.textShadow = new TextShadow();

            if (done)
            {
                panel.style.opacity = .85f;
                var stamp = PK.Centre(PK.Box(panel));
                PK.Abs(stamp, left: 0, right: 0, top: U(k.ch) * .46f - U(k.nfs * .75f));
                var sb = PK.Box(stamp);
                sb.style.alignSelf = Align.Center;
                Kit.Border(sb, U(4), GameTheme.C("#7dff6a"));
                Kit.Round(sb, U(10));
                sb.style.backgroundColor = GameTheme.T("#0b1d3a", .6f);
                PK.Pad(sb, U(10), U(4), U(10), U(4));
                sb.style.rotate = new Rotate(new Angle(-12, AngleUnit.Degree));
                var sl = PK.TL(sb, p, "CLAIMED", k.nfs * .75f, 4);
                sl.style.color = GameTheme.C("#7dff6a");
            }

            if (locked)
            {
                var lk = PK.Img(panel, "Icon_ImageIcon_Lock01_s", U(k.nfs * 1.7f));
                PK.Abs(lk, left: (U(k.cw) - U(6) - lk.style.width.value.value) / 2f, bottom: -U(k.nfs * .5f));
            }

            if (st == CpCouponStore.State.Ready)
            {
                var nw = PK.Row(card);
                PK.Abs(nw, left: -U(8), top: -U(10));
                nw.style.height = U(k.nfs * 1.05f);
                PK.Pad(nw, U(8), 0, U(12), U(4));
                Kit.Nine(nw, GameArt.Get("Label_Ribbon_Single_Orange"), 12, 14, 24, 26, U(8) / 14f);
                PK.TL(nw, p, "NEW!", k.nfs * .55f, 3);
            }

            var name = PK.TL(card, p, t.Name, k.nfs, k.nfs * .13f);
            name.style.marginTop = U(k.nfs * .45f);
            name.style.unityTextAlign = TextAnchor.MiddleCenter;

            if (locked)
            {
                var sub = PK.TL(card, p, "Required Level " + t.LockLevel, k.nfs * .55f, 3);
                sub.style.color = GameTheme.T("#7fd0ff");
                sub.style.marginTop = U(k.nfs * .25f);
            }
            else if (done || st == CpCouponStore.State.Ready)
            {
                var get = PK.Row(card);
                get.style.justifyContent = Justify.Center;
                get.style.marginTop = U(k.nfs * .3f);
                get.style.height = U(k.nfs * 1.5f);
                PK.Pad(get, U(k.nfs * .8f), 0, U(k.nfs * .8f), 0);
                Kit.Nine(get, GameArt.Get(done ? "Button01_195_Blue" : "Button01_195_Green"), 30, 96, 30, 96, U(k.nfs * .75f) / 96f);
                PK.TL(get, p, done ? "Show code" : "Get code", k.nfs * .7f, 4);
                PK.Tap(get, p, () =>
                {
                    var have = CpCouponStore.CodeFor(t);
                    if (have != null) Open("coupon", have);
                    else CpCouponClaim(t);
                });
            }
            else
            {
                var have = Mathf.Min(t.Progress(eco), t.Of);
                var bar = PK.Clip(PK.Box(card));
                bar.style.marginTop = U(k.nfs * .3f);
                bar.style.width = new Length(80, LengthUnit.Percent);
                bar.style.height = U(k.nfs * .85f);
                bar.style.backgroundColor = GameTheme.T("#0d1d4f");
                Kit.Border(bar, U(3), PK.Ink);
                Kit.Round(bar, U(8));
                var fill = PK.Abs(PK.Box(bar), left: 0, top: 0, bottom: 0);
                Kit.Nine(fill, GameArt.Get("Slider_Basic02_Fill_Yellow"), 16, 18, 16, 18, U(6) / 18f);
                PK.SetPct(fill, t.Of > 0 ? have * 100f / t.Of : 0);
                var bl = PK.TL(PK.Centre(PK.Inset(PK.Box(bar))), p, PK.Num(have) + " / " + PK.Num(t.Of), k.nfs * .52f, 3);
                bl.style.unityTextAlign = TextAnchor.MiddleCenter;
            }

            return card;
        }

        /// <summary>A "My codes" row: logo, value · task, earned/expiry line, the code, Copy.</summary>
        private VisualElement CpCodeRow(VisualElement parent, Pt p, CpCfg k, CpCoupon code)
        {
            float U(float n) => p.U(n);
            var type = CpCouponStore.Type(code.typeId);
            var row = PK.ListRow(parent, p);
            row.style.height = U(k.rh);
            PK.Pad(row, U(14), 0, U(14), 0);
            PK.Gap(row, U(14));
            CpLogo(row, p, type, U(k.phone ? 34 : 58));
            var w = PK.Col(row);
            w.style.flexGrow = 1;
            w.style.flexShrink = 1;
            w.style.overflow = Overflow.Hidden;
            PK.TL(w, p, CpAmount.Of(code.amount, type) + " · " + code.task, k.rh * .26f, 3);
            var days = Mathf.Max(0, (int)Math.Ceiling((code.expiresAt - Farmverse.Presentation.World.Island.IslandEconomyHost.Clock) / 86400.0));
            var earned = DateTimeOffset.FromUnixTimeSeconds((long)code.issuedAt).LocalDateTime;
            var when = earned.Date == DateTime.Now.Date ? "today" : earned.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture);
            var s = CpN(w, p, $"Earned {when} · expires in {days} days{(code.copiedAt > 0 ? " · copied" : "")}", k.rh * .17f, GameTheme.T("#8ad6ff"));
            s.style.marginTop = U(4);
            var box = PK.Box(row);
            box.style.backgroundColor = PK.Ink;
            Kit.Round(box, U(8));
            PK.Pad(box, U(10), U(6), U(10), U(6));
            var cl = CpN(box, p, code.code, k.rh * .22f, GameTheme.C("#7dff6a"));
            cl.style.letterSpacing = U(2);
            var copy = PK.Centre(PK.Box(row));
            PK.Size(copy, U(k.rh * 1.6f), U(k.rh * .6f));
            Kit.Nine(copy, GameArt.Get("Button01_195_Green"), 30, 96, 30, 96, U(k.rh * .3f) / 96f);
            PK.TL(copy, p, "Copy", k.rh * .24f, 3);
            PK.Tap(copy, p, () => CpCopy(code));
            return row;
        }

        private void CpCopy(CpCoupon code)
        {
            GameLinks.Copy(code.code);
            CpCouponStore.MarkCopied(code);
            Say("Copied!", 1.2f);
            MarkDirty();
        }

        /// <summary>The coupon popup: "Your coupon", the ticket (logo + value | COUPON CODE, code, earned for), Copy + redeem, how-to steps. Args: CpCoupon or its code.</summary>
        private void CpCouponPopup(Ctx c)
        {
            var p = c.P;
            var k = CpCfg.For(p);
            float U(float n) => p.U(n);
            var code = c.Args as CpCoupon ?? (c.Args is string s ? CpCouponStore.Find(s) : null);
            if (code == null)
            {
                Back();
                return;
            }

            var type = CpCouponStore.Type(code.typeId);
            var pop = PK.Col(c.Root);
            pop.pickingMode = PickingMode.Position;
            pop.style.position = Position.Absolute;
            pop.style.left = new Length(50, LengthUnit.Percent);
            pop.style.top = new Length(50, LengthUnit.Percent);
            pop.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            pop.style.width = U(k.pw);
            pop.style.alignItems = Align.Center;
            Kit.Nine(pop, GameArt.Get("Popup03_Single"), 18, 24, 18, 30, U(22) / 24f);
            PK.Pad(pop, U(k.pp), U(k.pp), U(k.pp), U(k.pp + 24));
            PK.Gap(pop, U(k.pp * .5f));

            PK.TL(pop, p, "Your coupon", k.nfs * 1.05f, 4);

            // ticket: accent gradient, logo + value on the left, dashed line, code on the right, two notches
            var ticket = PK.Row(pop);
            ticket.style.alignSelf = Align.Stretch;
            ticket.style.alignItems = Align.Stretch;
            Kit.Picture(ticket, CpArt.Diag(type.id, GameTheme.C(type.accentA), GameTheme.C(type.accentB)));
            Kit.Border(ticket, U(3), PK.Ink);
            Kit.Round(ticket, U(16));
            PK.Clip(ticket);
            var left = PK.Centre(PK.Col(ticket, U(6)));
            PK.Size(left, U(k.tl), float.NaN);
            PK.Pad(left, 0, U(12), 0, U(12));
            left.style.borderRightWidth = U(3);
            left.style.borderRightColor = GameTheme.T("#0b1d3a", .45f);
            CpLogo(left, p, type, U(k.phone ? 40 : 72));
            PK.TL(left, p, CpAmount.Of(code.amount, type), k.phone ? 15 : 24, 4);
            var right = PK.Col(ticket, U(8));
            right.style.flexGrow = 1;
            PK.Pad(right, U(18), U(12), U(18), U(12));
            var kk = PK.TL(right, p, "COUPON CODE", k.nfs * .6f, .01f);
            kk.style.unityTextOutlineWidth = 0;
            kk.style.textShadow = new TextShadow();
            kk.style.color = GameTheme.C(type.ink);
            kk.style.letterSpacing = U(1);
            var big = PK.Box(right);
            big.style.backgroundColor = PK.Ink;
            Kit.Round(big, U(10));
            PK.Pad(big, U(14), U(8), U(14), U(8));
            var bl = CpN(big, p, code.code, k.nfs * .95f, GameTheme.C("#7dff6a"), TextAnchor.MiddleCenter);
            bl.style.letterSpacing = U(3);
            CpN(right, p, "Earned for: " + code.task, k.nfs * .42f, GameTheme.C(type.ink));
            foreach (var top in new[] { true, false })   // the two half-circle notches over the dashed line
            {
                var notch = PK.Box(ticket);
                PK.Abs(notch, left: U(k.tl - 14) - U(3), width: U(28), height: U(28));
                if (top) notch.style.top = -U(17) - U(3);
                else notch.style.bottom = -U(17) - U(3);
                notch.style.backgroundColor = GameTheme.T("#0d4a8e");
                Kit.Border(notch, U(3), PK.Ink);
                Kit.Round(notch, U(14));
            }

            // buttons
            var btns = PK.Row(pop, U(14));
            btns.style.alignSelf = Align.Stretch;
            foreach (var (green, label) in new[] { (true, "Copy code"), (false, type.redeemLabel) })
            {
                var b = PK.Centre(PK.Box(btns));
                b.style.flexGrow = 1;
                b.style.flexBasis = 0;
                b.style.height = U(k.nfs * 1.7f);
                Kit.Nine(b, GameArt.Get(green ? "Button01_195_Green" : "Button01_195_Blue"), 30, 96, 30, 96, U(k.nfs * .85f) / 96f);
                PK.TL(b, p, label, k.nfs * .75f, 4);
                if (green) PK.Tap(b, p, () => CpCopy(code));
                else
                    PK.Tap(b, p, () =>
                    {
                        if (!string.IsNullOrEmpty(type.redeemUrl)) UnityEngine.Application.OpenURL(type.redeemUrl);
                    });
            }

            // how to redeem
            var howH = PK.TL(pop, p, "How to redeem", k.nfs * .55f, 3);
            howH.style.alignSelf = Align.FlexStart;
            howH.style.color = GameTheme.T("#7fd0ff");
            var steps = PK.Col(pop, U(k.nfs * .25f));
            steps.style.alignSelf = Align.Stretch;
            for (var i = 0; i < type.steps.Length; i++)
            {
                var row = PK.Row(steps, U(k.nfs * .35f));
                row.style.backgroundColor = GameTheme.T("#041634", .45f);
                Kit.Round(row, U(10));
                PK.Pad(row, U(k.nfs * .3f), U(k.nfs * .18f), U(k.nfs * .3f), U(k.nfs * .18f));
                var n = PK.Centre(PK.Box(row));
                PK.Size(n, U(k.nfs * .75f), U(k.nfs * .75f));
                n.style.backgroundColor = GameTheme.C("#ffd23a");
                Kit.Round(n, U(k.nfs));
                var nl = PK.TL(n, p, (i + 1).ToString(), k.nfs * .45f, .01f);
                nl.style.unityTextOutlineWidth = 0;
                nl.style.textShadow = new TextShadow();
                nl.style.color = PK.Ink;
                var txt = type.steps[i].Replace("<b>", "<color=#ffd23a>").Replace("</b>", "</color>");
                var tl = CpN(row, p, txt, k.nfs * .45f, GameTheme.T("#dff1ff"), TextAnchor.MiddleLeft, true);
                tl.style.flexShrink = 1;
            }

            // close (top right)
            var x = PK.Centre(PK.Abs(PK.Box(pop), top: -U(16), right: -U(16), width: U(k.cs), height: U(k.cs * 1.1f)));
            Kit.Picture(x, GameArt.Get("Button_Square02_Gray"));
            var xi = PK.Img(x, "Icon_PictoIcon_Close", U(k.cs) * .52f * 53f / 49f, U(k.cs) * .52f);
            xi.style.marginTop = -U(k.cs) * .08f;
            PK.Tap(x, p, Back, "whoosh");
        }

        private bool _cpCouponBusy;

        /// <summary>Ask the server for a code (coupons-claim): it checks the task on the mirrored island and takes one from the pool.</summary>
        private async void CpCouponClaim(CpCouponTask t)
        {
            var svc = Coupons;
            var host = Host;
            if (_cpCouponBusy || host == null) return;
            if (svc == null || !svc.Online)
            {
                Say("Connect to the internet to get your code");
                return;
            }
            _cpCouponBusy = true;
            var result = await svc.Claim(t.Id, JsonUtility.ToJson(host.CurrentState()));
            _cpCouponBusy = false;
            if (result.IsFailure)
            {
                Say(result.Error.Code switch
                {
                    ErrorCode.CouponLocked => "Not finished yet",
                    ErrorCode.CouponLinkRequired => "Coupons need a signed-in account — wallet sign-in is coming soon",
                    _ => "Couldn't get a code — check your connection and try again",
                });
                MarkDirty();
                return;
            }
            var got = result.Value;
            var type = CpCouponStore.Type(got.TypeId);
            MarkDirty();
            Open("coupon", new CpCoupon
            {
                code = got.Code, typeId = got.TypeId, taskId = t.Id, task = t.Label,
                issuedAt = Farmverse.Presentation.World.Island.IslandEconomyHost.Clock, expiresAt = got.ExpiresAt / 1000.0, amount = got.Amount,
            });
        }
    }
}
