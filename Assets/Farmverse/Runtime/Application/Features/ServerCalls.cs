#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Remote;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// The callables the game uses directly: farm name ideas, friend invites and
    /// the island's server copy.
    /// </summary>
    public sealed class ServerCalls
    {
        private readonly CallableClient _client;
        private readonly Func<RequestMeta> _meta;

        public ServerCalls(CallableClient client, Func<RequestMeta> meta)
        {
            _client = client;
            _meta = meta;
        }

        /// <summary>Farm names nobody has taken yet.</summary>
        public async Task<Result<List<string>>> SuggestNames(int count, CancellationToken cancellation = default)
        {
            var result = await _client.Call(Callables.PlayerSuggestNames, new PlayerSuggestNamesRequest { Meta = _meta(), Count = Math.Max(1, Math.Min(10, count)) }, cancellation);
            return result.IsOk ? Result<List<string>>.Ok(result.Value.Names) : Result<List<string>>.Fail(result.Error);
        }

        public Task<Result<FriendsCreateInviteResponse>> CreateInvite(CancellationToken cancellation = default) =>
            _client.Call(Callables.FriendsCreateInvite, new FriendsCreateInviteRequest { Meta = _meta() }, cancellation);

        /// <summary>
        /// The island as the server runs it. <paramref name="adopt"/> is the device's island save (IslandState JSON):
        /// sent once to move a local island to the server, or every time in shadow mode (<paramref name="mirror"/>).
        /// </summary>
        public Task<Result<IslandSyncResponse>> IslandSync(string? adopt, bool mirror, CancellationToken cancellation = default, string? device = null) =>
            _client.Call(Callables.IslandSync, new IslandSyncRequest { Meta = _meta(), Adopt = adopt, Mirror = mirror ? true : (bool?)null, Device = device }, cancellation);

        /// <summary>Take items off the island for an NFT: returns the signed voucher to mint with (docs 13 §7).</summary>
        public Task<Result<ChainMintResponse>> ChainMint(MintKind kind, string? item, int? quantity, long? buildingUid, string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.ChainMint, new ChainMintRequest { Meta = _meta(), Kind = kind, Item = item, Quantity = quantity, BuildingUid = buildingUid, Key = key }, cancellation);

        /// <summary>Mint vouchers the wallet can still sign, today's mint allowance and the fee (docs 15 §4.1).</summary>
        public Task<Result<ChainPendingMintsResponse>> ChainPendingMints(CancellationToken cancellation = default) =>
            _client.Call(Callables.ChainPendingMints, new ChainPendingMintsRequest { Meta = _meta() }, cancellation);

        /// <summary>Today's test FARM (testnet only, docs 15 §6.6).</summary>
        public Task<Result<ChainFaucetResponse>> ChainFaucet(CancellationToken cancellation = default) =>
            _client.Call(Callables.ChainFaucet, new ChainFaucetRequest { Meta = _meta() }, cancellation);

        /// <summary>Credit the items of an NFT this wallet redeemed on chain (safe to repeat).</summary>
        public Task<Result<ChainRedeemResponse>> ChainRedeem(string txHash, CancellationToken cancellation = default) =>
            _client.Call(Callables.ChainRedeem, new ChainRedeemRequest { Meta = _meta(), TxHash = txHash }, cancellation);

        /// <summary>The game is open (start, then every minute in front): no pushes while playing; returns today's raid.</summary>
        public Task<Result<SessionPingResponse>> SessionPing(int offsetMin, bool raidAck, CancellationToken cancellation = default) =>
            _client.Call(Callables.SessionPing, new SessionPingRequest { Meta = _meta(), OffsetMin = offsetMin, RaidAck = raidAck ? true : (bool?)null }, cancellation);

        /// <summary>The game ran (or skipped) today's raid with the player there.</summary>
        public Task<Result<RaidFinishResponse>> RaidFinish(string day, RaidOutcomeKind outcome, int size, CancellationToken cancellation = default) =>
            _client.Call(Callables.RaidFinish, new RaidFinishRequest { Meta = _meta(), Day = day, Outcome = outcome, Size = size }, cancellation);

        /// <summary>Delete the account and everything stored about it (store requirement).</summary>
        public Task<Result<AccountDeleteResponse>> AccountDelete(CancellationToken cancellation = default) =>
            _client.Call(Callables.AccountDelete, new AccountDeleteRequest { Meta = _meta() }, cancellation);

        /// <summary>The referral program: this (invited) player's island reached the referral level.</summary>
        public Task<Result<ReferralClaimResponse>> ReferralClaim(CancellationToken cancellation = default) =>
            _client.Call(Callables.ReferralClaim, new ReferralClaimRequest { Meta = _meta() }, cancellation);

        /// <summary>Collect a reward the server gave; the answer says what to add to the island.</summary>
        public Task<Result<RewardsClaimResponse>> RewardsClaim(string id, CancellationToken cancellation = default) =>
            _client.Call(Callables.RewardsClaim, new RewardsClaimRequest { Meta = _meta(), Id = id }, cancellation);

        /// <summary>Buy a catalogue offer with gems; the same <paramref name="key"/> never charges twice.</summary>
        public Task<Result<WalletSpendResponse>> WalletSpend(string offerId, string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.WalletSpend, new WalletSpendRequest { Meta = _meta(), OfferId = offerId, Key = key }, cancellation);

        /// <summary>Gems earned in the game (capped per source per day on the server); the same key never pays twice.</summary>
        public Task<Result<WalletEarnResponse>> WalletEarn(GemSource source, int amount, string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.WalletEarn, new WalletEarnRequest { Meta = _meta(), Source = source, Amount = amount, Key = key }, cancellation);

        /// <summary>Use one boost from Storage.</summary>
        public Task<Result<BoostUseResponse>> BoostUse(BoostId boost, string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.BoostUse, new BoostUseRequest { Meta = _meta(), BoostId = boost, Key = key }, cancellation);

        /// <summary>Collect the shop's free coin gift.</summary>
        public Task<Result<ShopClaimGiftResponse>> ShopClaimGift(string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.ShopClaimGift, new ShopClaimGiftRequest { Meta = _meta(), Key = key }, cancellation);

        /// <summary>Open today's order or task chest.</summary>
        public Task<Result<ShopClaimChestResponse>> ShopClaimChest(ChestId chest, string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.ShopClaimChest, new ShopClaimChestRequest { Meta = _meta(), Chest = chest, Key = key }, cancellation);

        /// <summary>Collect an achievement's gems (checked on the server's island copy).</summary>
        public Task<Result<GoalsClaimAchievementResponse>> GoalsClaimAchievement(string id, string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.GoalsClaimAchievement, new GoalsClaimAchievementRequest { Meta = _meta(), Id = id, Key = key }, cancellation);

        /// <summary>Collect today's login reward.</summary>
        public Task<Result<DailyClaimLoginResponse>> DailyClaimLogin(string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.DailyClaimLogin, new DailyClaimLoginRequest { Meta = _meta(), Key = key }, cancellation);

        /// <summary>Spin the Lucky Wheel (the server rolls it); the same <paramref name="key"/> never spins twice.</summary>
        public Task<Result<WheelSpinResponse>> WheelSpin(string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.WheelSpin, new WheelSpinRequest { Meta = _meta(), Key = key }, cancellation);

        /// <summary>Join this season's Farm Pass (idempotent).</summary>
        public Task<Result<PassJoinResponse>> PassJoin(CancellationToken cancellation = default) =>
            _client.Call(Callables.PassJoin, new PassJoinRequest { Meta = _meta() }, cancellation);

        /// <summary>Collect a Farm Pass tier (checked on the server's island copy).</summary>
        public Task<Result<PassClaimResponse>> PassClaim(int tier, bool premium, string key, CancellationToken cancellation = default) =>
            _client.Call(Callables.PassClaim, new PassClaimRequest { Meta = _meta(), Tier = tier, Premium = premium, Key = key }, cancellation);

        /// <summary>A coupon code for a finished task (checked on the server's island copy).</summary>
        public Task<Result<CouponsClaimResponse>> CouponsClaim(string taskId, CancellationToken cancellation = default) =>
            _client.Call(Callables.CouponsClaim, new CouponsClaimRequest { Meta = _meta(), TaskId = taskId }, cancellation);

        /// <summary>Change the public profile (name, avatar, language); only the fields set are sent.</summary>
        public Task<Result<Empty>> UpdateProfile(PlayerUpdateProfileRequest change, CancellationToken cancellation = default)
        {
            change.Meta = _meta();
            return _client.Call(Callables.PlayerUpdateProfile, change, cancellation);
        }

        /// <summary>Another player's island to look at.</summary>
        public Task<Result<VisitsGetResponse>> VisitsGet(string uid, CancellationToken cancellation = default) =>
            _client.Call(Callables.VisitsGet, new VisitsGetRequest { Meta = _meta(), Uid = uid }, cancellation);

        /// <summary>Like an island (once a day).</summary>
        public Task<Result<VisitsLikeResponse>> VisitsLike(string uid, CancellationToken cancellation = default) =>
            _client.Call(Callables.VisitsLike, new VisitsLikeRequest { Meta = _meta(), Uid = uid }, cancellation);

        /// <summary>Delete inbox messages (not ones with a reward still to collect).</summary>
        public Task<Result<InboxDeleteResponse>> InboxDelete(IReadOnlyList<string> ids, CancellationToken cancellation = default) =>
            _client.Call(Callables.InboxDelete, new InboxDeleteRequest { Meta = _meta(), Ids = new List<string>(ids) }, cancellation);

        public Task<Result<FriendsAcceptResponse>> AcceptInvite(string code, CancellationToken cancellation = default) =>
            _client.Call(Callables.FriendsAccept, new FriendsAcceptRequest { Meta = _meta(), Code = code }, cancellation);
    }
}
