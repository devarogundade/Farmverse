#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>What a mint or redeem did, for the UI.</summary>
    public sealed class ChainOutcome
    {
        public string TokenId = string.Empty;
        /// <summary>The wallet transaction, when one was sent.</summary>
        public string? TxHash;
        /// <summary>The player declined in the wallet (mint: the items come back to the island when the voucher expires).</summary>
        public bool Declined;
        /// <summary>The voucher is valid until (mint), epoch ms.</summary>
        public long ExpiresAtMs;
        /// <summary>Coins the mint fee took.</summary>
        public long Fee;
    }

    /// <summary>
    /// Island items ⇄ NFTs (docs 13 §7). <b>Mint</b>: the server takes the items and signs a voucher, the
    /// wallet mints with it. <b>Redeem</b>: the wallet burns the token, the server credits the items.
    /// Either way the server changed the island, so the island is then loaded from the server.
    /// </summary>
    public sealed class ChainService
    {
        /// <summary>Storage the mint pays for (the contract refunds what it doesn't use).</summary>
        public const string MintDepositYocto = "50000000000000000000000"; // 0.05 NEAR
        public const string OneYocto = "1";
        public const int Gas = 100;

        private readonly ServerCalls _server;
        private readonly IWallet _wallet;
        private readonly Action<string> _loadIsland;
        private readonly Func<string> _itemsContract;

        /// <param name="loadIsland">Takes the server's island JSON in place of the device's (Presentation).</param>
        /// <param name="itemsContract">The items contract id (from the server's config).</param>
        public ChainService(ServerCalls server, IWallet wallet, Action<string> loadIsland, Func<string> itemsContract)
        {
            _server = server;
            _wallet = wallet;
            _loadIsland = loadIsland;
            _itemsContract = itemsContract;
        }

        /// <summary>Minting and redeeming work: a wallet on the page and a chain on the server.</summary>
        public bool Available => _wallet.Available && !string.IsNullOrEmpty(_itemsContract());

        /// <summary>The FARM token contract (drops are paid in it); empty when the server doesn't say.</summary>
        public Func<string> FarmContract { get; set; } = () => "";

        /// <summary>Mints goods (or boosts) from the island as one NFT. <paramref name="packed"/>: the server took them (the wallet is next).</summary>
        public Task<Result<ChainOutcome>> MintGoods(string item, int quantity, bool boost, CancellationToken cancellation, Action? packed = null) =>
            Mint(boost ? MintKind.Boost : MintKind.Good, item, quantity, null, cancellation, packed);

        /// <summary>Picks up a placed building (or a field: <paramref name="land"/>) as one NFT.</summary>
        public Task<Result<ChainOutcome>> MintBuilding(int buildingUid, bool land, CancellationToken cancellation, Action? packed = null) =>
            Mint(land ? MintKind.Land : MintKind.Building, null, null, buildingUid, cancellation, packed);

        /// <summary>Vouchers not signed yet, today's allowance and the fee.</summary>
        public Task<Result<ChainPendingMintsResponse>> PendingMints(CancellationToken cancellation) => _server.ChainPendingMints(cancellation);

        /// <summary>Signs a voucher the player didn't finish (closed the wallet, lost the tab).</summary>
        public async Task<Result<ChainOutcome>> FinishMint(PendingMint m, CancellationToken cancellation)
        {
            if (!_wallet.Available) return Result<ChainOutcome>.Fail(ErrorCode.Unsupported);
            var outcome = new ChainOutcome { TokenId = m.TokenId, ExpiresAtMs = m.ExpiresAtMs };
            try
            {
                outcome.TxHash = await _wallet.Call(m.Voucher.ContractId, "mint_with_voucher", new Dictionary<string, object>
                {
                    ["voucher"] = m.Voucher.ToWire(),
                    ["signer"] = m.Signer,
                    ["signature"] = m.Signature,
                }, MintDepositYocto, Gas);
            }
            catch (WalletException e)
            {
                outcome.Declined = e.Declined;
                if (!e.Declined) return Result<ChainOutcome>.Fail(AppError.FromException(ErrorCode.Internal, e));
            }
            return Result<ChainOutcome>.Ok(outcome);
        }

        /// <summary>Today's test FARM (and test NEAR the first time).</summary>
        public Task<Result<ChainFaucetResponse>> ClaimFaucet(CancellationToken cancellation) => _server.ChainFaucet(cancellation);

        /// <summary>
        /// Buys one edition of a studio drop with FARM (docs 15 §5): one wallet approval. With <paramref name="use"/>
        /// the item is used at once (the Farm Pass from the pass screen) and the server credits it straight away.
        /// </summary>
        public async Task<Result<ChainOutcome>> BuyDrop(string dropId, string priceYocto, bool use, CancellationToken cancellation)
        {
            if (!_wallet.Available) return Result<ChainOutcome>.Fail(ErrorCode.Unsupported);
            var farm = FarmContract();
            if (string.IsNullOrEmpty(farm)) return Result<ChainOutcome>.Fail(ErrorCode.Unsupported);
            string txHash;
            try
            {
                txHash = await _wallet.Call(farm, "ft_transfer_call", new Dictionary<string, object>
                {
                    ["receiver_id"] = _itemsContract(),
                    ["amount"] = priceYocto,
                    ["msg"] = WireJson.Serialize(new Dictionary<string, object> { ["drop"] = dropId, ["use"] = use }),
                }, OneYocto, 300);
            }
            catch (WalletException e)
            {
                if (e.Declined) return Result<ChainOutcome>.Ok(new ChainOutcome { Declined = true });
                return Result<ChainOutcome>.Fail(AppError.FromException(ErrorCode.Internal, e));
            }
            if (!use) return Result<ChainOutcome>.Ok(new ChainOutcome { TxHash = txHash });
            var credited = await _server.ChainRedeem(txHash, cancellation);
            if (!credited.IsOk) return Result<ChainOutcome>.Fail(credited.Error);
            if (credited.Value.State != null) _loadIsland(credited.Value.State);
            return Result<ChainOutcome>.Ok(new ChainOutcome { TokenId = credited.Value.TokenId, TxHash = txHash });
        }

        private async Task<Result<ChainOutcome>> Mint(MintKind kind, string? item, int? quantity, long? buildingUid, CancellationToken cancellation, Action? packed)
        {
            if (!_wallet.Available) return Result<ChainOutcome>.Fail(ErrorCode.Unsupported);
            var minted = await _server.ChainMint(kind, item, quantity, buildingUid, "m-" + Guid.NewGuid().ToString("N"), cancellation);
            if (!minted.IsOk) return Result<ChainOutcome>.Fail(minted.Error);
            var r = minted.Value;
            packed?.Invoke();
            var outcome = new ChainOutcome { TokenId = r.TokenId, ExpiresAtMs = r.ExpiresAtMs, Fee = r.Fee ?? 0 };
            try
            {
                outcome.TxHash = await _wallet.Call(r.Voucher.ContractId, "mint_with_voucher", new Dictionary<string, object>
                {
                    ["voucher"] = r.Voucher.ToWire(),
                    ["signer"] = r.Signer,
                    ["signature"] = r.Signature,
                }, MintDepositYocto, Gas);
            }
            catch (WalletException e)
            {
                outcome.Declined = e.Declined;
                if (!e.Declined) return Result<ChainOutcome>.Fail(AppError.FromException(ErrorCode.Internal, e));
            }
            // the items left the island on the server whatever the wallet did: take the server's island
            if (r.State != null) _loadIsland(r.State);
            return Result<ChainOutcome>.Ok(outcome);
        }

        /// <summary>Burns an NFT the player holds and credits its items to the island.</summary>
        public async Task<Result<ChainOutcome>> Redeem(string tokenId, CancellationToken cancellation)
        {
            if (!_wallet.Available) return Result<ChainOutcome>.Fail(ErrorCode.Unsupported);
            string txHash;
            try
            {
                txHash = await _wallet.Call(_itemsContract(), "redeem", new Dictionary<string, object> { ["token_id"] = tokenId }, OneYocto, Gas);
            }
            catch (WalletException e)
            {
                if (e.Declined) return Result<ChainOutcome>.Ok(new ChainOutcome { TokenId = tokenId, Declined = true });
                return Result<ChainOutcome>.Fail(AppError.FromException(ErrorCode.Internal, e));
            }
            var credited = await _server.ChainRedeem(txHash, cancellation);
            if (!credited.IsOk) return Result<ChainOutcome>.Fail(credited.Error);
            if (credited.Value.State != null) _loadIsland(credited.Value.State);
            return Result<ChainOutcome>.Ok(new ChainOutcome { TokenId = tokenId, TxHash = txHash });
        }
    }
}
