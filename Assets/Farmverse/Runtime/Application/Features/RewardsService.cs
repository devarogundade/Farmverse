#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>A reward the server gave and the game hasn't collected yet.</summary>
    public sealed class PendingReward
    {
        public PendingReward(string id, RewardDoc doc)
        {
            Id = id;
            Reason = doc.Reason;
            Coins = (int)doc.Coins;
            Gems = (int)doc.Gems;
            About = doc.About;
        }

        public string Id { get; }
        public RewardReason Reason { get; }
        public int Coins { get; }
        public int Gems { get; }
        /// <summary>The friend the reward is about.</summary>
        public string About { get; }
    }

    /// <summary>
    /// Rewards from the server (the referral program): the uncollected ones, live, and collecting one —
    /// once, by the server's say (rewards-claim); the game then adds the coins and gems to the island.
    /// </summary>
    public sealed class RewardsService : IDisposable
    {
        private readonly IDocumentStore _documents;
        private readonly ServerCalls _server;
        private readonly ReactiveProperty<int> _version = new ReactiveProperty<int>(0);
        private readonly HashSet<string> _claiming = new HashSet<string>();
        private IReadOnlyList<PendingReward> _pending = Array.Empty<PendingReward>();
        private IDisposable? _watch;

        public RewardsService(IDocumentStore documents, ServerCalls server)
        {
            _documents = documents;
            _server = server;
        }

        public IReadOnlyReactiveProperty<int> Version => _version;

        /// <summary>Uncollected rewards, minus any being collected right now.</summary>
        public IReadOnlyList<PendingReward> Pending => _pending;

        public void Attach(string uid)
        {
            _watch?.Dispose();
            var query = new QuerySpec(Paths.Rewards(uid), new[] { new QueryFilter("claimedAt", FilterOp.Equal, 0L) }, Array.Empty<QueryOrder>(), 50);
            _watch = _documents.ListenQuery(query, OnRewards, _ => { });
        }

        /// <summary>Collect a reward. A fresh answer means: add these coins and gems now.</summary>
        public async Task<Result<RewardsClaimResponse>> Claim(string id, CancellationToken cancellation = default)
        {
            if (!_claiming.Add(id)) return Result<RewardsClaimResponse>.Fail(AppError.Of(ErrorCode.Conflict));
            Refresh();
            try
            {
                return await _server.RewardsClaim(id, cancellation);
            }
            finally
            {
                _claiming.Remove(id);
                Refresh();
            }
        }

        public void Dispose() => _watch?.Dispose();

        private List<PendingReward> _all = new List<PendingReward>();

        private void OnRewards(QuerySnapshot snapshot)
        {
            var rows = new List<PendingReward>();
            foreach (var doc in snapshot.Documents)
            {
                if (!doc.Exists || doc.Data == null) continue;
                try
                {
                    var reward = RewardDoc.FromWire(new Dictionary<string, object>(doc.Data));
                    if (reward.ClaimedAt == 0) rows.Add(new PendingReward(doc.Id, reward));
                }
                catch (WireFormatException)
                {
                    // not a reward the server wrote; skip it
                }
            }
            _all = rows;
            Refresh();
        }

        private void Refresh()
        {
            _pending = _all.FindAll(r => !_claiming.Contains(r.Id));
            _version.Value = _version.Value + 1;
        }
    }
}
