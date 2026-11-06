#nullable enable
using System.Threading;
using Farmverse.Application.Features;
using Farmverse.Contracts;
using Farmverse.Domain.Gameplay;
using Farmverse.Presentation.World.Island;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // The mint sheet (docs 15 §4.1): one panel for goods, boosts and buildings. Choose how many (quick chips and a
    // stepper), see what it's worth, today's allowance and the fee, then four steps tick off: packing, approve in
    // the wallet, minting, done (Sell on market / Keep). Vouchers the player didn't finish can be signed from here.
    public sealed partial class GameUi
    {
        private static readonly bool RegMint = Register("mint", UiLayer.Popup, (ui, c) => ui.MintSheet(c), .5f);

        /// <summary>What the sheet mints: goods/boosts by item and how many are held, or a placed building by uid.</summary>
        private sealed class MintArgs
        {
            public string Kind = "good";   // good | boost | building | land
            public string Item = "";
            public int Uid;
            public int Max = 1;
            public string Name = "";
        }

        private MintArgs? _mint;
        private int _mintQty;
        /// <summary>0 choose · 1 packing · 2 approve in wallet · 3 done · -1 failed.</summary>
        private int _mintStage;
        private string _mintMsg = "", _mintToken = "";
        private ChainPendingMintsResponse? _mintInfo;
        private bool _mintInfoLoading;

        private const int MintLot = 10;

        /// <summary>Opens the sheet for goods or boosts held.</summary>
        private void MintOpenGoods(string item, int held, bool boost)
        {
            var args = new MintArgs { Kind = boost ? "boost" : "good", Item = item, Max = held, Name = boost ? item : IslandEconomyHost.NameOf(item) };
            var lot = boost || item.StartsWith(GameplayRules.GoldenPrefix, System.StringComparison.Ordinal) ? 1 : MintLot;
            MintOpen(args, Mathf.Clamp(lot, 1, Mathf.Max(1, held)));
        }

        /// <summary>Opens the sheet for a placed building (or a field).</summary>
        private void MintOpenBuilding(int uid, string id) =>
            MintOpen(new MintArgs { Kind = id == GameplayRules.FieldBuilding ? "land" : "building", Item = id, Uid = uid, Max = 1, Name = IslandEconomyHost.NameOf(id) }, 1);

        private void MintOpen(MintArgs args, int qty)
        {
            _mint = args;
            _mintQty = qty;
            _mintStage = 0;
            _mintMsg = _mintToken = "";
            MintLoadInfo();
            Open("mint");
        }

        private async void MintLoadInfo()
        {
            var chain = Chain;
            if (chain == null || _mintInfoLoading) return;
            _mintInfoLoading = true;
            try
            {
                var r = await chain.PendingMints(CancellationToken.None);
                if (r.IsOk) _mintInfo = r.Value;
            }
            finally
            {
                _mintInfoLoading = false;
                MarkDirty();
            }
        }

        private int MintLotFor(MintArgs a) =>
            a.Kind != "good" || a.Item.StartsWith(GameplayRules.GoldenPrefix, System.StringComparison.Ordinal) ? 1 : MintLot;

        private void MintSheet(Ctx c)
        {
            var p = c.P;
            var a = _mint;
            var pop = PK.Popup(c.Root, p, 640, 0, _mintStage == 3 ? "Minted!" : "Mint as NFT", () => { if (_mintStage is 1 or 2) return; Back(); });
            var col = PK.Col(pop, p.P(12));
            col.style.alignItems = Align.Stretch;
            PK.Pad(col, p.P(8), p.P(12), p.P(8), p.P(8));
            if (a == null) return;
            c.Watch(() => _mintStage + "|" + _mintQty + "|" + (_mintInfo == null ? "" : _mintInfo.SpentToday + "/" + _mintInfo.Mints.Count) + "|" + _mintMsg);

            // what
            var head = PK.Row(col, p.P(14));
            head.style.alignItems = Align.Center;
            var slot = PK.Slot(head, p, a.Kind == "boost" ? "Icon_Potion02_Green" : null, "", "Purple", 110);
            if (a.Kind != "boost") ShopSlotGood(slot, p, a.Item, 110);
            var info = PK.Col(head);
            PK.T(info, p, a.Name + (a.Kind is "good" or "boost" ? " ×" + PK.Num(_mintQty) : ""), 30);
            var unit = a.Kind is "good" ? GameplayRules.PriceOf(a.Item) : 0;
            var value = unit * _mintQty;
            if (value > 0) PK.N(info, p, "Worth about " + PK.Num(value) + " coins", 18, GameTheme.T("#9fd0ff")).style.marginTop = p.P(6);

            switch (_mintStage)
            {
                case 0: MintChoose(col, p, a, value); break;
                case 1:
                case 2: MintSteps(col, p, a); break;
                case 3: MintDone(col, p); break;
                default: MintFailed(col, p); break;
            }
        }

        private void MintChoose(VisualElement col, Pt p, MintArgs a, int value)
        {
            var lot = MintLotFor(a);
            if (a.Kind is "good" or "boost" && a.Max > 1)
            {
                var chips = PK.Row(col, p.P(10));
                foreach (var n in new[] { 10, 50, 100 })
                {
                    if (n > a.Max || n < lot) continue;
                    var q = n;
                    PK.Btn(chips, p, _mintQty == q ? "yellow" : "blue", PK.Num(q), 110, 54, 22, () => { _mintQty = q; MarkDirty(); });
                }
                PK.Btn(chips, p, _mintQty == a.Max ? "yellow" : "blue", "All " + PK.Num(a.Max), 150, 54, 22, () => { _mintQty = a.Max; MarkDirty(); });

                var step = PK.Row(col);
                step.style.justifyContent = Justify.SpaceBetween;
                step.style.alignItems = Align.Center;
                step.style.height = p.P(60);
                step.style.backgroundColor = PK.Ink;
                Kit.Round(step, p.U(10));
                PK.Pad(step, p.P(10), 0, p.P(10), 0);
                var minus = PK.Centre(PK.Box(step));
                PK.Size(minus, p.P(56), p.P(56));
                PK.T(minus, p, "−", 40);
                PK.Tap(minus, p, () => { _mintQty = Mathf.Max(lot, _mintQty - 1); MarkDirty(); });
                PK.T(step, p, PK.Num(_mintQty) + " / " + PK.Num(a.Max), 28);
                var plus = PK.Centre(PK.Box(step));
                PK.Size(plus, p.P(56), p.P(56));
                PK.T(plus, p, "+", 40);
                PK.Tap(plus, p, () => { _mintQty = Mathf.Min(a.Max, _mintQty + 1); MarkDirty(); });
            }

            // today's allowance and the fee
            var mi = _mintInfo;
            var fee = 0;
            if (mi != null)
            {
                if (a.Kind == "good" && mi.DailyCap > 0)
                {
                    var spent = (int)Mathf.Min(mi.SpentToday, mi.DailyCap);
                    PK.Bar(col, p, 580, 36, mi.DailyCap > 0 ? (spent + value) / (float)mi.DailyCap : 0f,
                        "Today: " + PK.Num(spent + value) + " / " + PK.Num(mi.DailyCap), blue: spent + value <= mi.DailyCap);
                }
                if (a.Kind != "boost") fee = Mathf.Max((int)mi.FeeMin, (int)((long)value * mi.FeeBps / 10_000));
            }
            PK.N(col, p, (fee > 0 ? "Fee: " + PK.Num(fee) + " coins · " : "") + "Network: about 0.01 NEAR (the rest comes back)", 17, GameTheme.T("#dff1ff"), TextAnchor.MiddleCenter, true);

            string? why = null;
            if (a.Kind is "good" && _mintQty < lot) why = "Mint at least " + lot + " at a time.";
            else if (mi != null && a.Kind == "good" && mi.DailyCap > 0 && mi.SpentToday + value > mi.DailyCap)
                why = "That's over today's limit (" + PK.Num(System.Math.Max(0, mi.DailyCap - mi.SpentToday)) + " coins of value left).";
            else if (fee > 0 && Economy != null && Economy.Coins < fee) why = "You need " + PK.Num(fee) + " coins for the fee.";
            if (why != null) PK.N(col, p, why, 18, GameTheme.T("#ffd23a"), TextAnchor.MiddleCenter, true);
            PK.Btn(col, p, why == null ? "purple" : "gray", "Mint", 580, 80, 32, () => { if (why == null) MintGo(); });

            // vouchers the player didn't finish
            if (mi != null && mi.Mints.Count > 0)
            {
                PK.T(col, p, "Unfinished mints", 24).style.marginTop = p.P(10);
                foreach (var m in mi.Mints)
                {
                    var row = PK.Row(col, p.P(10));
                    row.style.alignItems = Align.Center;
                    row.style.justifyContent = Justify.SpaceBetween;
                    PK.N(row, p, m.Voucher.Title, 18, Color.white);
                    var pending = m;
                    PK.Btn(row, p, "green", "Finish", 140, 52, 20, () => MintFinish(pending));
                }
            }
        }

        private void MintSteps(VisualElement col, Pt p, MintArgs a)
        {
            var steps = new[] { a.Kind is "building" or "land" ? "Picking it up" : "Packing your goods", "Approve in your wallet", "Minting on NEAR" };
            for (var i = 0; i < steps.Length; i++)
            {
                var done = _mintStage > i + 1 || (_mintStage == 2 && i == 0);
                var now = (_mintStage == 1 && i == 0) || (_mintStage == 2 && i >= 1);
                var row = PK.Row(col, p.P(12));
                row.style.alignItems = Align.Center;
                PK.T(row, p, done ? "✓" : now ? "…" : "·", 30, done ? GameTheme.T("#7dff6a") : Color.white);
                PK.N(row, p, steps[i], 22, now || done ? Color.white : GameTheme.T("#9fd0ff"));
            }
            PK.N(col, p, "Keep this tab open until it's done.", 16, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter).style.marginTop = p.P(8);
        }

        private void MintDone(VisualElement col, Pt p)
        {
            PK.N(col, p, _mintMsg, 20, Color.white, TextAnchor.MiddleCenter, true);
            if (_mintToken.Length > 0)
                PK.Btn(col, p, "yellow", "Sell on market", 580, 72, 28, () => GameLinks.Open(MarketToken(_mintToken, sell: true), "Sell on the market"));
            PK.Btn(col, p, "green", "Keep", 580, 64, 26, () => { CloseAll(); });
        }

        private void MintFailed(VisualElement col, Pt p)
        {
            PK.N(col, p, _mintMsg, 20, GameTheme.T("#ffd23a"), TextAnchor.MiddleCenter, true);
            PK.Btn(col, p, "blue", "Try again", 580, 72, 28, () => { _mintStage = 0; _mintMsg = ""; MintLoadInfo(); MarkDirty(); });
            PK.Btn(col, p, "gray", "Close", 580, 56, 24, Back);
        }

        private async void MintGo()
        {
            var chain = Chain;
            var a = _mint;
            if (chain == null || a == null || _chainBusy) return;
            _chainBusy = true;
            _mintStage = 1;
            MarkDirty();
            try
            {
                if (Host != null) await IslandShadow.SyncNow(Host.CurrentState());   // the server mints from its copy
                void Packed() { _mintStage = 2; MarkDirty(); }
                var r = a.Kind is "good" or "boost"
                    ? await chain.MintGoods(a.Item, _mintQty, a.Kind == "boost", CancellationToken.None, Packed)
                    : await chain.MintBuilding(a.Uid, a.Kind == "land", CancellationToken.None, Packed);
                MintResult(r, a.Kind is "good" or "boost" ? a.Name + " ×" + PK.Num(_mintQty) : a.Name);
            }
            finally
            {
                _chainBusy = false;
                MarkDirty();
            }
        }

        private async void MintFinish(PendingMint m)
        {
            var chain = Chain;
            if (chain == null || _chainBusy) return;
            _chainBusy = true;
            _mintStage = 2;
            MarkDirty();
            try
            {
                MintResult(await chain.FinishMint(m, CancellationToken.None), m.Voucher.Title);
            }
            finally
            {
                _chainBusy = false;
                MarkDirty();
            }
        }

        private void MintResult(Core.Results.Result<ChainOutcome> r, string what)
        {
            if (!r.IsOk)
            {
                _mintStage = -1;
                _mintMsg = ChainError(r.Error);
            }
            else if (r.Value.Declined)
            {
                _mintStage = -1;
                _mintMsg = "Not minted. Your items come back to your farm within the hour, or finish it from here.";
                MintLoadInfo();
            }
            else
            {
                _mintStage = 3;
                _mintToken = r.Value.TokenId;
                _mintMsg = what + " is an NFT in your wallet" + (r.Value.Fee > 0 ? " (fee " + PK.Num(r.Value.Fee) + " coins)" : "") + ".";
                if (Nfts != null) _ = Nfts.RefreshOwned();
            }
        }
    }
}
