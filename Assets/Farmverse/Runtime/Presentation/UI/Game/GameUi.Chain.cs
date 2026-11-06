#nullable enable
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

        /// <summary>"New farms can mint after …" in days when it's whole days, else hours.</summary>
        internal static string MintTooSoonText(long hours)
        {
            if (hours <= 0) return "This farm can't mint yet. Try again in a moment.";
            if (hours == 24) return "New farms can mint after their first day.";
            if (hours % 24 == 0) return $"New farms can mint after {hours / 24} days.";
            return hours == 1 ? "New farms can mint after their first hour." : $"New farms can mint after {hours} hours.";
        }

        /// <summary>A mint refusal in the player's words.</summary>
        private static string ChainError(AppError e)
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
                case ErrorCode.Unsupported: return "Minting needs the web version with a NEAR wallet.";
                case ErrorCode.NetworkRequired: return "You're offline. Try again when connected.";
                default: return "Couldn't mint right now. Please try again.";
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
