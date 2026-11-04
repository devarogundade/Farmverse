#nullable enable
using System.Collections.Generic;
using System.Threading;
using Farmverse.Application.Ports;
using Farmverse.Application.Remote;
using Farmverse.Contracts;
using Farmverse.Core.Logging;
using Farmverse.Core.Time;
using NUnit.Framework;

namespace Farmverse.Tests.Application
{
    [TestFixture]
    public sealed class CallableClientTests
    {
        private static (CallableClient, FakeTransport, ServerClock, ManualTimer) Rig()
        {
            var transport = new FakeTransport();
            var timer = new ManualTimer();
            var clock = new ServerClock(timer, new ManualClock(1_000));
            return (new CallableClient(transport, clock, NullLog.Instance), transport, clock, timer);
        }

        private static IslandSyncRequest Request() => new IslandSyncRequest { Meta = new RequestMeta { ClientVersion = "1.0.0", Platform = Platform.Ios } };

        [Test]
        public void Unwraps_a_success_and_syncs_the_server_clock()
        {
            var (client, transport, clock, _) = Rig();
            transport.Ok(_ => new Dictionary<string, object>
            {
                ["state"] = "{}", ["rulesVersion"] = 1L, ["nextWakeMs"] = 9_000L,
                ["report"] = new Dictionary<string, object> { ["awaySeconds"] = 5.0, ["workers"] = 2L, ["loads"] = 3L, ["seedCoins"] = 0L, ["storageFull"] = false, ["gathered"] = new List<object>() },
            });
            var result = client.Call(Callables.IslandSync, Request(), CancellationToken.None).Result;
            Assert.IsTrue(result.IsOk);
            Assert.AreEqual(3, result.Value.Report.Loads);
            Assert.AreEqual("island-sync", transport.Calls[0].Name);
            Assert.IsTrue(clock.IsSynced.Value);
            Assert.AreEqual(transport.ServerTimeMs, clock.NowMs);
        }

        [Test]
        public void Turns_a_refusal_envelope_into_the_contract_error()
        {
            var (client, transport, _, _) = Rig();
            transport.Refuse("CLIENT_OUTDATED", new Dictionary<string, string> { ["minVersion"] = "2.0.0" });
            var result = client.Call(Callables.IslandSync, Request(), CancellationToken.None).Result;
            Assert.AreEqual(ErrorCode.ClientOutdated, result.Error.Code);
            Assert.AreEqual("2.0.0", result.Error.Param("minVersion"));
        }

        [TestCase(TransportFailure.Unavailable, ErrorCode.NetworkRequired)]
        [TestCase(TransportFailure.Unauthenticated, ErrorCode.Unauthenticated)]
        [TestCase(TransportFailure.Internal, ErrorCode.Internal)]
        public void Maps_transport_failures_to_client_codes(TransportFailure failure, ErrorCode expected)
        {
            var (client, transport, _, _) = Rig();
            transport.Fail(failure);
            Assert.AreEqual(expected, client.Call(Callables.IslandSync, Request(), CancellationToken.None).Result.Error.Code);
        }

        [Test]
        public void Reports_a_malformed_envelope_as_internal_without_throwing()
        {
            var (client, transport, _, _) = Rig();
            transport.Raw("not an envelope");
            Assert.AreEqual(ErrorCode.Internal, client.Call(Callables.IslandSync, Request(), CancellationToken.None).Result.Error.Code);
        }
    }
}
