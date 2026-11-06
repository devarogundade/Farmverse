#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// The shop beyond gems, owned by the server: boosts held and running (<c>state/inventory</c>), the
    /// free gift's timer, the starter pack's window and today's chests (<c>state/shop</c>). Both docs are
    /// kept on the device too, so Storage shows the last known boosts offline. Using a boost or claiming
    /// a gift or a chest needs a connection; each call carries its own key, so a retry never pays twice.
    /// </summary>
    public sealed class ShopService : IDisposable
    {
        private const string InventoryCache = "shop_inventory";
        private const string ShopCache = "shop_state";
        private const string GoalsCache = "shop_goals";
        private const string DailyCache = "shop_daily";
        private const string PassCache = "shop_pass";

        private readonly IDocumentStore _documents;
        private readonly ServerCalls _server;
        private readonly ILocalStore _local;
        private readonly List<IDisposable> _watches = new List<IDisposable>();
        private InventoryDoc _inventory;
        private ShopDoc? _shop;
        private GoalsDoc? _goals;
        private DailyDoc? _daily;
        private PassDoc? _pass;

        public ShopService(IDocumentStore documents, ServerCalls server, ILocalStore local)
        {
            _documents = documents;
            _server = server;
            _local = local;
            _inventory = Load(InventoryCache, InventoryDoc.FromWire) ?? new InventoryDoc();
            _shop = Load(ShopCache, ShopDoc.FromWire);
            _goals = Load(GoalsCache, GoalsDoc.FromWire);
            _daily = Load(DailyCache, DailyDoc.FromWire);
            _pass = Load(PassCache, PassDoc.FromWire);
        }

        public event Action? Changed;

        /// <summary>Signed in and listening: claims and boosts can be used.</summary>
        public bool Online { get; private set; }

        /// <summary>How many of a boost are held.</summary>
        public int Boosts(string boostId) => _inventory.Boosts.TryGetValue(boostId, out var n) ? n : 0;

        /// <summary>When a boost's effect ends (epoch ms; 0 or past = not running).</summary>
        public long ActiveUntil(string boostId) => _inventory.ActiveUntil.TryGetValue(boostId, out var t) ? t : 0;

        /// <summary>When the free gift is ready (epoch ms), or null before the shop doc is known.</summary>
        public long? NextGiftAt => _shop?.NextGiftAt;

        /// <summary>When the starter pack stops being offered (epoch ms; 0 = unknown).</summary>
        public long StarterEndsAt => _shop?.StarterEndsAt ?? 0;

        /// <summary>True when the chest was already opened on this UTC day (yyyy-MM-dd).</summary>
        public bool ChestClaimed(ChestId chest, string utcDay) =>
            _shop != null && _shop.ChestDay.TryGetValue(ChestIdWire.ToWire(chest), out var d) && d == utcDay;

        /// <summary>True once the achievement's gems were collected.</summary>
        public bool AchievementClaimed(string id) => _goals != null && _goals.Achievements.ContainsKey(id);

        /// <summary>The login ladder and today's spins, or null before the server answered.</summary>
        public DailyDoc? Daily => _daily;

        /// <summary>This season's Farm Pass for the player (null before joining / the server answered).</summary>
        public PassDoc? Pass => _pass;

        public void Attach(string uid)
        {
            foreach (var w in _watches) w.Dispose();
            _watches.Clear();
            _watches.Add(_documents.Listen(Paths.Inventory(uid), s => OnDoc(s, InventoryCache, d => _inventory = InventoryDoc.FromWire(d)), _ => { }));
            _watches.Add(_documents.Listen(Paths.Shop(uid), s => OnDoc(s, ShopCache, d => _shop = ShopDoc.FromWire(d)), _ => { }));
            _watches.Add(_documents.Listen(Paths.Pass(uid), s => OnDoc(s, PassCache, d => _pass = PassDoc.FromWire(d)), _ => { }));
            _watches.Add(_documents.Listen(Paths.Daily(uid), s => OnDoc(s, DailyCache, d => _daily = DailyDoc.FromWire(d)), _ => { }));
            _watches.Add(_documents.Listen(Paths.Goals(uid), s => OnDoc(s, GoalsCache, d => _goals = GoalsDoc.FromWire(d)), _ => { }));
            Online = true;
            Changed?.Invoke();
            _ = _server.PassJoin();   // into this season's Farm Pass (idempotent; a new season resets it)
        }

        public async Task<Result<BoostUseResponse>> UseBoost(BoostId boost, CancellationToken cancellation = default)
        {
            if (!Online) return Result<BoostUseResponse>.Fail(ErrorCode.NetworkRequired);
            return await Call(key => _server.BoostUse(boost, key, cancellation));
        }

        /// <summary>Today's login reward (the server keeps the ladder).</summary>
        public async Task<Result<DailyClaimLoginResponse>> ClaimLogin(CancellationToken cancellation = default)
        {
            if (!Online) return Result<DailyClaimLoginResponse>.Fail(ErrorCode.NetworkRequired);
            return await Call(key => _server.DailyClaimLogin(key, cancellation));
        }

        /// <summary>
        /// A Lucky Wheel spin under <paramref name="key"/> (one spin per key, the catalogue's spinsPerDay a day):
        /// the server rolls the slice and pays it (coins are the game's to add).
        /// </summary>
        public async Task<Result<WheelSpinResponse>> Spin(string key, CancellationToken cancellation = default)
        {
            if (!Online) return Result<WheelSpinResponse>.Fail(ErrorCode.NetworkRequired);
            var result = await _server.WheelSpin(key, cancellation);
            if (result.IsFailure && (result.Error.Code == ErrorCode.Timeout || result.Error.Code == ErrorCode.Internal))
                result = await _server.WheelSpin(key, cancellation);   // the same key repeats a lost answer
            return result;
        }

        /// <summary>Collect a Farm Pass tier: the island is mirrored first, the server checks its pass XP.</summary>
        public async Task<Result<PassClaimResponse>> ClaimPass(int tier, bool premium, string islandJson, CancellationToken cancellation = default)
        {
            if (!Online) return Result<PassClaimResponse>.Fail(ErrorCode.NetworkRequired);
            var mirrored = await _server.IslandSync(islandJson, mirror: true, cancellation);
            if (mirrored.IsFailure) return Result<PassClaimResponse>.Fail(mirrored.Error);
            return await Call(key => _server.PassClaim(tier, premium, key, cancellation));
        }

        public async Task<Result<ShopClaimGiftResponse>> ClaimGift(CancellationToken cancellation = default)
        {
            if (!Online) return Result<ShopClaimGiftResponse>.Fail(ErrorCode.NetworkRequired);
            return await Call(key => _server.ShopClaimGift(key, cancellation));
        }

        /// <summary>
        /// Open today's chest. The server checks today's orders / daily goals on its copy of the island, so the
        /// island (<paramref name="islandJson"/>, IslandState JSON) is mirrored to it first.
        /// </summary>
        public async Task<Result<ShopClaimChestResponse>> ClaimChest(ChestId chest, string islandJson, CancellationToken cancellation = default)
        {
            if (!Online) return Result<ShopClaimChestResponse>.Fail(ErrorCode.NetworkRequired);
            var mirrored = await _server.IslandSync(islandJson, mirror: true, cancellation);
            if (mirrored.IsFailure) return Result<ShopClaimChestResponse>.Fail(mirrored.Error);
            return await Call(key => _server.ShopClaimChest(chest, key, cancellation));
        }

        /// <summary>Collect an achievement's gems (its stat is checked on the mirrored island).</summary>
        public async Task<Result<GoalsClaimAchievementResponse>> ClaimAchievement(string id, string islandJson, CancellationToken cancellation = default)
        {
            if (!Online) return Result<GoalsClaimAchievementResponse>.Fail(ErrorCode.NetworkRequired);
            var mirrored = await _server.IslandSync(islandJson, mirror: true, cancellation);
            if (mirrored.IsFailure) return Result<GoalsClaimAchievementResponse>.Fail(mirrored.Error);
            return await Call(key => _server.GoalsClaimAchievement(id, key, cancellation));
        }

        public void Dispose()
        {
            foreach (var w in _watches) w.Dispose();
            _watches.Clear();
        }

        /// <summary>One call under a fresh key, retried once with the same key when the answer was lost.</summary>
        private static async Task<Result<T>> Call<T>(Func<string, Task<Result<T>>> call)
        {
            var key = Guid.NewGuid().ToString("N");
            var result = await call(key);
            if (result.IsFailure && (result.Error.Code == ErrorCode.Timeout || result.Error.Code == ErrorCode.NetworkRequired || result.Error.Code == ErrorCode.Internal))
                result = await call(key);
            return result;
        }

        private void OnDoc(DocumentSnapshot snapshot, string cacheKey, Action<Dictionary<string, object>> apply)
        {
            if (!snapshot.Exists || snapshot.Data == null) return;
            var data = new Dictionary<string, object>(snapshot.Data);
            try
            {
                apply(data);
            }
            catch (WireFormatException)
            {
                return;   // keep the last good copy
            }
            _local.Write(cacheKey, WireJson.Serialize(data));
            Changed?.Invoke();
        }

        private T? Load<T>(string key, Func<object?, T> parse) where T : class
        {
            var json = _local.Read(key);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return parse(WireJson.Parse(json!));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
