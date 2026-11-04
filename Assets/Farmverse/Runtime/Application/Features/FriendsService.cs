#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Application.Features
{
    /// <summary>One friend as the list shows them (<c>players/{uid}/friends/{friendUid}</c>).</summary>
    public sealed class FriendRow
    {
        public FriendRow(string uid, FriendDoc doc)
        {
            Uid = uid;
            Name = doc.DisplayName;
            AvatarId = doc.AvatarId;
            ShortId = doc.ShortId;
            Since = doc.Since;
        }

        public string Uid { get; }
        public string Name { get; }
        public string AvatarId { get; }
        public string ShortId { get; }
        /// <summary>Server ms when the two became friends.</summary>
        public long Since { get; }
    }

    /// <summary>
    /// Friends (R44): the live list (newest first, the 50 the rules allow a page to hold), this player's
    /// invite code, and accepting someone else's code — typed, pasted as a link, or arriving as a deep link
    /// (https://farmverse.world/invite/CODE) before the player is signed in, kept until they are.
    /// </summary>
    public sealed class FriendsService : IDisposable
    {
        public const int PageSize = 50;
        public const int CodeLength = 8;

        private readonly IDocumentStore _documents;
        private readonly ServerCalls _server;
        private readonly ReactiveProperty<bool> _ready = new ReactiveProperty<bool>(false);
        private readonly ReactiveProperty<int> _version = new ReactiveProperty<int>(0);
        private IReadOnlyList<FriendRow> _friends = Array.Empty<FriendRow>();
        private IDisposable? _watch;
        private string? _pendingCode;

        public FriendsService(IDocumentStore documents, ServerCalls server)
        {
            _documents = documents;
            _server = server;
        }

        /// <summary>Signed in and listening: invites and codes work.</summary>
        public IReadOnlyReactiveProperty<bool> Ready => _ready;

        /// <summary>Bumped whenever the list, the invite or a pending code changes (screens rebuild on it).</summary>
        public IReadOnlyReactiveProperty<int> Version => _version;

        public IReadOnlyList<FriendRow> Friends => _friends;

        /// <summary>This player's latest invite (null until they make one this session).</summary>
        public FriendsCreateInviteResponse? Invite { get; private set; }

        /// <summary>A friend just added through a deep link, for the screen to celebrate once.</summary>
        public FriendsAcceptResponse? JustAdded { get; set; }

        /// <summary>Start listening to the signed-in player's friends; a code that arrived earlier is accepted now.</summary>
        public void Attach(string uid)
        {
            _watch?.Dispose();
            var query = new QuerySpec(Paths.Friends(uid), Array.Empty<QueryFilter>(), new[] { new QueryOrder("since", true) }, PageSize);
            _watch = _documents.ListenQuery(query, OnFriends, _ => { });
            _ready.Value = true;
            Bump();
            if (_pendingCode != null)
            {
                var code = _pendingCode;
                _pendingCode = null;
                _ = AcceptPending(code);
            }
        }

        public void Detach()
        {
            _watch?.Dispose();
            _watch = null;
            _ready.Value = false;
            Bump();
        }

        /// <summary>A deep link opened the app: an invite link is accepted (now, or once signed in).</summary>
        public void OfferLink(string url)
        {
            if (!url.Contains("/invite/")) return;
            var code = CodeFrom(url);
            if (code == null) return;
            if (_ready.Value) _ = AcceptPending(code);
            else _pendingCode = code;
        }

        /// <summary>
        /// The referral program (an invited player's side): their island reached the referral level. The
        /// island is mirrored to the server first (the server checks the level on its own copy), then both
        /// players are rewarded — rewards arrive through <see cref="RewardsService"/>.
        /// </summary>
        public async Task<Result<ReferralClaimResponse>> ClaimReferral(string islandJson, CancellationToken cancellation = default)
        {
            var mirrored = await _server.IslandSync(islandJson, mirror: true, cancellation);
            if (mirrored.IsFailure) return Result<ReferralClaimResponse>.Fail(mirrored.Error);
            return await _server.ReferralClaim(cancellation);
        }

        public async Task<Result<FriendsCreateInviteResponse>> CreateInvite(CancellationToken cancellation = default)
        {
            var result = await _server.CreateInvite(cancellation);
            if (result.IsOk)
            {
                Invite = result.Value;
                Bump();
            }
            return result;
        }

        /// <summary>Become friends with whoever made this code (a code or a link to it).</summary>
        public Task<Result<FriendsAcceptResponse>> Accept(string codeOrLink, CancellationToken cancellation = default)
        {
            var code = CodeFrom(codeOrLink);
            if (code == null) return Task.FromResult(Result<FriendsAcceptResponse>.Fail(AppError.Of(ErrorCode.InviteInvalid)));
            return _server.AcceptInvite(code, cancellation);
        }

        /// <summary>The 8-character code in what the player typed or pasted (spaces, dashes, case and the link around it don't matter).</summary>
        public static string? CodeFrom(string text)
        {
            var at = text.LastIndexOf("/invite/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) text = text.Substring(at + "/invite/".Length);
            var sb = new StringBuilder(CodeLength);
            foreach (var ch in text)
            {
                if (ch == '?' || ch == '#') break;
                if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            }
            if (sb.Length != CodeLength) return null;
            foreach (var ch in sb.ToString())
                if (!(ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9')) return null;
            return sb.ToString();
        }

        public void Dispose() => _watch?.Dispose();

        private async Task AcceptPending(string code)
        {
            var result = await Accept(code);
            if (result.IsOk)
            {
                JustAdded = result.Value;
                Bump();
            }
        }

        private void OnFriends(QuerySnapshot snapshot)
        {
            var rows = new List<FriendRow>(snapshot.Documents.Count);
            foreach (var doc in snapshot.Documents)
            {
                if (!doc.Exists || doc.Data == null) continue;
                try
                {
                    rows.Add(new FriendRow(doc.Id, FriendDoc.FromWire(new Dictionary<string, object>(doc.Data))));
                }
                catch (WireFormatException)
                {
                    // a row the server would never write; leave it out rather than break the list
                }
            }
            _friends = rows;
            Bump();
        }

        private void Bump() => _version.Value = _version.Value + 1;
    }
}
