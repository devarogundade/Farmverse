#nullable enable
using System.Threading;
using Farmverse.Application.Features;
using UnityEngine;
using UnityEngine.UIElements;

namespace Farmverse.Presentation.UI.Game
{
    // Get FARM (docs 15 §3.3, §6.6): what the wallet holds, the testnet faucet, and the ways FARM is earned
    // (sell what you grow on the market, rent out Farmhands). FARM is the market's money; coins stay on the farm.
    public sealed partial class GameUi
    {
        private static readonly bool RegGetFarm = Register("getfarm", UiLayer.Popup, (ui, c) => ui.GetFarmSheet(c), .5f);

        private bool _faucetBusy;
        private string _faucetMsg = "";

        private void GetFarmSheet(Ctx c)
        {
            var p = c.P;
            var n = Nfts;
            var pop = PK.Popup(c.Root, p, 620, 0, "FARM", Back);
            var col = PK.Col(pop, p.P(12));
            col.style.alignItems = Align.Stretch;
            PK.Pad(col, p.P(8), p.P(12), p.P(8), p.P(8));
            c.Watch(() => (n?.FarmYocto ?? "") + "|" + (n?.NearYocto ?? "") + "|" + _faucetBusy + _faucetMsg);
            if (n == null || !ChainReady)
            {
                PK.N(col, p, "FARM needs the web version with a NEAR wallet.", 20, Color.white, TextAnchor.MiddleCenter, true);
                return;
            }
            n.RefreshIfStale();

            var row = PK.Row(col, p.P(12));
            row.style.alignItems = Align.Center;
            PK.Img(row, "Icon_Clover", p.P(70));
            var bal = PK.Col(row);
            PK.T(bal, p, n.FarmYocto == null ? "…" : NftService.Farm(n.FarmYocto) + " FARM", 34);
            PK.N(bal, p, "The market's money. Buy, sell, rent and shop with it.", 17, GameTheme.T("#9fd0ff"), TextAnchor.MiddleLeft, true);

            if (FaucetOpen())
            {
                PK.Btn(col, p, _faucetBusy ? "gray" : "green", _faucetBusy ? "Sending…" : "Get 100 test FARM", 560, 76, 28, () => { if (!_faucetBusy) Faucet(); });
                PK.N(col, p, "Once a day while the game is on testnet.", 16, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter);
            }
            if (_faucetMsg.Length > 0) PK.N(col, p, _faucetMsg, 18, GameTheme.T("#ffd23a"), TextAnchor.MiddleCenter, true);

            PK.T(col, p, "Earn FARM", 24).style.marginTop = p.P(10);
            PK.Btn(col, p, "purple", "Mint goods and sell them", 560, 60, 22, () => { _shopCat = "all"; Go("storage"); });
            PK.Btn(col, p, "yellow", "Sell or rent on the market", 560, 60, 22, () => GameLinks.Open(GameLinks.Market, "Open the market"));
            if (n.NearYocto != null)
                PK.N(col, p, "Fees are on us: you don't need NEAR to mint or redeem.", 15, GameTheme.T("#9fd0ff"), TextAnchor.MiddleCenter, true).style.marginTop = p.P(6);
        }

        private async void Faucet()
        {
            var chain = Chain;
            if (chain == null) return;
            _faucetBusy = true;
            _faucetMsg = "";
            MarkDirty();
            try
            {
                var r = await chain.ClaimFaucet(CancellationToken.None);
                if (r.IsOk)
                {
                    Say(r.Value.Farm + " FARM sent" + (r.Value.Near != "0" ? " (and " + r.Value.Near + " NEAR)" : ""), 3f);
                    if (Nfts != null) await Nfts.RefreshBalances();
                }
                else if (r.Error.Code == Farmverse.Contracts.ErrorCode.FaucetWait)
                {
                    var next = r.Error.Params.TryGetValue("nextAt", out var v) && long.TryParse(v, out var at) ? at : 0;
                    var hours = next > 0 ? Mathf.Max(1, Mathf.CeilToInt((next - System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 3_600_000f)) : 24;
                    _faucetMsg = "Today's FARM is claimed. Come back in about " + hours + " h.";
                }
                else _faucetMsg = "The faucet is closed right now.";
            }
            finally
            {
                _faucetBusy = false;
                MarkDirty();
            }
        }
    }
}
