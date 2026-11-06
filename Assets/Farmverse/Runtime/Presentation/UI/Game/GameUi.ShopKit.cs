#nullable enable
using System;
using Farmverse.Contracts;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Shared parts of the Shop, Storage and Orders & Goals pages: goods art, the island's stores, selling,
    // boosts (server inventory; their effects on the island) and small widgets.
    public sealed partial class GameUi
    {
        // ------------------------------------------------------------------ goods

        /// <summary>The pack picture standing in for a good (the pack has no farm goods art; null = draw a lettered badge).</summary>
        private static string? ShopGoodArt(string item) => item switch
        {
            "wheat" or "maize" or "beans" => "G_Wheat",
            "milk" => "G_Milk",
            "wood" or "planks" => "G_Wood",
            "stone" => "G_Stone",
            "flour" or "croissant" => "G_Flour",
            "water" => "G_Water",
            "egg" => "Icon_Egg",
            "meat" => "Icon_Food_Meat",
            "yogurt" => "G_Milk",
            "tomato" => "G_Tomato",
            "sunflower" or "oil" => "G_Sunflower",
            "carrot" or "cabbage" => "G_Carrot",
            "flowers" => "G_Flower",
            "mushroom" or "veg_box" or "juice" => "Icon_Clover",
            "pumpkin" => "Icon_Pumkin",
            "gears" => "Icon_GearWheels",
            _ => null,
        };

        /// <summary>A good's picture: the pack icon, or a round badge in the good's colour with its first letter.</summary>
        private static VisualElement ShopGoodIcon(Pt p, string item, float size)
        {
            var art = ShopGoodArt(item);
            if (art != null) return PK.Fit(null, art, size, size);
            var b = PK.Centre(PK.Box());
            PK.Size(b, size, size);
            var inner = PK.Centre(PK.Box(b));
            PK.Size(inner, size * .82f, size * .82f);
            inner.style.backgroundColor = IslandEconomyHost.GoodColour(item);
            Kit.Round(inner, size);
            Kit.Border(inner, Mathf.Max(1, size * .05f), PK.Ink);
            var l = PK.T(inner, p, item.Length > 0 ? char.ToUpperInvariant(item[0]).ToString() : "?", 1);
            l.style.fontSize = size * .46f;
            l.style.unityTextOutlineWidth = Mathf.Max(1, size * .06f);
            return b;
        }

        private static string ShopGoodName(string item) => IslandEconomyHost.PrettyOf(item);

        /// <summary>Storage kind of a good: crops, animal, mat (or boost for the local boosts).</summary>
        private static string ShopKindOf(string item) =>
            GameplayRules.IsCrop(item) ? "crops"
            : item is "egg" or "milk" or "meat" or "fish" or "yogurt" ? "animal"
            : "mat";

        private static string ShopSlotColour(string item) => ShopKindOf(item) switch
        {
            "crops" => item is "mushroom" or "flowers" ? "Green" : "Yellow",
            "animal" => item is "meat" ? "Brown" : "Blue",
            _ => item is "flour" or "croissant" ? "Yellow" : item == "water" ? "Blue" : "Brown",
        };

        private static string ShopGoodAbout(string item) => item switch
        {
            "wheat" => "Grown in fields. The windmill turns it into flour.",
            "maize" => "Grown in fields. Animal feed is made from it.",
            "beans" => "Grown in fields.",
            "egg" => "Laid in the chicken coop.",
            "milk" => "From the cows. The dairy makes cheese from it.",
            "meat" => "From the pig pen. Feeds your townsfolk.",
            "wood" => "Chopped from trees. Builds and repairs.",
            "stone" => "Broken from rocks or the quarry.",
            "flour" => "Made in the windmill. The bakery needs it.",
            "croissant" => "Baked in the bakery.",
            "yogurt" => "Made in the dairy from milk.",
            "veg_box" => "Packed in the packing shed: cabbages and pumpkins.",
            "planks" => "Sawn from wood.",
            "fish" => "Caught at the fishing hut.",
            _ => GameplayRules.IsCrop(item) ? "Grown on your island." : "Made on your island.",
        };

        // ------------------------------------------------------------------ the island's stores

        /// <summary>The farmhouses and silos that hold the island's goods.</summary>
        private List<Department> ShopStores()
        {
            var eco = Economy;
            if (eco == null) return new List<Department>();
            return eco.Departments.Values.Where(d => (d.Levels.Kind == DepartmentKind.Farmhouse || d.Levels.Kind == DepartmentKind.Silo) && eco.Working(d.Uid)).ToList();
        }

        /// <summary>Everything in store, most first.</summary>
        private List<(string item, int n)> ShopStock() =>
            ShopStores().SelectMany(d => d.Stock).GroupBy(kv => kv.Key).Select(g => (item: g.Key, n: g.Sum(kv => kv.Value)))
                .Where(x => x.n > 0).OrderByDescending(x => x.n).ThenBy(x => x.item, StringComparer.Ordinal).ToList();

        private int ShopHave(string item) => ShopStores().Sum(d => d.Stock.TryGetValue(item, out var n) ? n : 0);

        /// <summary>What the market stall pays for one now (it buys anything; specialist shops pay more on the island).</summary>
        private int ShopPrice(string item) => Economy?.MarketPrice("market_stall", item) ?? GameplayRules.PriceOf(item);

        /// <summary>Sells goods straight from store at the market stall's price. Returns the coins made.</summary>
        private int ShopSell(string item, int count)
        {
            var eco = Economy;
            if (eco == null || count <= 0) return 0;
            var price = ShopPrice(item);
            var taken = 0;
            foreach (var d in ShopStores())
            {
                if (taken >= count) break;
                taken += d.TakeStock(item, count - taken);
            }

            if (taken <= 0) return 0;
            eco.AddCoins(price * taken);
            eco.Count("sold", taken);
            IslandAudio.Play("coin");
            return price * taken;
        }

        // ------------------------------------------------------------------ boosts

        /// <summary>
        /// How boosts look in the game (names, pictures, words). How many are held and how long they run are the
        /// server's (state/inventory, the catalogue's <c>boosts</c>); prices are the catalogue's boost_* offers.
        /// The island's economy has no boost hooks yet: gameplay should read <see cref="ShopBoostRunning"/>.
        /// </summary>
        private static readonly (string id, string name, string art, string sub, string use)[] ShopBoosts =
        {
            ("shield", "Raid Shield", "Isl_x_shield", "8 hours", "Zombies stay away for 8 hours."),
            ("fert", "Fertilizer", "Isl_x_fert", "Crops 2× · 30 min", "Crops grow 2× for 30 min."),
            ("rush", "Worker Rush", "Isl_x_rush", "Workers 2× · 30 min", "Workers 2× faster for 30 min."),
            ("instant", "Instant Build", "Isl_x_instant", "Finish one build now", "Finishes one building now."),
            ("basket", "Bigger Basket", "Isl_x_basket", "+10 slots forever", "+10 basket slots, for good."),
            ("nightshift", "Night Shift", "Isl_x_nightshift", "24 h · normal food", "Your next absence: workers keep going for 24 hours at the normal food price."),
            ("lunch", "Lunch Basket", "Isl_x_lunch", "Feeds every workers' house", "A full meal for every workers' house, so they keep working."),
        };

        /// <summary>The picture of a boost by id (prizes and rewards show boosts by it).</summary>
        public static string BoostArt(string id) => ShopBoosts.FirstOrDefault(x => x.id == id).art ?? "Icon_Booster";

        /// <summary>Boosts held: the server's inventory (state/inventory).</summary>
        private static int ShopBoostCount(string id) => Wallet.Shop?.Boosts(id) ?? 0;

        /// <summary>True while a timed boost runs (the server's activeUntil).</summary>
        private static bool ShopBoostRunning(string id) => (Wallet.Shop?.ActiveUntil(id) ?? 0) > (long)(IslandEconomyHost.Clock * 1000);

        private bool _shopUsing;

        /// <summary>Uses one boost (boost-use): the server takes it from Storage and starts its timer.</summary>
        private async void ShopUseBoost(string id)
        {
            var shop = Wallet.Shop;
            if (_shopUsing || ShopBoostCount(id) <= 0) return;
            if (shop == null || !shop.Online)
            {
                Say("Connect to the internet to use boosts");
                return;
            }
            var boost = BoostIdWire.FromWire(id);
            if (boost == BoostId.Unknown) return;
            var eco = Economy;
            if (id == "nightshift" && eco?.NightShift == true)
            {
                Say("Your Night Shift is already booked for your next absence");
                return;
            }
            if (id == "lunch" && (eco == null || !eco.Has("workers_house")))
            {
                Say("Build a workers' house first");
                return;
            }
            if (id == "instant" && (eco == null || !eco.Sites.Any(site => !site.Repair)))
            {
                Say("Nothing is being built right now");
                return;
            }
            _shopUsing = true;
            var result = await shop.UseBoost(boost);
            _shopUsing = false;
            var name = ShopBoosts.FirstOrDefault(x => x.id == id).name ?? id;
            if (result.IsFailure)
            {
                Say(result.Error.Code == ErrorCode.BoostNone ? "No " + name + " left" : "Couldn't use it — check your connection and try again");
                MarkDirty();
                return;
            }
            // what acts at once happens on the island now (timed ones follow the server's timer: BoostSync)
            var now = IslandEconomyHost.Clock;
            if (id == "instant") Economy?.FinishOneSite(now);
            else if (id == "basket") Economy?.AddBasketSlots(BasketSlotsPerBoost);
            else if (id == "nightshift") Economy?.StartNightShift();
            else if (id == "lunch") Economy?.Lunch();
            BoostSync();
            Host?.Save();
            IslandAudio.Play("cheer");
            Say(name + " used!");
            MarkDirty();
        }

        /// <summary>Basket slots one Bigger Basket adds.</summary>
        private const int BasketSlotsPerBoost = 10;

        /// <summary>
        /// The island follows the server: Fertilizer's timer (the economy sows faster before it), the builders
        /// Builder's Passes added, and Worker Rush / Raid Shield (read by the island's people).
        /// </summary>
        private void BoostSync()
        {
            var eco = Economy;
            if (eco == null) return;
            var fert = (Wallet.Shop?.ActiveUntil("fert") ?? 0) / 1000.0;
            if (fert > eco.FertUntil) eco.Fertilize(IslandEconomyHost.Clock, fert);   // crops already growing speed up too
            var rush = (Wallet.Shop?.ActiveUntil("rush") ?? 0) / 1000.0;
            if (rush > eco.RushUntil) eco.RushUntil = rush;
        }

        // ------------------------------------------------------------------ shop timers

        private static long ShopToday => (long)Math.Floor(IslandEconomyHost.Clock / 86400.0);

        /// <summary>Time to the next UTC midnight (when the island's daily goals roll), as "9h 12m".</summary>
        private static string ShopUntilMidnight()
        {
            var left = (ShopToday + 1) * 86400.0 - IslandEconomyHost.Clock;
            return ShopSpan(left);
        }

        private static string ShopSpan(double seconds)
        {
            seconds = Math.Max(0, seconds);
            var h = (int)(seconds / 3600);
            var m = (int)(seconds % 3600 / 60);
            if (h > 0) return $"{h}h {m}m";
            var s = (int)(seconds % 60);
            return $"{m}m {s:00}s";
        }

        /// <summary>The UTC day as the server writes it (yyyy-MM-dd): chests and daily limits reset at UTC midnight.</summary>
        private static string ShopUtcDay => DateTimeOffset.FromUnixTimeSeconds((long)IslandEconomyHost.Clock).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ widgets

        /// <summary>The kit's coupon icon: the pack's gold ticket with a stroked %.</summary>
        private static VisualElement ShopCouponIcon(float w)
        {
            var h = w * 242f / 399f;
            var e = PK.Centre(PK.Box());
            PK.Size(e, w, h);
            Kit.Picture(e, GameArt.Get("Icon_ImageIcon_Pass_Golden"));
            var l = new Label("%") { pickingMode = PickingMode.Ignore };
            var font = GameArt.Display;
            if (font != null) l.style.unityFontDefinition = FontDefinition.FromFont(font);
            l.style.fontSize = h * .62f;
            l.style.color = Color.white;
            l.style.unityTextOutlineWidth = Mathf.Max(2, h * .09f);
            l.style.unityTextOutlineColor = GameTheme.C("#b25b00");
            l.style.marginTop = -h * .06f;
            e.Add(l);
            return e;
        }

        /// <summary>A picture dimmed and desaturated (CSS filter on the inactive tabs).</summary>
        private static void ShopDull(VisualElement e, bool dull) =>
            e.style.unityBackgroundImageTintColor = dull ? new Color(.62f, .66f, .72f, 1f) : Color.white;

        /// <summary>Every scrolled list keeps a gap between its items: any item after the first gets at least
        /// <paramref name="gap"/> before it (a bigger gap the caller set is kept).</summary>
        private static void SpaceItems(ScrollView sv, bool horizontal, float gap)
        {
            var c = sv.contentContainer;
            c.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (c.resolvedStyle.flexWrap != Wrap.NoWrap) return;   // grids space themselves
                VisualElement? prev = null;
                foreach (var ch in c.Children())
                {
                    if (ch.resolvedStyle.position == Position.Absolute || ch.resolvedStyle.display == DisplayStyle.None) continue;
                    if (prev != null)
                    {
                        // the space already there: this item's leading margin plus the one before's trailing margin
                        var have = horizontal ? ch.resolvedStyle.marginLeft + prev.resolvedStyle.marginRight
                                              : ch.resolvedStyle.marginTop + prev.resolvedStyle.marginBottom;
                        if (have + .01f < gap)
                        {
                            var add = gap - have + (horizontal ? ch.resolvedStyle.marginLeft : ch.resolvedStyle.marginTop);
                            if (horizontal) ch.style.marginLeft = add; else ch.style.marginTop = add;
                        }
                    }
                    prev = ch;
                }
            });
        }

        /// <summary>A ScrollView with the scrollers hidden, scrolled by touch, by mouse drag and by the wheel.</summary>
        private static ScrollView ShopScroller(VisualElement parent, bool horizontal, float gap)
        {
            var sv = new ScrollView(horizontal ? ScrollViewMode.Horizontal : ScrollViewMode.Vertical);
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            sv.style.flexGrow = 1;
            sv.style.flexShrink = 1;
            sv.pickingMode = PickingMode.Position;
            parent.Add(sv);
            SpaceItems(sv, horizontal, gap);

            if (horizontal)
                sv.RegisterCallback<WheelEvent>(e =>
                {
                    sv.scrollOffset = new Vector2(sv.scrollOffset.x + e.delta.y * 30f, sv.scrollOffset.y);
                    e.StopPropagation();
                });

            // mouse drag (touch already scrolls)
            var down = false;
            var moved = 0f;
            Vector2 last = default;
            sv.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.pointerType != "mouse") return;
                down = true;
                moved = 0;
                last = e.position;
            }, TrickleDown.TrickleDown);
            sv.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!down || e.pointerType != "mouse") return;
                if ((e.pressedButtons & 1) == 0) { down = false; return; }
                Vector2 pos = e.position;
                var d = pos - last;
                last = pos;
                moved += d.magnitude;
                if (moved < 8) return;
                sv.scrollOffset = horizontal ? new Vector2(sv.scrollOffset.x - d.x, 0) : new Vector2(0, sv.scrollOffset.y - d.y);
            }, TrickleDown.TrickleDown);
            sv.RegisterCallback<PointerUpEvent>(_ => down = false, TrickleDown.TrickleDown);
            return sv;
        }

        /// <summary>A picture element, fitted, for flex rows (CSS img with height, contain).</summary>
        private static VisualElement ShopImg(VisualElement? parent, string art, float h, float w = float.NaN) => PK.Img(parent, art, h, w);

        /// <summary>The kit's navy pill (.pill.navy) used for the storage capacity and the order timer.</summary>
        private static VisualElement ShopNavyPill(Pt p)
        {
            var pill = PK.Row(null);
            pill.style.height = p.U(p.Phone ? 28 : 50);
            pill.style.flexShrink = 0;
            pill.style.marginLeft = p.U(10);
            pill.style.backgroundColor = GameTheme.T("#041634", .8f);
            Kit.Round(pill, p.U(10));
            return pill;
        }
    }
}
