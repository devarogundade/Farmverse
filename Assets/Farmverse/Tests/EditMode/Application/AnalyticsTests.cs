#nullable enable
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Farmverse.Application.Config;
using Farmverse.Application.Features;
using Farmverse.Application.Ports;
using NUnit.Framework;

namespace Farmverse.Tests.Application
{
    [TestFixture]
    public sealed class AnalyticsTests
    {
        private sealed class Recorder : IAnalytics
        {
            public readonly List<(string Name, IReadOnlyDictionary<string, object>? Params)> Events = new List<(string, IReadOnlyDictionary<string, object>?)>();
            public void Event(string name, IReadOnlyDictionary<string, object>? parameters = null) => Events.Add((name, parameters));
            public void SetUserProperty(string name, string value) { }
        }

        [Test]
        public void EventNamesAreValidForFirebase()
        {
            var valid = new Regex("^[a-z][a-z0-9_]{0,39}$");
            foreach (var name in AnalyticsEvents.All) Assert.IsTrue(valid.IsMatch(name), name);
        }

        [Test]
        public void A_failed_boot_is_counted_with_its_state()
        {
            var rc = new FakeRemoteConfig();
            rc.Values[RemoteKeys.Maintenance] = true;
            var recorder = new Recorder();
            var session = SessionStub.Create(new ClientRig(), new FakeDocumentStore(), rc, new FakeApp());
            using var analytics = new GameAnalytics(recorder, session, () => 0);
            session.Start(CancellationToken.None).Wait();
            Assert.AreEqual(1, recorder.Events.Count);
            Assert.AreEqual(AnalyticsEvents.SessionFailed, recorder.Events[0].Name);
            Assert.AreEqual("Maintenance", recorder.Events[0].Params![AnalyticsEvents.Params.State]);
        }
    }

    internal static class SessionStub
    {
        /// <summary>A session over fakes.</summary>
        public static Farmverse.Application.Session.SessionService Create(ClientRig rig, FakeDocumentStore docs, FakeRemoteConfig rc, FakeApp app) =>
            new Farmverse.Application.Session.SessionService(new Farmverse.Application.Session.SessionDependencies
            {
                Platform = new FakePlatform(),
                RemoteConfig = rc,
                Auth = new FakeAuth(),
                Documents = docs,
                Connectivity = rig.Connectivity,
                App = app,
                Crash = new NullCrash(),
                Callables = rig.Client(),
                Scheduler = rig.Scheduler,
                Log = Farmverse.Core.Logging.NullLog.Instance,
            });
    }
}
