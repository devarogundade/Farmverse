#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>
    /// Unity IAP v5 (Google Play, the App Store; the Editor's fake store). Purchases arrive as pending
    /// orders and stay pending until <see cref="Finish"/> — the server has paid them by then.
    /// </summary>
    public sealed class UnityAppStore : IAppStore
    {
        private StoreController? _store;
        private IReadOnlyDictionary<string, bool> _products = new Dictionary<string, bool>();

        public bool Ready { get; private set; }

        public event Action<StoreOrder>? Purchased;
        public event Action<string, bool>? Failed;
        public event Action? Changed;

        public async Task Connect(IReadOnlyDictionary<string, bool> products)
        {
            _products = products;
            if (_store == null)
            {
                _store = UnityIAPServices.StoreController();
                _store.OnStoreConnected += Fetch;
                _store.OnStoreDisconnected += d => Debug.LogWarning("[Store] disconnected: " + d.Message);
                _store.OnProductsFetched += _ =>
                {
                    Ready = true;
                    Changed?.Invoke();
                    _store.FetchPurchases();   // unfinished purchases of earlier runs come back as pending orders
                };
                _store.OnProductsFetchFailed += f => Debug.LogWarning("[Store] products failed: " + f.FailureReason);
                _store.OnPurchasesFetched += _ => { };
                _store.OnPurchasesFetchFailed += f => Debug.LogWarning("[Store] purchases failed: " + f.Message);
                _store.OnPurchasePending += OnPending;
                _store.OnPurchaseConfirmed += _ => { };
                _store.OnPurchaseFailed += OnFailed;
                _store.OnPurchaseDeferred += _ => { };   // Ask to Buy / pending payment: it comes back as pending later
                _store.OnAuthAccountChanged += Fetch;
            }
            try
            {
                await _store.Connect();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Store] connect failed: " + e.Message);
            }
        }

        private void Fetch()
        {
            if (_store == null) return;
            var defs = new List<ProductDefinition>();
            foreach (var kv in _products) defs.Add(new ProductDefinition(kv.Key, kv.Value ? ProductType.Consumable : ProductType.NonConsumable));
            _store.FetchProducts(defs);
        }

        public string? PriceOf(string productId)
        {
#if UNITY_EDITOR
            return null;   // the fake store's prices are placeholders: show the catalogue's
#else
            var p = _store?.GetProductById(productId);
            return p?.metadata?.localizedPriceString;
#endif
        }

        public void Buy(string productId)
        {
            if (_store == null || !Ready)
            {
                Failed?.Invoke(productId, false);
                return;
            }
#if UNITY_EDITOR
            // the Editor's fake store asks with a uGUI dialog: it needs an EventSystem on the Input System
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                var go = new GameObject("EventSystem (store)");
                go.AddComponent<UnityEngine.EventSystems.EventSystem>();
                go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                UnityEngine.Object.DontDestroyOnLoad(go);
            }
#endif
            _store.PurchaseProduct(productId);
        }

        public void Restore(Action<bool> done)
        {
            if (_store == null)
            {
                done(false);
                return;
            }
            _store.RestoreTransactions((ok, _) => done(ok));
        }

        public void Finish(StoreOrder order)
        {
            if (_store != null && order.Handle is PendingOrder pending) _store.ConfirmPurchase(pending);
        }

        private void OnPending(PendingOrder order)
        {
            var info = order.Info;
            foreach (var item in order.CartOrdered.Items())
            {
                var id = item.Product.definition.id;
                if (info.Google != null)
                    Purchased?.Invoke(new StoreOrder(id, IapStore.Google, info.Google.OrderId ?? info.TransactionID ?? "", info.Google.PurchaseToken ?? "", order));
                else if (info.Apple != null && !string.IsNullOrEmpty(info.Apple.jwsRepresentation))
                    Purchased?.Invoke(new StoreOrder(id, IapStore.Apple, info.TransactionID ?? "", info.Apple.jwsRepresentation, order));
                else
                    // the Editor's fake store (the server accepts these only on the local emulators)
                    Purchased?.Invoke(new StoreOrder(id, IapStore.Fake, string.IsNullOrEmpty(info.TransactionID) ? Guid.NewGuid().ToString("N") : info.TransactionID, info.Receipt ?? "fake", order));
            }
        }

        private void OnFailed(FailedOrder order)
        {
            var cancelled = order.FailureReason == PurchaseFailureReason.UserCancelled;
            foreach (var item in order.CartOrdered.Items()) Failed?.Invoke(item.Product.definition.id, cancelled);
        }
    }
}
