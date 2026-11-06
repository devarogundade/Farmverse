#nullable enable
using System.Collections.Generic;
using System.Linq;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // The Storage page (design: storage.html, the pack's Inventory): category tabs and the item grid on the
    // left, the chosen item on the right with a quantity stepper and Sell (to the market stall) or Use (boosts).
    // Holds what the island's farmhouses and silos store, plus the boosts bought in the shop.
    public sealed partial class GameUi
    {
        private static readonly bool RegShop_Storage = Register("storage", UiLayer.Page, (ui, c) => ui.ShopStoragePage(c), .55f);

        private string _shopCat = "all";
        private string? _shopSel;
        private int _shopQty = 1;

        private static readonly (string id, string label, string? icon)[] ShopCats =
        {
            ("all", "ALL", null), ("crops", "Crops", "Icon_Pumkin"), ("animal", "Animal goods", "Icon_Egg"), ("mat", "Materials", "G_Wood"), ("boost", "Boosts", "Icon_Potion02_Green"), ("nft", "NFTs", "Icon_Gift"),
        };

        /// <summary>A row of the storage grid: a stored good or a boost.</summary>
        private sealed class ShopEntry
        {
            public string id = "", name = "", cat = "", colour = "Blue", about = "";
            public int count, price;
            public string? use, boostArt;
            /// <summary>The good whose picture the slot shows (NFT goods: their item, not the entry id).</summary>
            public string? goodId;
            /// <summary>An NFT the wallet holds (the NFTs tab).</summary>
            public Farmverse.Application.Ports.OwnedNft? nft;
        }

        private List<ShopEntry> ShopEntries()
        {
            var list = ShopStock().Select(s => new ShopEntry
            {
                id = s.item, name = ShopGoodName(s.item), cat = ShopKindOf(s.item), colour = ShopSlotColour(s.item), count = s.n,
                price = ShopPrice(s.item), about = ShopGoodAbout(s.item),
            }).ToList();
            foreach (var b in ShopBoosts)
            {
                var n = ShopBoostCount(b.id);
                if (n > 0) list.Add(new ShopEntry { id = "boost:" + b.id, name = b.name, cat = "boost", colour = "Purple", count = n, use = b.use, boostArt = b.art, about = "Boost. Use it on your island." });
            }
            if (Nfts != null)
                foreach (var t in Nfts.Owned)
                    list.Add(NftEntry(t));

            return list;
        }

        private void ShopStoragePage(Ctx c)
        {
            var p = c.P;
            var all = ShopEntries();
            var list = all.Where(i => _shopCat == "all" || i.cat == _shopCat).ToList();
            var it = list.FirstOrDefault(i => i.id == _shopSel) ?? list.FirstOrDefault();
            if (it != null && it.id != _shopSel)
            {
                _shopSel = it.id;
                _shopQty = 1;
            }

            if (it != null) _shopQty = Mathf.Clamp(_shopQty, 1, Mathf.Max(1, it.count));
            c.Watch(() => string.Join(",", ShopStock().Select(x => x.item + x.n)) + "|" + string.Join(",", ShopBoosts.Select(b => ShopBoostCount(b.id)))
                          + "|" + (Nfts == null ? "" : string.Join(",", Nfts.Owned.Select(t => t.TokenId + t.Price)) + Nfts.OwnedLoaded + Nfts.OwnedFailed));
            if (_shopCat == "nft") Nfts?.RefreshIfStale();

            var screen = PK.Screen(c.Root, true);
            var H = p.Phone ? 52f : 96f;

            // capacity pill: what the stores hold of what they can
            var stores = ShopStores();
            var used = stores.Sum(d => d.Stored);
            var room = stores.Sum(d => d.StorageCapacity);
            var cap = ShopNavyPill(p);
            PK.Pad(cap, p.U(p.Phone ? 30 : 56), 0, p.U(p.Phone ? 10 : 18), 0);
            PK.Abs(PK.Img(cap, "Icon_Bag", p.U(p.Phone ? 34 : 60)), left: -p.U(p.Phone ? 2 : 6), top: (p.U(p.Phone ? 28 : 50) - p.U(p.Phone ? 34 : 60)) / 2f);
            var capText = PK.T(cap, p, used + " / " + room, 25);
            if (room > 0 && used >= room) capText.style.color = GameTheme.C("#ff7a8c");
            var (coins, gems) = PK.Header(screen, p, "Storage", Back, () => Go("home"), "0", "0", cap,
                addCoins: () => Open("shop", "coins"), addGems: () => Open("shop", "gems"));
            c.Live(() =>
            {
                coins.text = Loc.Of(PK.Num(Economy?.Coins ?? 0));
                gems.text = Loc.Of(PK.Num(Wallet.Gems));
            });

            var pad = p.P(24);
            var body = PK.Row(screen);
            PK.Abs(body, left: p.L + pad, top: p.U(H) + p.P(10), right: p.R + pad, bottom: p.B + pad);
            body.style.alignItems = Align.Stretch;

            // left: tabs + grid
            var left = PK.Panel(body, p);
            left.style.flexGrow = 1;
            left.style.minWidth = 0;
            left.style.flexDirection = FlexDirection.Column;
            PK.Pad(left, p.P(16), 0, p.P(16), p.P(16));
            var cats = PK.Row(left);
            cats.style.height = p.P(78);
            cats.style.flexShrink = 0;
            cats.style.marginBottom = p.P(12);
            cats.style.borderBottomWidth = p.U(3);
            cats.style.borderBottomColor = GameTheme.Light(.06f);
            foreach (var (id, label, icon) in ShopCats)
            {
                var cat = PK.Centre(PK.Box(cats));
                cat.style.flexGrow = 1;
                cat.style.flexBasis = 0;
                cat.style.height = new Length(100, LengthUnit.Percent);
                var on = id == _shopCat;
                if (icon != null) ShopDull(PK.Img(cat, icon, p.P(52)), !on);
                else PK.T(cat, p, label, 30);
                if (on)
                {
                    var u = PK.Abs(PK.Box(cat), bottom: 0, height: p.U(4));
                    u.style.left = new Length(22, LengthUnit.Percent);
                    u.style.right = new Length(22, LengthUnit.Percent);
                    u.style.backgroundColor = Color.white;
                    Kit.Round(u, p.U(6), p.U(6), 0, 0);
                }

                var cid = id;
                PK.Tap(cat, p, () =>
                {
                    _shopCat = cid;
                    _shopSel = null;
                    _shopQty = 1;
                    MarkDirty();
                });
            }

            var s = p.Phone ? 96f : 104f;
            var sv = ShopScroller(left, false, p.P(12));
            var grid = sv.contentContainer;
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.FlexStart;
            grid.style.alignItems = Align.FlexStart;
            var cw = p.P(s);
            var ch = p.P(s * 1.06f);
            var cells = new List<VisualElement>();
            foreach (var i in list)
            {
                var item = i;
                var cell = PK.Box(grid);
                PK.Size(cell, cw, ch);   // every cell the same box, filled or empty
                var slotIcon = i.boostArt;
                var slot = PK.Slot(cell, p, slotIcon, PK.Num(i.count), i.colour, s, it != null && i.id == it.id, () =>
                {
                    _shopSel = item.id;
                    _shopQty = 1;
                    MarkDirty();
                });
                if (slotIcon == null) ShopSlotGood(slot, p, i.goodId ?? i.id, s);
                cells.Add(cell);
            }

            // empty cells to fill whole rows (enough for the widest screen; the layout below shows only full rows)
            var minRows = p.Phone ? 3 : 5;
            for (var k = list.Count; k < Mathf.Max(list.Count, 0) + 12 * minRows; k++)
            {
                var empty = PK.Box(grid);
                PK.Size(empty, cw, ch);
                Kit.Round(empty, p.P(14));
                empty.style.backgroundColor = GameTheme.T("#041634", .35f);
                cells.Add(empty);
            }

            // a true grid: as many columns as fit the viewport, equal gaps, every cell placed absolutely
            // (flex-wrap with margins re-wrapped differently whenever the width moved by a pixel)
            grid.style.flexWrap = Wrap.NoWrap;
            grid.style.position = Position.Relative;
            foreach (var e in cells) { e.style.position = Position.Absolute; e.style.flexShrink = 0; e.style.marginLeft = e.style.marginRight = e.style.marginTop = e.style.marginBottom = 0; }
            var minGap = p.P(10);
            var lastW = -1f;
            void Lay()
            {
                var w = sv.contentViewport.layout.width;
                if (float.IsNaN(w) || w <= 0 || Mathf.Abs(w - lastW) < .5f) return;
                lastW = w;
                var n = Mathf.Max(1, Mathf.FloorToInt((w + minGap) / (cw + minGap)));
                var gap = n > 1 ? (w - n * cw) / (n - 1) : 0f;
                var rows = Mathf.Max(minRows, Mathf.CeilToInt(list.Count / (float)n));
                for (var k = 0; k < cells.Count; k++)
                {
                    var e = cells[k];
                    var show = k < rows * n;
                    e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                    if (!show) continue;
                    e.style.left = Mathf.Round((k % n) * (cw + gap));
                    e.style.top = Mathf.Round((k / n) * (ch + gap));
                }
                grid.style.width = w;
                grid.style.height = rows * ch + (rows - 1) * gap;
            }
            sv.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => Lay());

            if (list.Count == 0)
            {
                var none = PK.N(null, p, _shopCat == "boost" ? "No boosts yet: get them in the Shop." : _shopCat == "nft" ? NftEmptyText() : "Empty: walk your harvest into the farmhouse.", 20, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter);
                PK.Abs(none, left: 0, right: 0, top: p.P(220));
                left.Add(none);
            }

            // right: the chosen item
            var sideW = p.Phone ? 520f : 420f;
            var side = PK.Panel(body, p);
            side.style.width = p.P(sideW);
            side.style.flexShrink = 0;
            side.style.marginLeft = p.P(20);
            side.style.flexDirection = FlexDirection.Column;
            side.style.alignItems = Align.Center;
            PK.Pad(side, p.P(22), p.P(22), p.P(22), p.P(22));
            if (it == null)
            {
                PK.N(side, p, "Nothing here yet.", 20, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter);
                return;
            }

            var top = PK.Row(side);
            top.style.alignSelf = Align.Stretch;
            var big = PK.Slot(top, p, it.boostArt, "", it.colour, p.Phone ? 120 : 130);
            if (it.boostArt == null) ShopSlotGood(big, p, it.goodId ?? it.id, p.Phone ? 120 : 130);
            var info = PK.Col(top);
            info.style.marginLeft = p.P(16);
            info.style.minWidth = 0;
            PK.T(info, p, it.name, 32);
            var tag = PK.N(info, p, ShopCats.First(x => x.id == it.cat).label, 16, Color.white);
            tag.style.alignSelf = Align.FlexStart;
            tag.style.backgroundColor = GameTheme.T("#1a8de8");
            Kit.Round(tag, p.U(6));
            PK.Pad(tag, p.P(16) * .5f, p.P(16) * .15f, p.P(16) * .5f, p.P(16) * .15f);
            tag.style.marginTop = p.P(10);
            PK.N(info, p, "You have " + PK.Num(it.count), 18, GameTheme.T("#9fd0ff")).style.marginTop = p.P(10);

            var desc = PK.N(side, p, it.about + (it.nft != null ? "" : "\n" + (it.use != null
                ? "<b><color=#7dff6a>" + it.use + "</color></b>"
                : "<color=#ffd23a>Shops pay <b>" + it.price + "</b> coins each.</color>")), 19, GameTheme.T("#dff1ff"), TextAnchor.UpperLeft, true);
            desc.enableRichText = true;
            desc.style.alignSelf = Align.Stretch;
            desc.style.marginTop = p.P(14);
            desc.style.backgroundColor = GameTheme.Light(.05f);
            Kit.Round(desc, p.U(10));
            PK.Pad(desc, p.P(12), p.P(12), p.P(12), p.P(12));

            PK.Grow(side);
            ShopHeldButton(side, p, sideW - 60);
            var entry = it;
            if (it.nft != null)
            {
                NftActions(side, p, sideW - 60, it.nft);
            }
            else if (it.use == null)
            {
                var step = PK.Row(side);
                step.style.alignSelf = Align.Stretch;
                step.style.justifyContent = Justify.SpaceBetween;
                step.style.height = p.P(60);
                step.style.flexShrink = 0;
                step.style.backgroundColor = PK.Ink;
                Kit.Round(step, p.U(10));
                PK.Pad(step, p.P(10), 0, p.P(10), 0);
                step.style.marginBottom = p.P(14);
                var minus = PK.Centre(PK.Box(step));
                PK.Size(minus, p.P(56), p.P(56));
                PK.T(minus, p, "−", 40);
                PK.Tap(minus, p, () => { _shopQty = Mathf.Max(1, _shopQty - 1); MarkDirty(); });
                PK.T(step, p, _shopQty + " / " + entry.count, 28);
                var plus = PK.Centre(PK.Box(step));
                PK.Size(plus, p.P(56), p.P(56));
                PK.T(plus, p, "+", 40);
                PK.Tap(plus, p, () => { _shopQty = Mathf.Min(entry.count, _shopQty + 1); MarkDirty(); });

                var sell = PK.Btn(side, p, "yellow", null, sideW - 60, 80, 32, () =>
                {
                    var got = ShopSell(entry.id, _shopQty);
                    if (got > 0) Say("Sold to the shop · +" + PK.Num(got) + " coins");
                    _shopQty = 1;
                    MarkDirty();
                });
                PK.T(sell, p, "Sell", 32).style.marginRight = p.P(10);
                PK.Img(sell, "ResourceBar_Icon_Gold", p.P(80) * .62f).style.marginRight = p.P(6);
                PK.T(sell, p, PK.Num(entry.price * _shopQty), 32);
                if (ChainReady)
                {
                    // the goods become one NFT the player can trade on the marketplace (docs 13 §7)
                    var qty = _shopQty;
                    var mint = PK.Btn(side, p, "purple", "Mint " + PK.Num(qty) + " as NFT", sideW - 60, 64, 26, () => ChainMintGoods(entry.id, qty));
                    mint.style.marginTop = p.P(10);
                }
            }
            else
            {
                var id = it.id.Substring("boost:".Length);
                PK.Btn(side, p, "green", "Use", sideW - 60, 80, 32, () =>
                {
                    ShopUseBoost(id);
                    MarkDirty();
                });
            }
        }

        /// <summary>Under Storage: redeemed NFT buildings waiting to be placed.</summary>
        private void ShopHeldButton(VisualElement parent, Pt p, float width)
        {
            var count = IslandServerState.Held.Count;
            if (count == 0) return;
            var b = PK.Btn(parent, p, "green", "NFT buildings to place (" + count + ")", width, 60, 22, () => Open("heldbuildings"));
            b.style.marginTop = p.P(10);
        }

        /// <summary>Puts a good's picture into a slot made without one (sized like the slot's own icon).</summary>
        private static void ShopSlotGood(VisualElement slot, Pt p, string item, float s)
        {
            var icon = ShopGoodIcon(p, item, p.P(s * .72f));
            slot.Insert(0, icon);
        }
    }
}
