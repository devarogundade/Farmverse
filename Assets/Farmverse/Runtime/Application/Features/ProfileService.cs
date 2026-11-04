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
    /// The player's public profile (<c>players/{uid}</c>: name, avatar) — what friends, visitors and the
    /// leaderboard see. Live from the server; changes go through <c>player-updateProfile</c> (names stay unique).
    /// </summary>
    public sealed class ProfileService : IDisposable
    {
        private readonly IDocumentStore _documents;
        private readonly ServerCalls _server;
        private IDisposable? _watch;

        public ProfileService(IDocumentStore documents, ServerCalls server)
        {
            _documents = documents;
            _server = server;
        }

        /// <summary>Raised when the server's profile arrives or changes.</summary>
        public event Action? Changed;

        public bool Online { get; private set; }

        /// <summary>The server's copy (null until it arrived).</summary>
        public PlayerDoc? Player { get; private set; }

        public void Attach(string uid)
        {
            _watch?.Dispose();
            _watch = _documents.Listen(Paths.Player(uid), OnPlayer, _ => { });
            Online = true;
        }

        public Task<Result<Empty>> Rename(string name, CancellationToken cancellation = default) =>
            _server.UpdateProfile(new PlayerUpdateProfileRequest { DisplayName = name }, cancellation);

        public Task<Result<Empty>> SetAvatar(string avatarId, CancellationToken cancellation = default) =>
            _server.UpdateProfile(new PlayerUpdateProfileRequest { AvatarId = avatarId }, cancellation);

        public Task<Result<Empty>> SetLocale(string locale, CancellationToken cancellation = default) =>
            _server.UpdateProfile(new PlayerUpdateProfileRequest { Locale = locale }, cancellation);

        public void Dispose() => _watch?.Dispose();

        private void OnPlayer(DocumentSnapshot snapshot)
        {
            if (!snapshot.Exists || snapshot.Data == null) return;
            try
            {
                Player = PlayerDoc.FromWire(new Dictionary<string, object>(snapshot.Data));
            }
            catch (WireFormatException)
            {
                return;
            }
            Changed?.Invoke();
        }
    }
}
