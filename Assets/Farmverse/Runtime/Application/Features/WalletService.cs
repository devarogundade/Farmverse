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
    /// Gems, owned by the server (<c>players/{uid}/state/wallet</c>, written only by functions).
    ///
    /// Spending is online only (<c>wallet-spend</c>: the server checks the price and the balance).
    /// Earning works offline: each earning is queued on the device under its own key and sent
    /// (<c>wallet-earn</c>) as soon as the player is signed in; a retry with the same key never pays
    /// twice, and the server caps each source per day. The balance shown is the server's plus the
    /// earnings still on their way, minus purchases in flight.
    /// </summary>
    public sealed class WalletService : IDisposable
    {
        private const string BalanceKey = "wallet_gems";
        private const string PendingKey = "wallet_pending";

        private readonly IDocumentStore _documents;
        private readonly ServerCalls _server;
        private readonly ILocalStore _local;
        private readonly CatalogueService _catalogue;
        private readonly List<PendingEarn> _pending = new List<PendingEarn>();
        private IDisposable? _watch;
        private int _balance;
        private string _boughtOn = "";
        private Dictionary<string, long> _bought = new Dictionary<string, long>();
        private int _reserved;
        private bool _flushing;

        public WalletService(IDocumentStore documents, ServerCalls server, ILocalStore local, CatalogueService catalogue)
        {
            _documents = documents;
            _server = server;
            _local = local;
            _catalogue = catalogue;
            _balance = int.TryParse(_local.Read(BalanceKey), out var b) ? b : catalogue.Current.StartingGems;
            LoadPending();
        }

        /// <summary>Raised whenever <see cref="Gems"/> may have changed.</summary>
        public event Action? Changed;

        /// <summary>True once signed in and listening: purchases are possible.</summary>
        public bool Online { get; private set; }

        /// <summary>The gems to show.</summary>
        public int Gems
        {
            get
            {
                var n = _balance - _reserved;
                foreach (var p in _pending) n += p.Amount;
                return Math.Max(0, n);
            }
        }

        public void Attach(string uid)
        {
            _watch?.Dispose();
            _watch = _documents.Listen(Paths.Wallet(uid), OnWallet, _ => { });
            Online = true;
            _ = Flush();
        }

        /// <summary>Gems earned in the game (a chest, an achievement…): shown at once, confirmed by the server.</summary>
        public void Earn(GemSource source, int amount)
        {
            if (amount <= 0) return;
            _pending.Add(new PendingEarn(NewKey(), source, Math.Min(amount, 1000)));
            SavePending();
            Changed?.Invoke();
            _ = Flush();
        }

        /// <summary>
        /// Buy a catalogue offer. Ok means the gems are gone and the answer says what to add (coins,
        /// boosts); a <c>Fresh == false</c> answer means it was already added.
        /// </summary>
        public async Task<Result<WalletSpendResponse>> Spend(string offerId, CancellationToken cancellation = default)
        {
            if (!Online) return Result<WalletSpendResponse>.Fail(ErrorCode.NetworkRequired);
            var price = _catalogue.OfferGems(offerId);
            if (price <= 0) return Result<WalletSpendResponse>.Fail(ErrorCode.OfferUnknown);
            if (price > Gems) return Result<WalletSpendResponse>.Fail(AppError.Of(ErrorCode.NotEnoughGems, ("need", (price - Gems).ToString())));
            _reserved += price;
            Changed?.Invoke();
            try
            {
                var key = NewKey();
                var result = await _server.WalletSpend(offerId, key, cancellation);
                // a lost answer: the same key either completes the purchase or repeats its answer
                if (result.IsFailure && IsTransient(result.Error.Code)) result = await _server.WalletSpend(offerId, key, cancellation);
                if (result.IsOk) SetBalance((int)result.Value.Gems);
                return result;
            }
            finally
            {
                _reserved -= price;
                Changed?.Invoke();
            }
        }

        /// <summary>How many of a daily-limited offer were bought on <paramref name="utcDay"/> (yyyy-MM-dd), as the server counts.</summary>
        public long BoughtOn(string offerId, string utcDay) =>
            _boughtOn == utcDay && _bought.TryGetValue(offerId, out var n) ? n : 0;

        public void Dispose() => _watch?.Dispose();

        private void OnWallet(DocumentSnapshot snapshot)
        {
            if (!snapshot.Exists || snapshot.Data == null)
            {
                // a player made before wallets existed: the server opens it with the starting gems
                SetBalance(_catalogue.Current.StartingGems);
                return;
            }
            try
            {
                var doc = WalletDoc.FromWire(new Dictionary<string, object>(snapshot.Data));
                _boughtOn = doc.BoughtOn ?? "";
                _bought = doc.Bought != null ? new Dictionary<string, long>(doc.Bought) : new Dictionary<string, long>();
                SetBalance(doc.Gems);
                Changed?.Invoke();
            }
            catch (WireFormatException)
            {
                // keep the last good balance
            }
        }

        private void SetBalance(int gems)
        {
            if (gems == _balance) return;
            _balance = gems;
            _local.Write(BalanceKey, gems.ToString());
            Changed?.Invoke();
        }

        private async Task Flush()
        {
            if (!Online || _flushing) return;
            _flushing = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var next = _pending[0];
                    var result = await _server.WalletEarn(next.Source, next.Amount, next.Key);
                    if (result.IsFailure && IsTransient(result.Error.Code)) return;   // try again next time
                    _pending.RemoveAt(0);   // paid (perhaps capped), or refused for good
                    SavePending();
                    if (result.IsOk) SetBalance((int)result.Value.Gems);
                    Changed?.Invoke();
                }
            }
            finally
            {
                _flushing = false;
            }
        }

        private static bool IsTransient(ErrorCode code) =>
            code == ErrorCode.NetworkRequired || code == ErrorCode.Timeout || code == ErrorCode.Internal
            || code == ErrorCode.RateLimited || code == ErrorCode.Conflict || code == ErrorCode.Unauthenticated;

        private static string NewKey() => Guid.NewGuid().ToString("N");

        private void LoadPending()
        {
            var json = _local.Read(PendingKey);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                if (!(WireJson.Parse(json!) is List<object> rows)) return;
                foreach (var row in rows)
                {
                    if (!(row is Dictionary<string, object> m)) continue;
                    var key = m.TryGetValue("k", out var k) ? k as string : null;
                    var source = m.TryGetValue("s", out var s) && s is string ss ? GemSourceWire.FromWire(ss) : GemSource.Unknown;
                    var amount = m.TryGetValue("a", out var a) && a is long al ? (int)al : 0;
                    if (key != null && source != GemSource.Unknown && amount > 0) _pending.Add(new PendingEarn(key, source, amount));
                }
            }
            catch (Exception)
            {
                // a broken queue is dropped rather than blocking the wallet
            }
        }

        private void SavePending()
        {
            var rows = new List<object>(_pending.Count);
            foreach (var p in _pending)
                rows.Add(new Dictionary<string, object> { ["k"] = p.Key, ["s"] = GemSourceWire.ToWire(p.Source), ["a"] = (long)p.Amount });
            _local.Write(PendingKey, WireJson.Serialize(rows));
        }

        private readonly struct PendingEarn
        {
            public PendingEarn(string key, GemSource source, int amount)
            {
                Key = key;
                Source = source;
                Amount = amount;
            }

            public string Key { get; }
            public GemSource Source { get; }
            public int Amount { get; }
        }
    }
}
