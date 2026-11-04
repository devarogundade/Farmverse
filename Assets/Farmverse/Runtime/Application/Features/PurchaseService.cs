#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// Real-money purchases. The store sells the catalogue's <c>iap</c> products; every purchase is checked by
    /// the server (<c>iap-verify</c>), which pays it once, and only then finished with the store — so a purchase
    /// interrupted anywhere (crash, no network) is delivered on a later launch. What purchases unlocked for good
    /// (No Ads, extra builders) is the server's <c>state/purchases</c>.
    /// </summary>
    public sealed class PurchaseService : IDisposable
    {
        private readonly IAppStore _store;
        private readonly ServerCalls _server;
        private readonly CatalogueService _catalogue;
        private readonly IDocumentStore _documents;
        private readonly HashSet<string> _delivering = new HashSet<string>();
        private IDisposable? _watch;
        private bool _connecting;
        private string? _buying;

        public PurchaseService(IAppStore store, ServerCalls server, CatalogueService catalogue, IDocumentStore documents)
        {
            _store = store;
            _server = server;
            _catalogue = catalogue;
            _documents = documents;
            _store.Purchased += order => _ = Deliver(order);
            _store.Failed += (id, cancelled) =>
            {
                if (id == _buying) _buying = null;
                Finished?.Invoke(id, cancelled ? Result<IapVerifyResponse>.Fail(ErrorCode.Cancelled) : Result<IapVerifyResponse>.Fail(ErrorCode.IapInvalid));
            };
            _store.Changed += () => Changed?.Invoke();
        }

        /// <summary>Prices, ownership or the unlocks changed.</summary>
        public event Action? Changed;

        /// <summary>A purchase was delivered (Ok: what it paid; coins are the game's to add) or failed.</summary>
        public event Action<string, Result<IapVerifyResponse>>? Finished;

        public bool Online { get; private set; }

        public bool StoreReady => _store.Ready;

        /// <summary>A purchase is on its way (the buttons wait).</summary>
        public bool Busy => _buying != null;

        public PurchasesDoc Unlocked { get; private set; } = new PurchasesDoc();

        public async void Attach(string uid)
        {
            _watch?.Dispose();
            _watch = _documents.Listen(Paths.Purchases(uid), s =>
            {
                if (!s.Exists || s.Data == null) return;
                try { Unlocked = PurchasesDoc.FromWire(new Dictionary<string, object>(s.Data)); }
                catch (WireFormatException) { return; }
                Changed?.Invoke();
            }, _ => { });
            Online = true;
            if (_connecting || _store.Ready) return;
            _connecting = true;
            try
            {
                var products = _catalogue.Current.Iap.ToDictionary(kv => kv.Key, kv => kv.Value.Kind != IapKind.Noads);
                await _store.Connect(products);
            }
            finally
            {
                _connecting = false;
                Changed?.Invoke();
            }
        }

        /// <summary>The price to show: the store's own, else the catalogue's US$ one.</summary>
        public string PriceOf(string productId)
        {
            var store = _store.PriceOf(productId);
            if (!string.IsNullOrEmpty(store)) return store!;
            return _catalogue.Current.Iap.TryGetValue(productId, out var p) ? "US$ " + p.Usd.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        /// <summary>Start buying a catalogue product. False when it can't start (offline, store not ready, another purchase on its way).</summary>
        public bool Buy(string productId)
        {
            if (!Online || !_store.Ready || _buying != null || !_catalogue.Current.Iap.ContainsKey(productId)) return false;
            _buying = productId;
            Changed?.Invoke();
            _store.Buy(productId);
            return true;
        }

        public void Restore(Action<bool> done) => _store.Restore(done);

        private async Task Deliver(StoreOrder order)
        {
            var key = order.Store + ":" + order.TransactionId;
            if (!_delivering.Add(key)) return;
            try
            {
                var result = await _server.IapVerify(order.Store, order.ProductId, order.TransactionId, order.Receipt);
                for (var i = 0; i < 3 && result.IsFailure && Transient(result.Error.Code); i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 << i));
                    result = await _server.IapVerify(order.Store, order.ProductId, order.TransactionId, order.Receipt);
                }
                // finished with the store once the server answered for good: paid, paid before, or not a real purchase
                if (result.IsOk && !result.Value.Pending) _store.Finish(order);
                else if (result.IsFailure && (result.Error.Code == ErrorCode.IapInvalid || result.Error.Code == ErrorCode.IapUnknown)) _store.Finish(order);
                if (order.ProductId == _buying) _buying = null;
                Finished?.Invoke(order.ProductId, result);
                Changed?.Invoke();
            }
            finally
            {
                _delivering.Remove(key);
            }
        }

        private static bool Transient(ErrorCode code) =>
            code == ErrorCode.NetworkRequired || code == ErrorCode.Timeout || code == ErrorCode.Internal || code == ErrorCode.Unauthenticated;

        public void Dispose() => _watch?.Dispose();
    }
}
