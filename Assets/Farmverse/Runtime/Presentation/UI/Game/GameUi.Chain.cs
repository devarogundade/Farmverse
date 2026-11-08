#nullable enable
using System.Linq;
using System.Threading;
using Farmverse.Application.Features;
using Farmverse.Contracts;
using Farmverse.Core.Results;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Island items ⇄ NFTs (docs 13 §7): Mint from Storage or a selected building; redeemed buildings
    // wait in "NFT buildings" until placed. Only on the web build, where the page has a wallet.
    public sealed partial class GameUi
    {
        private static readonly bool RegChain_Held = Register("heldbuildings", UiLayer.Popup, (ui, c) => ui.ChainHeldPopup(c), .55f);

        private bool _chainBusy;

        /// <summary>Whether minting is offered here (web build with a wallet, signed in).</summary>
        private static bool ChainReady => Chain != null && Chain.Available;

        /// <summary>FARM as shown in the pills: "…" until the balance is known.</summary>
        private static string FarmText => Nfts?.FarmYocto == null ? "…" : NftService.Farm(Nfts.FarmYocto);

        /// <summary>
        /// The FARM pill for page headers (PK.HeaderFarm): the clover, the wallet's FARM kept current while it's on
        /// screen, its plus and the pill opening Get FARM. Null without a wallet (editor, no chain).
        /// </summary>
        private VisualElement? FarmPill(Pt p, float k)
        {
            if (!ChainReady) return null;
            var (pill, text) = PK.Currency(null, p, false, FarmText, k, () => Open("getfarm"), "Icon_Clover");
            PK.Tap(pill, p, () => Open("getfarm"));
            Nfts?.RefreshIfStale();
            pill.schedule.Execute(() => text.text = Loc.Of(FarmText)).Every(500);
            return pill;
        }

        // ------------------------------------------------------------------ links in and out (docs 15 §3.2)

        /// <summary>The page's URL when the game opened (or a link opened later); handled once signed in.</summary>
        public static string? PendingLink { get; set; }

        /// <summary>
        /// Links from the marketplace: <c>?open=nfts</c> (Storage's NFTs tab), <c>?open=shop</c>, <c>?open=pass</c>,
        /// <c>?redeemed=&lt;token&gt;</c> (redeemed on the market: fetch the island and say so).
        /// </summary>
        private void LinkTick()
        {
            var url = PendingLink;
            if (url == null || Nfts?.Account == null || Host == null) return;
            PendingLink = null;
            var q = url.IndexOf('?');
            if (q < 0) return;
            string? open = null, redeemed = null;
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;
                var key = part.Substring(0, eq);
                var value = System.Uri.UnescapeDataString(part.Substring(eq + 1));
                if (key == "open") open = value;
                else if (key == "redeemed") redeemed = value;
            }
            if (redeemed != null) LinkRedeemed();
            switch (open)
            {
                case "nfts":
                    _shopCat = "nft";
                    _shopSel = null;
                    Go("storage");
                    break;
                case "shop":
                    Go("shop");
                    break;
                case "pass":
                    Go("pass");
                    break;
            }
        }

        /// <summary>Redeemed on the market: the server credited it; the next sync brings it to the island.</summary>
        private async void LinkRedeemed()
        {
            Say("Your redeemed item is on its way to your farm…", 4f);
            if (Host != null) await IslandShadow.SyncNow(Host.CurrentState());
            if (Nfts != null) await Nfts.RefreshOwned();
            if (IslandServerState.Held.Count > 0) Open("heldbuildings");
            else Say("It's on your farm!");
        }

        private static string MarketBuy(string item) =>
            GameLinks.Market + "/#/?item=" + System.Uri.EscapeDataString(item) + "&sort=unit_price";

        // ------------------------------------------------------------------ drops: Farm Pass, gem packs, deeds (docs 15 §5)

        /// <summary>The Farm Pass drop on sale (the newest season's), or null.</summary>
        private static Farmverse.Application.Ports.MarketDrop? PassDrop() =>
            Nfts?.Drops.Where(d => d.ItemId == "farm_pass" && !d.Paused && d.Left != 0).OrderByDescending(d => d.StartsAt).FirstOrDefault();

        private static string DropArt(string itemId) =>
            itemId == "farm_pass" ? "ShopItem_SpecialChest_Green"
            : itemId == "builder_pass" ? "Icon_Hammer"
            : itemId.StartsWith("gems_") ? "Icon_Gems"
            : itemId == "deed" ? "Icon_Map"
            : "Icon_Gift";

        /// <summary>The pass popup's buttons: use a held pass, or buy one with FARM (one approval, used at once).</summary>
        private void PassBuyButtons(VisualElement box, Pt p, bool active)
        {
            if (!ChainReady || Nfts == null)
            {
                PK.N(box, p, "Premium is bought with FARM in the web version.", 18, Color.white, TextAnchor.MiddleCenter, true);
                return;
            }
            Nfts.RefreshIfStale();
            var held = Nfts.Holding("pack", "farm_pass");
            if (held != null)
            {
                PK.Btn(box, p, "green", active ? "Use your Farm Pass (saved for next season)" : "Use your Farm Pass", 520, 72, 24, () => ChainRedeem(held));
                return;
            }
            var drop = PassDrop();
            if (drop == null)
            {
                PK.N(box, p, "No Farm Pass on sale right now. Check the market.", 18, Color.white, TextAnchor.MiddleCenter, true);
                return;
            }
            var price = Farmverse.Application.Features.NftService.Farm(drop.Price);
            PK.Btn(box, p, _chainBusy ? "gray" : "purple", _chainBusy ? "Buying…" : (active ? "Buy one for next season · " : "Get Premium · ") + price + " FARM", 520, 76, 26,
                () => ShopBuyDrop(drop, use: true));
            if (drop.Left.HasValue) PK.N(box, p, PK.Num(drop.Left.Value) + " of " + PK.Num(drop.Cap ?? 0) + " Founders passes left", 16, GameTheme.C("#e3d0ff"), TextAnchor.MiddleCenter);
        }

        /// <summary>Buys a drop with FARM. Packs are used at once (one approval); a deed stays an NFT to use or sell.</summary>
        private async void ShopBuyDrop(Farmverse.Application.Ports.MarketDrop d, bool? use = null)
        {
            var chain = Chain;
            if (chain == null || _chainBusy) return;
            var useNow = use ?? (d.Kind == "pack");
            var have = Nfts?.FarmYocto;
            if (have != null && CompareYocto(have, d.Price) < 0)
            {
                Say("You need " + Farmverse.Application.Features.NftService.Farm(d.Price) + " FARM.", 3f);
                Open("getfarm");
                return;
            }
            _chainBusy = true;
            MarkDirty();
            try
            {
                Say("Approve the payment in your wallet", 4f);
                var r = await chain.BuyDrop(d.Id, d.Price, useNow, CancellationToken.None);
                if (!r.IsOk) Say(ChainError(r.Error, "buy that"), 4f);
                else if (r.Value.Declined) Say("Not bought. Your FARM is still yours.");
                else Say(useNow ? d.Title + ": done!" : d.Title + " is in your NFTs", 3f);
                if (Nfts != null)
                {
                    await Nfts.RefreshBalances();
                    await Nfts.RefreshDrops();
                    await Nfts.RefreshOwned();
                }
            }
            finally
            {
                _chainBusy = false;
                MarkDirty();
            }
        }

        /// <summary>Compares two non-negative integers written in decimal (yocto amounts).</summary>
        private static int CompareYocto(string a, string b)
        {
            a = a.TrimStart('0');
            b = b.TrimStart('0');
            return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        }

        // ------------------------------------------------------------------ merchants (docs 15 §13.1)

        private readonly System.Collections.Generic.Dictionary<string, (float at, Farmverse.Application.Ports.ItemOffer? best)> _offers = new();

        /// <summary>"Bea" for bea.farmverse.testnet (the Market Town cast), else the account.</summary>
        private static string BuyerName(string account)
        {
            var first = account.Split('.')[0];
            return first is "bea" or "ruth" or "tomas" or "ivy" ? char.ToUpperInvariant(first[0]) + first.Substring(1) : account;
        }

        /// <summary>The best standing offer for a good the player holds enough of: "Sell to Bea".</summary>
        private void MerchantOffer(VisualElement side, Pt p, float width, string item, int held)
        {
            var n = Nfts;
            if (n == null) return;
            if (!_offers.TryGetValue(item, out var cached) || Time.unscaledTime - cached.at > 60f)
            {
                _offers[item] = (Time.unscaledTime, cached.best);
                LoadOffers(n, item);
            }
            var best = _offers[item].best;
            if (best == null) return;
            var name = BuyerName(best.From);
            var lot = (int)System.Math.Max(1, best.MinQuantity);
            var label = $"{name} buys {IslandEconomyHost.NameOf(item)} ×{lot} for {NftService.Farm(best.Amount)} FARM";
            PK.N(side, p, label, 17, GameTheme.T("#ffd23a"), TextAnchor.MiddleCenter, true).style.marginTop = p.P(10);
            if (held >= lot)
                PK.Btn(side, p, _chainBusy ? "gray" : "green", "Sell to " + name, width, 60, 22, () => SellToMerchant(item, lot, best)).style.marginTop = p.P(6);
        }

        private async void LoadOffers(NftService n, string item)
        {
            var r = await n.OffersForItem(item);
            if (!r.IsOk) return;
            var best = r.Value.OrderByDescending(o => o.Amount.Length).ThenByDescending(o => o.Amount, System.StringComparer.Ordinal).FirstOrDefault();
            _offers[item] = (Time.unscaledTime, best);
            MarkDirty();
        }

        /// <summary>Mints the lot (sponsored) and accepts the merchant's offer: the FARM arrives, the goods go to them.</summary>
        private async void SellToMerchant(string item, int lot, Farmverse.Application.Ports.ItemOffer offer)
        {
            var chain = Chain;
            if (chain == null || _chainBusy) return;
            _chainBusy = true;
            MarkDirty();
            try
            {
                Say("Packing " + lot + " " + IslandEconomyHost.NameOf(item) + "…", 3f);
                if (Host != null) await IslandShadow.SyncNow(Host.CurrentState());
                var minted = await chain.MintGoods(item, lot, boost: false, CancellationToken.None);
                if (!minted.IsOk) { Say(ChainError(minted.Error), 4f); return; }
                if (minted.Value.Declined) { Say("Not sold. Your goods come back within the hour.", 4f); return; }
                Say("Approve the sale in your wallet", 4f);
                var sold = await chain.AcceptOffer(minted.Value.TokenId, offer.Id, CancellationToken.None);
                if (!sold.IsOk) Say("Minted, but the sale didn't go through. It's in your NFTs: sell it there.", 4.5f);
                else if (sold.Value.Declined) Say("Kept as an NFT: you can sell it from your NFTs.", 4f);
                else Say("Sold to " + BuyerName(offer.From) + " for " + NftService.Farm(offer.Amount) + " FARM!", 3.5f);
                _offers.Remove(item);
                if (Nfts != null)
                {
                    await Nfts.RefreshBalances();
                    await Nfts.RefreshOwned();
                }
            }
            finally
            {
                _chainBusy = false;
                MarkDirty();
            }
        }

        // ------------------------------------------------------------------ Farmhands (docs 15 §7)

        private float _crewNext, _notesNext;
        private int _goldenSeen = -1;

        /// <summary>The wallet's Farmhands (held and rented) onto the island, with their outfits; the server does the same on sync.</summary>
        private void CrewTick()
        {
            if (Time.unscaledTime < _crewNext) return;
            _crewNext = Time.unscaledTime + 3f;
            var eco = Economy;
            if (eco != null) IslandData.WildOpen = eco.WildPlotsOpen;   // land deeds: wild plots become buildable
            if (eco != null)
            {
                // a golden unit came up in the farmer's own harvest (docs 15 §12.4)
                var golden = eco.Stat("golden");
                if (_goldenSeen >= 0 && golden > _goldenSeen) Say("A golden harvest! It's worth " + Farmverse.Domain.Gameplay.GameplayRules.GoldenPriceFactor + "× and you can mint it.", 3.5f);
                _goldenSeen = golden;
            }
            var n = Nfts;
            if (n != null && ChainReady && Time.unscaledTime >= _notesNext)
            {
                _notesNext = Time.unscaledTime + 60f;
                NotesTick(n);
            }
            if (eco == null || n == null || !n.OwnedLoaded || n.OwnedFailed) return;
            var crew = n.Owned.Where(t => t.Kind == "worker" && t.Price == null).Select(CrewOf).ToList();
            // compared with what the island holds now (a server island arrives without the outfits)
            static string Key(System.Collections.Generic.IEnumerable<Farmverse.Domain.Gameplay.Farmhand> c) =>
                string.Join("|", c.OrderBy(f => f.token, System.StringComparer.Ordinal).Select(f => f.token + ":" + f.level + f.rented + f.look));
            if (Key(crew) == Key(eco.Crew)) return;
            eco.SetCrew(crew);
            Host?.Save();
            MarkDirty();
        }

        /// <summary>Market news as notes in the game (docs 15 §3.4): sold, sent to you, hired.</summary>
        private async void NotesTick(NftService n)
        {
            var news = await n.NewNotes();
            foreach (var text in news.Take(3)) Say(text, 4f);
        }

        private static Farmverse.Domain.Gameplay.Farmhand CrewOf(Farmverse.Application.Ports.OwnedNft t)
        {
            int Trait(string k) => t.Props.TryGetValue(k, out var v) ? System.Convert.ToInt32(v) : 1;
            return new Farmverse.Domain.Gameplay.Farmhand
            {
                token = t.TokenId,
                name = t.Props.TryGetValue("name", out var nm) && nm is string s ? s : t.Title,
                specialty = t.Props.TryGetValue("specialty", out var sp) && sp is string sps ? sps : "",
                speed = Trait("speed"), carry = Trait("carry"), stamina = Trait("stamina"), level = Trait("level"),
                rented = t.Rented,
                look = LookOf(t.Outfit),
            };
        }

        /// <summary>The marketplace's outfit keys → the island's dressing slots (CharacterParts).</summary>
        private static string LookOf(System.Collections.Generic.IReadOnlyDictionary<string, string> outfit)
        {
            var slots = new System.Collections.Generic.List<string>();
            foreach (var kv in outfit)
            {
                var slot = kv.Key switch
                {
                    "hair" => "Hairstyle", "hat" => "Hat", "face" => "Faces", "glasses" => "Glasses", "top" => "Outerwear",
                    "bottom" => "Pants", "glove" => "Gloves", "shoes" => "Shoes",
                    "accessory" => kv.Value.StartsWith("Mustache") ? "Mustache" : "Accessories",
                    _ => null,
                };
                if (slot != null) slots.Add(slot + "=" + kv.Value);
            }
            return string.Join(";", slots);
        }

        // ------------------------------------------------------------------ the NFTs tab (docs 15 §3.1)

        /// <summary>A Storage row for an NFT the wallet holds.</summary>
        private static ShopEntry NftEntry(Farmverse.Application.Ports.OwnedNft t)
        {
            var e = new ShopEntry { id = "nft:" + t.TokenId, name = t.Title, cat = "nft", colour = "Purple", count = (int)System.Math.Min(int.MaxValue, t.Quantity), nft = t };
            switch (t.Kind)
            {
                case "good":
                    e.goodId = t.ItemId;
                    e.about = t.Price != null ? "On the market for " + NftService.Farm(t.Price) + " FARM." : "An NFT of your goods. Redeem it to put them in your farmhouse, or sell it on the market.";
                    break;
                case "worker":
                    e.boostArt = "Icon_PictoIcon_User";
                    e.about = "A Farmhand. They work on your farm while you hold them" + (t.Props.TryGetValue("specialty", out var sp) ? ", best at " + sp : "") + ".";
                    break;
                case "pack":
                    e.boostArt = "Icon_Gift";
                    e.about = t.ItemId == "farm_pass" ? "A Farm Pass: one season of premium rewards. Redeem it to use it."
                        : t.ItemId == "builder_pass" ? "A Builder's Pass: one more builder for good."
                        : t.ItemId.StartsWith("gems_") ? "Gems. Redeem them into your wallet."
                        : "A pack. Redeem it to open it.";
                    break;
                case "land":
                    e.boostArt = t.ItemId == "deed" ? "Icon_Map" : null;
                    if (t.ItemId != "deed") e.goodId = t.ItemId;
                    e.about = t.ItemId == "deed" ? "A Genesis land deed. Redeem it to clear a wild plot on your island for good." : "A field. Redeem it and place it on your island.";
                    break;
                case "boost":
                    e.boostArt = "Icon_Potion02_Green";
                    e.about = "Boosts. Redeem them into your bag.";
                    break;
                default:
                    e.goodId = t.ItemId;
                    e.about = "A building. Redeem it and place it on your island.";
                    break;
            }
            return e;
        }

        private static string NftEmptyText()
        {
            if (Nfts == null || !ChainReady) return "NFTs need the web version with a NEAR wallet.";
            if (!Nfts.OwnedLoaded) return "Looking for your NFTs…";
            if (Nfts.OwnedFailed) return "Can't reach the market right now. Try again in a moment.";
            return "No NFTs yet: mint goods here, or buy on the market.";
        }

        private static string MarketToken(string tokenId, bool sell = false) =>
            GameLinks.Market + "/#/token/" + System.Uri.EscapeDataString(tokenId) + (sell ? "?sell=1" : "");

        /// <summary>The NFTs tab's buttons: Redeem / Sell on market, or what the token is doing.</summary>
        private void NftActions(VisualElement side, Pt p, float width, Farmverse.Application.Ports.OwnedNft t)
        {
            if (t.Price != null)
            {
                PK.Btn(side, p, "blue", "Listed · " + NftService.Farm(t.Price) + " FARM", width, 72, 26, () => GameLinks.Open(MarketToken(t.TokenId), "Open the market"));
                PK.N(side, p, "Take it off the market to redeem it.", 16, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter).style.marginTop = p.P(8);
                return;
            }
            if (t.Kind == "worker")
            {
                PK.N(side, p, "Working on your farm", 22, GameTheme.T("#7dff6a"), TextAnchor.MiddleCenter).style.marginBottom = p.P(10);
                PK.Btn(side, p, "yellow", "Sell or rent out", width, 72, 26, () => GameLinks.Open(MarketToken(t.TokenId), "Open the market"));
                return;
            }
            var deedFull = t.Kind == "land" && t.ItemId == "deed" && Economy != null && Economy.WildPlotsOpen >= Farmverse.Domain.Gameplay.GameplayRules.WildPlots;
            if (deedFull)
                PK.N(side, p, "All your wild land is open already. You can still sell this deed.", 17, GameTheme.T("#ffd23a"), TextAnchor.MiddleCenter, true).style.marginBottom = p.P(10);
            else
                PK.Btn(side, p, "green", _chainBusy ? "Redeeming…" : "Redeem", width, 80, 32, () => ChainRedeem(t));
            var sell = PK.Btn(side, p, "yellow", "Sell on market", width, 64, 26, () => GameLinks.Open(MarketToken(t.TokenId, sell: true), "Sell on the market"));
            sell.style.marginTop = p.P(10);
        }

        /// <summary>Burns an NFT the wallet holds and credits it to the island (goods, buildings, packs, deeds).</summary>
        private async void ChainRedeem(Farmverse.Application.Ports.OwnedNft t)
        {
            var chain = Chain;
            if (chain == null || _chainBusy) return;
            _chainBusy = true;
            MarkDirty();
            try
            {
                Say("Approve the redeem in your wallet", 4f);
                var r = await chain.Redeem(t.TokenId, CancellationToken.None);
                if (r.IsOk && r.Value.Declined) Say("Not redeemed. It's still yours.");
                else if (r.IsOk)
                {
                    Say(t.Kind switch
                    {
                        "good" => t.Title + " is in your farmhouse",
                        "pack" => t.ItemId == "farm_pass" ? "Your Farm Pass is on!" : t.Title + " opened",
                        "land" when t.ItemId == "deed" => "A wild plot is clear. Build on it!",
                        "boost" => t.Title + " is in your bag",
                        _ => t.Title + " is ready to place",
                    });
                    if (t.Kind is "building" or "tool" || (t.Kind == "land" && t.ItemId != "deed")) Open("heldbuildings");
                }
                else Say(ChainError(r.Error, "redeem"));
                if (Nfts != null) await Nfts.RefreshOwned();
            }
            finally
            {
                _chainBusy = false;
                MarkDirty();
            }
        }

        /// <summary>Mints goods from the farmhouse as one NFT: the island is synced first so the server has them.</summary>
        private async void ChainMintGoods(string item, int quantity)
        {
            var chain = Chain;
            if (chain == null || _chainBusy || quantity < 1) return;
            _chainBusy = true;
            try
            {
                Say("Getting your goods ready…", 3f);
                if (Host != null) await IslandShadow.SyncNow(Host.CurrentState());
                Say("Approve the mint in your wallet", 4f);
                var r = await chain.MintGoods(item, quantity, boost: false, CancellationToken.None);
                ChainSay(r, $"Minted {quantity} {IslandEconomyHost.NameOf(item)} as an NFT. List it on the marketplace!");
            }
            finally
            {
                _chainBusy = false;
                MarkDirty();
            }
        }

        /// <summary>Picks up the selected building (or field) as one NFT.</summary>
        private async void ChainMintBuilding(int uid, string id)
        {
            var chain = Chain;
            if (chain == null || _chainBusy) return;
            _chainBusy = true;
            try
            {
                Say("Getting your building ready…", 3f);
                if (Host != null) await IslandShadow.SyncNow(Host.CurrentState());
                Say("Approve the mint in your wallet", 4f);
                var r = await chain.MintBuilding(uid, land: id == Farmverse.Domain.Gameplay.GameplayRules.FieldBuilding, CancellationToken.None);
                ChainSay(r, $"{IslandEconomyHost.NameOf(id)} is now an NFT. Redeem it to place it on any farm.");
            }
            finally
            {
                _chainBusy = false;
                MarkDirty();
            }
        }

        private void ChainSay(Result<ChainOutcome> r, string done)
        {
            if (!r.IsOk) Say(ChainError(r.Error), 4f);
            else if (r.Value.Declined) Say("Mint cancelled in the wallet. The items come back to your farm within the hour.", 4.5f);
            else Say(done, 4f);
        }

        /// <summary>Hours a new farm waits before minting (remote config, set by GameRoot).</summary>
        public static long MintMinHours { get; set; } = 24;

        /// <summary>The server's testnet faucet is open (Get FARM sheet).</summary>
        public static System.Func<bool> FaucetOpen { get; set; } = () => false;

        /// <summary>"New farms can mint after …" in days when it's whole days, else hours.</summary>
        internal static string MintTooSoonText(long hours)
        {
            if (hours <= 0) return "This farm can't mint yet. Try again in a moment.";
            if (hours == 24) return "New farms can mint after their first day.";
            if (hours % 24 == 0) return $"New farms can mint after {hours / 24} days.";
            return hours == 1 ? "New farms can mint after their first hour." : $"New farms can mint after {hours} hours.";
        }

        /// <summary>A mint refusal in the player's words.</summary>
        private static string ChainError(AppError e, string action = "mint")
        {
            string P(string key) => e.Params.TryGetValue(key, out var v) ? v : "";
            switch (e.Code)
            {
                case ErrorCode.MintLocked: return "Minting is paused on this farm while we check it. Playing is fine.";
                case ErrorCode.MintLimit: return $"Today's mint limit is reached ({P("left")} coins of value left today).";
                case ErrorCode.MintTooSoon: return MintTooSoonText(MintMinHours);
                case ErrorCode.MintNotEnough: return $"Not enough in the farmhouse yet (the server sees {P("have")}). Try again in a moment.";
                case ErrorCode.MintBuildingBusy:
                    return P("reason") switch
                    {
                        "not_empty" => "Empty it first: collect what's inside.",
                        "busy" => "Wait until it finishes working.",
                        "core_building" => "The farmhouse and the market stay on the farm.",
                        "under_construction" => "Finish building it first.",
                        "ruined" => "Repair it first.",
                        _ => "This building can't be minted right now.",
                    };
                case ErrorCode.MintNotAllowed: return "That can't be minted.";
                case ErrorCode.MintLotTooSmall: return $"Mint at least {P("min")} at a time.";
                case ErrorCode.MintNoCoins: return $"Minting this costs {P("fee")} coins. Sell a few goods first.";
                case ErrorCode.Unsupported: return "Minting needs the web version with a NEAR wallet.";
                case ErrorCode.NetworkRequired: return "You're offline. Try again when connected.";
                default: return $"Couldn't {action} right now. Please try again.";
            }
        }

        /// <summary>Redeemed buildings waiting to be placed (they keep their level, nothing to pay).</summary>
        private void ChainHeldPopup(Ctx c)
        {
            var p = c.P;
            var held = IslandServerState.Held;
            var pop = PK.Popup(c.Root, p, 620, 0, "NFT buildings", Back);
            var col = PK.Col(pop, p.P(12));
            PK.Pad(col, p.P(8), p.P(16), p.P(8), p.P(8));
            if (held.Count == 0)
            {
                PK.N(col, p, "Nothing waiting. Buildings you redeem from NFTs appear here.", 18, GameTheme.C("#dff1ff"), TextAnchor.MiddleCenter, true);
                return;
            }
            foreach (var h in held)
            {
                var row = PK.Row(col, p.P(12));
                row.style.alignItems = Align.Center;
                row.style.justifyContent = Justify.SpaceBetween;
                PK.T(row, p, IslandEconomyHost.NameOf(h.id) + (h.level > 1 ? "  ·  level " + h.level : ""), 24);
                var item = h;
                PK.Btn(row, p, "green", "Place", 150, 58, 22, () =>
                {
                    CloseAll();
                    if (Sandbox == null || !Sandbox.PlaceHeld(item)) Say("This building isn't available on this island.", 3f);
                });
            }
        }
    }
}
