#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Config;
using Farmverse.Application.Documents;
using Farmverse.Application.Ports;
using Farmverse.Application.Session;
using Farmverse.Contracts;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;
using Farmverse.Tests.Core;
using NUnit.Framework;

namespace Farmverse.Tests.Application
{
    internal sealed class FakePlatform : IPlatformBootstrap
    {
        public Result Answer = Result.Ok();
        public Task<Result> Initialize(CancellationToken cancellation) => Task.FromResult(Answer);
    }

    internal sealed class FakeRemoteConfig : IRemoteConfig
    {
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
        public event Action? Updated;
        public void RaiseUpdated() => Updated?.Invoke();
        public Task<Result> FetchAndActivate(CancellationToken cancellation) => Task.FromResult(Result.Ok());
        public string GetString(string key, string fallback) => Values.TryGetValue(key, out var v) ? (string)v : fallback;
        public long GetLong(string key, long fallback) => Values.TryGetValue(key, out var v) ? (long)v : fallback;
        public bool GetBool(string key, bool fallback) => Values.TryGetValue(key, out var v) ? (bool)v : fallback;
    }

    internal sealed class FakeAuth : IAuthService
    {
        private readonly ReactiveProperty<AuthUser?> _user = new ReactiveProperty<AuthUser?>(null);
        public IReadOnlyReactiveProperty<AuthUser?> User => _user;
        public int SignIns;
        public Task<Result<AuthUser>> SignIn(CancellationToken cancellation)
        {
            SignIns++;
            _user.Value = new AuthUser(ClientRig.Uid, true);
            return Task.FromResult(Result<AuthUser>.Ok(_user.Value));
        }
        public Task SignOut() { _user.Value = null; return Task.CompletedTask; }
    }

    internal sealed class FakeApp : IAppInfo
    {
        public string Version { get; set; } = "1.2.0";
        public Platform Platform => Platform.Editor;
        public string Locale => "en";
    }

    [TestFixture]
    public sealed class SessionTests
    {
        private readonly InlineContinuations _inline = new InlineContinuations();
        private ClientRig _rig = null!;
        private FakeDocumentStore _docs = null!;
        private FakeRemoteConfig _rc = null!;
        private FakeApp _app = null!;
        private SessionService _session = null!;

        [SetUp]
        public void SetUp()
        {
            _inline.Enter();
            _rig = new ClientRig();
            _docs = new FakeDocumentStore();
            _rc = new FakeRemoteConfig();
            _app = new FakeApp();
            _session = SessionStub.Create(_rig, _docs, _rc, _app);
        }

        [TearDown]
        public void TearDown() => _inline.Exit();

        private void SeedPlayer() =>
            _docs.Put(Paths.Player(ClientRig.Uid), new PlayerDoc { SchemaVersion = 1, Rev = 1, DisplayName = "Ada", AvatarId = "a", ShortId = "10001" });

        private void SuggestNames(params string[] names) =>
            _rig.Server.Ok(_ => new Dictionary<string, object> { ["names"] = new List<object>(names) });

        [Test]
        public void Boots_a_returning_player_to_ready()
        {
            SeedPlayer();
            var states = new List<SessionState>();
            _session.State.Subscribe(states.Add);
            var boot = _session.Start(CancellationToken.None);
            Assert.IsTrue(boot.IsCompleted);
            CollectionAssert.AreEqual(new[]
            {
                SessionState.Starting, SessionState.InitPlatform, SessionState.CheckingVersion, SessionState.Authenticating,
                SessionState.LoadingPlayer, SessionState.Ready,
            }, states);
            Assert.AreEqual(ClientRig.Uid, _session.Uid);
            Assert.AreEqual(0, _rig.Server.Calls.Count);
        }

        [Test]
        public void A_new_player_is_created_under_a_free_suggested_name()
        {
            SuggestNames("Sunny Acres", "Misty Vale");
            _rig.Server.Refuse("NAME_TAKEN");
            _rig.Server.Ok(_ => new Dictionary<string, object> { ["uid"] = ClientRig.Uid });
            _session.Start(CancellationToken.None).Wait();
            Assert.AreEqual(SessionState.Ready, _session.State.Value);
            CollectionAssert.AreEqual(new[] { "player-suggestNames", "player-create", "player-create" }, _rig.Server.Calls.ConvertAll(c => c.Name));
            Assert.AreEqual("Misty Vale", _rig.Server.Calls[2].Payload["displayName"]);
            Assert.AreEqual(SessionService.DefaultAvatar, _rig.Server.Calls[2].Payload["avatarId"]);
        }

        [Test]
        public void A_player_created_elsewhere_meanwhile_counts_as_created()
        {
            SuggestNames("Sunny Acres");
            _rig.Server.Refuse("PLAYER_EXISTS");
            _session.Start(CancellationToken.None).Wait();
            Assert.AreEqual(SessionState.Ready, _session.State.Value);
        }

        [Test]
        public void Stops_for_maintenance_and_outdated_clients()
        {
            _rc.Values[RemoteKeys.Maintenance] = true;
            _session.Start(CancellationToken.None).Wait();
            Assert.AreEqual(SessionState.Maintenance, _session.State.Value);

            _rc.Values[RemoteKeys.Maintenance] = false;
            _rc.Values[RemoteKeys.MinClientVersion] = "1.10.0";
            _session.Start(CancellationToken.None).Wait();
            Assert.AreEqual(SessionState.UpdateRequired, _session.State.Value);
            Assert.AreEqual("1.10.0", _session.Error!.Param("minVersion"));
        }

        [Test]
        public void Compares_versions_numerically()
        {
            Assert.IsTrue(SessionService.CompareVersions("1.10.0", "1.9.9") > 0);
            Assert.AreEqual(0, SessionService.CompareVersions("2.0", "2.0.0"));
            Assert.IsTrue(SessionService.CompareVersions("0.9.12", "1.0.0") < 0);
        }

        [Test]
        public void Pausing_suspends_and_resuming_is_ready_again()
        {
            SeedPlayer();
            _session.Start(CancellationToken.None).Wait();
            _session.Pause();
            Assert.AreEqual(SessionState.Suspended, _session.State.Value);
            _session.Resume();
            Assert.AreEqual(SessionState.Ready, _session.State.Value);
        }
    }
}
