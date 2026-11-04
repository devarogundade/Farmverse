#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;

namespace Farmverse.Application.Features
{
    /// <summary>One row on the board.</summary>
    public sealed class BoardRow
    {
        public BoardRow(string uid, LeaderboardEntryDoc doc)
        {
            Uid = uid;
            Name = doc.Name;
            AvatarId = doc.AvatarId;
            Locale = doc.Locale;
            Worth = doc.Worth;
            Level = (int)doc.Level;
        }

        public string Uid { get; }
        public string Name { get; }
        public string AvatarId { get; }
        public string Locale { get; }
        public long Worth { get; }
        public int Level { get; }
    }

    /// <summary>
    /// The weekly leaderboard (<c>leaderboards/{week}/players</c>), written by the server whenever it runs an
    /// island: the world's top 50, the top 50 in the player's language, the player's friends, and the player.
    /// Live while attached; the week rolls with the server clock (Monday 00:00 UTC).
    /// </summary>
    public sealed class LeaderboardService : IDisposable
    {
        public const int PageSize = 50;
        /// <summary>Weeks count from Monday 5 January 2026 (the contracts' WEEK_EPOCH_MS).</summary>
        public static readonly long WeekEpochMs = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        private const long WeekMs = 7L * 86_400_000L;

        private readonly IDocumentStore _documents;
        private readonly Func<long> _nowMs;
        private readonly ReactiveProperty<int> _version = new ReactiveProperty<int>(0);
        private readonly Dictionary<string, IDisposable> _friendWatches = new Dictionary<string, IDisposable>();
        private readonly Dictionary<string, BoardRow> _friendRows = new Dictionary<string, BoardRow>();
        private IDisposable? _world, _country, _mine;
        private string? _uid, _week, _locale;

        public LeaderboardService(IDocumentStore documents, Func<long> nowMs)
        {
            _documents = documents;
            _nowMs = nowMs;
        }

        public IReadOnlyReactiveProperty<int> Version => _version;

        public IReadOnlyList<BoardRow> World { get; private set; } = Array.Empty<BoardRow>();
        public IReadOnlyList<BoardRow> Country { get; private set; } = Array.Empty<BoardRow>();
        public BoardRow? Mine { get; private set; }
        public bool Loaded { get; private set; }

        /// <summary>Friends on this week's board (friends who haven't played this week have no row).</summary>
        public IEnumerable<BoardRow> Friends => _friendRows.Values;

        public static string WeekId(long nowMs) => "w" + (long)Math.Floor((nowMs - WeekEpochMs) / (double)WeekMs);

        /// <summary>When this week's board closes (prizes are paid just after).</summary>
        public long WeekEndsAtMs => WeekEpochMs + ((long)Math.Floor((_nowMs() - WeekEpochMs) / (double)WeekMs) + 1) * WeekMs;

        public void Attach(string uid, string locale)
        {
            _uid = uid;
            _locale = locale;
            Refresh(force: true);
        }

        /// <summary>Follow these friends' rows (call when the friends list changes).</summary>
        public void Follow(IEnumerable<string> friendUids)
        {
            if (_week == null) return;
            var want = new HashSet<string>(friendUids);
            foreach (var gone in new List<string>(_friendWatches.Keys))
                if (!want.Contains(gone))
                {
                    _friendWatches[gone].Dispose();
                    _friendWatches.Remove(gone);
                    _friendRows.Remove(gone);
                }
            foreach (var f in want)
            {
                if (_friendWatches.ContainsKey(f) || _friendWatches.Count >= PageSize) continue;
                var id = f;
                _friendWatches[id] = _documents.Listen(Paths.BoardEntry(_week, id), s => OnRow(s, row => { if (row == null) _friendRows.Remove(id); else _friendRows[id] = row; }), _ => { });
            }
        }

        /// <summary>Re-listen when the week rolled over (call now and then, e.g. when the board opens).</summary>
        public void Refresh(bool force = false)
        {
            if (_uid == null) return;
            var week = WeekId(_nowMs());
            if (!force && week == _week) return;
            _week = week;
            _world?.Dispose();
            _country?.Dispose();
            _mine?.Dispose();
            foreach (var w in _friendWatches.Values) w.Dispose();
            var followed = new List<string>(_friendWatches.Keys);
            _friendWatches.Clear();
            _friendRows.Clear();
            var order = new[] { new QueryOrder("worth", true) };
            _world = _documents.ListenQuery(new QuerySpec(Paths.Board(week), Array.Empty<QueryFilter>(), order, PageSize), s => World = Rows(s), _ => { });
            _country = string.IsNullOrEmpty(_locale) ? null
                : _documents.ListenQuery(new QuerySpec(Paths.Board(week), new[] { new QueryFilter("locale", FilterOp.Equal, _locale!) }, order, PageSize), s => Country = Rows(s), _ => { });
            _mine = _documents.Listen(Paths.BoardEntry(week, _uid), s => OnRow(s, row => Mine = row), _ => { });
            Follow(followed);
        }

        public void Dispose()
        {
            _world?.Dispose();
            _country?.Dispose();
            _mine?.Dispose();
            foreach (var w in _friendWatches.Values) w.Dispose();
        }

        private IReadOnlyList<BoardRow> Rows(QuerySnapshot snapshot)
        {
            var rows = new List<BoardRow>();
            foreach (var d in snapshot.Documents)
            {
                if (!d.Exists || d.Data == null) continue;
                try { rows.Add(new BoardRow(d.Id, LeaderboardEntryDoc.FromWire(new Dictionary<string, object>(d.Data)))); }
                catch (WireFormatException) { }
            }
            Loaded = true;
            _version.Value = _version.Value + 1;
            return rows;
        }

        private void OnRow(DocumentSnapshot snapshot, Action<BoardRow?> set)
        {
            BoardRow? row = null;
            if (snapshot.Exists && snapshot.Data != null)
                try { row = new BoardRow(snapshot.Id, LeaderboardEntryDoc.FromWire(new Dictionary<string, object>(snapshot.Data))); }
                catch (WireFormatException) { }
            set(row);
            _version.Value = _version.Value + 1;
        }
    }
}
