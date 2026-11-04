#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Farmverse.Contracts;

namespace Farmverse.Application.Ports
{
    /// <summary>A store purchase waiting for the server's check (Google: token; Apple: signed transaction).</summary>
    public sealed class StoreOrder
    {
        public StoreOrder(string productId, IapStore store, string transactionId, string receipt, object handle)
        {
            ProductId = productId;
            Store = store;
            TransactionId = transactionId;
            Receipt = receipt;
            Handle = handle;
        }

        public string ProductId { get; }
        public IapStore Store { get; }
        public string TransactionId { get; }
        public string Receipt { get; }

        /// <summary>The store's own order object (handed back to <see cref="IAppStore.Finish"/>).</summary>
        public object Handle { get; }
    }

    /// <summary>The platform's app store (Google Play, the App Store, the Editor's fake store).</summary>
    public interface IAppStore
    {
        /// <summary>Connected and the products are loaded.</summary>
        bool Ready { get; }

        /// <summary>Connect and load the products: id → consumable (true) or bought once (false).</summary>
        Task Connect(IReadOnlyDictionary<string, bool> products);

        /// <summary>The store's localised price ("₦1,500.00"), or null before it loaded.</summary>
        string? PriceOf(string productId);

        /// <summary>Start a purchase. The result arrives through <see cref="Purchased"/> or <see cref="Failed"/>.</summary>
        void Buy(string productId);

        /// <summary>Ask the store for past purchases again (iOS "Restore purchases").</summary>
        void Restore(Action<bool> done);

        /// <summary>Tell the store the purchase was delivered (until then it is offered again on every launch).</summary>
        void Finish(StoreOrder order);

        /// <summary>A purchase to deliver: a new one, or one left unfinished by an earlier run.</summary>
        event Action<StoreOrder>? Purchased;

        /// <summary>A purchase that didn't happen: (product id, the player cancelled).</summary>
        event Action<string, bool>? Failed;

        /// <summary>Products or prices changed.</summary>
        event Action? Changed;
    }
}
