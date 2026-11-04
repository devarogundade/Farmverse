#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;

namespace Farmverse.Application.Features
{
    /// <summary>
    /// The game's tuning data (prices, rewards, limits): the live copy at <c>config/catalogue</c>, the
    /// last one seen (kept on the device), or the one compiled into this build — whichever is newest.
    /// The server enforces the same catalogue, so what the game shows is what it charges.
    /// </summary>
    public sealed class CatalogueService : IDisposable
    {
        private const string CacheKey = "catalogue";

        private readonly IDocumentStore _documents;
        private readonly ILocalStore _local;
        private readonly ReactiveProperty<long> _version;
        private IDisposable? _watch;

        public CatalogueService(IDocumentStore documents, ILocalStore local)
        {
            _documents = documents;
            _local = local;
            Current = Builtin();
            var cached = Parse(_local.Read(CacheKey));
            if (cached != null && cached.Version >= Current.Version) Current = cached;
            _version = new ReactiveProperty<long>(Current.Version);
        }

        /// <summary>The catalogue in use right now (never null).</summary>
        public CatalogueDoc Current { get; private set; }

        /// <summary>Changes when a newer catalogue arrives.</summary>
        public IReadOnlyReactiveProperty<long> Version => _version;

        /// <summary>Start listening (once signed in: the catalogue is readable by players only).</summary>
        public void Attach()
        {
            _watch?.Dispose();
            _watch = _documents.Listen(Paths.Catalogue, OnCatalogue, _ => { });
        }

        /// <summary>The gem price of an offer, or 0 when the catalogue doesn't sell it.</summary>
        public int OfferGems(string offerId) => Current.GemOffers.TryGetValue(offerId, out var o) ? o.Gems : 0;

        public GemOffer? Offer(string offerId) => Current.GemOffers.TryGetValue(offerId, out var o) ? o : null;

        public void Dispose() => _watch?.Dispose();

        private void OnCatalogue(DocumentSnapshot snapshot)
        {
            if (!snapshot.Exists || snapshot.Data == null) return;
            CatalogueDoc doc;
            try
            {
                doc = CatalogueDoc.FromWire(new Dictionary<string, object>(snapshot.Data));
            }
            catch (WireFormatException)
            {
                return;   // a broken publish: keep what we have (the server falls back the same way)
            }
            if (doc.Version < Current.Version) return;
            Current = doc;
            _local.Write(CacheKey, WireJson.Serialize(doc.ToWire()));
            _version.Value = doc.Version;
        }

        /// <summary>The catalogue compiled into this build (contracts:gen).</summary>
        public static CatalogueDoc Builtin() => Parse(CatalogueDefaults.Json) ?? new CatalogueDoc { Version = 0 };

        private static CatalogueDoc? Parse(string? json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                return CatalogueDoc.FromWire(WireJson.Parse(json!));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
