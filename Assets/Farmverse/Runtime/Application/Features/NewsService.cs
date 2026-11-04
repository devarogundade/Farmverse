#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;

namespace Farmverse.Application.Features
{
    /// <summary>One news item as the News screen shows it.</summary>
    public sealed class NewsItem
    {
        public NewsItem(string id, NewsDoc doc)
        {
            Id = id;
            Title = doc.Title;
            Body = doc.Body;
            Art = doc.Art;
            Url = doc.Url;
            PublishedAt = doc.PublishedAt;
            _i18n = doc.I18n;
        }

        private readonly Dictionary<string, NewsText>? _i18n;

        /// <summary>The title and body in a language (ISO code), English when the item wasn't written in it.</summary>
        public (string title, string body) In(string iso) =>
            _i18n != null && _i18n.TryGetValue(iso, out var t) ? (t.Title, t.Body) : (Title, Body);

        public string Id { get; }
        public string Title { get; }
        public string Body { get; }
        public string? Art { get; }
        public string? Url { get; }
        public long PublishedAt { get; }
    }

    /// <summary>The game's News (the team's announcements, <c>news/</c>): the newest 20, live.</summary>
    public sealed class NewsService : IDisposable
    {
        public const int PageSize = 20;

        private readonly IDocumentStore _documents;
        private readonly ReactiveProperty<int> _version = new ReactiveProperty<int>(0);
        private IReadOnlyList<NewsItem> _items = Array.Empty<NewsItem>();
        private IDisposable? _watch;

        public NewsService(IDocumentStore documents)
        {
            _documents = documents;
        }

        public IReadOnlyReactiveProperty<int> Version => _version;

        /// <summary>Newest first.</summary>
        public IReadOnlyList<NewsItem> Items => _items;

        /// <summary>True once the first answer arrived (an empty list then means no news).</summary>
        public bool Loaded { get; private set; }

        public void Attach()
        {
            _watch?.Dispose();
            var query = new QuerySpec(Paths.News, Array.Empty<QueryFilter>(), new[] { new QueryOrder("publishedAt", true) }, PageSize);
            _watch = _documents.ListenQuery(query, OnNews, _ => { });
        }

        public void Dispose() => _watch?.Dispose();

        private void OnNews(QuerySnapshot snapshot)
        {
            var rows = new List<NewsItem>(snapshot.Documents.Count);
            foreach (var doc in snapshot.Documents)
            {
                if (!doc.Exists || doc.Data == null) continue;
                try
                {
                    rows.Add(new NewsItem(doc.Id, NewsDoc.FromWire(new Dictionary<string, object>(doc.Data))));
                }
                catch (WireFormatException)
                {
                    // a malformed item is left out
                }
            }
            _items = rows;
            Loaded = true;
            _version.Value = _version.Value + 1;
        }
    }
}
