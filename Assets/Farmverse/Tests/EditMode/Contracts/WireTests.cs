#nullable enable
using System.Collections.Generic;
using System.IO;
using Farmverse.Contracts;
using NUnit.Framework;

namespace Farmverse.Tests.Contracts
{
    [TestFixture]
    public sealed class WireTests
    {
        [Test]
        public void IntegersAcceptEveryNumericShapeButNoFractions()
        {
            Assert.AreEqual(5, Wire.AsInt(5L, "x"));
            Assert.AreEqual(5, Wire.AsInt(5.0, "x"));
            Assert.Throws<WireFormatException>(() => Wire.AsInt(5.5, "x"));
            Assert.Throws<WireFormatException>(() => Wire.AsInt(3_000_000_000L, "x"));
            Assert.Throws<WireFormatException>(() => Wire.AsLong(1e17, "x"));
            Assert.Throws<WireFormatException>(() => Wire.AsInt("5", "x"));
        }

        [Test]
        public void MissingRequiredFieldNamesThePath()
        {
            var error = Assert.Throws<WireFormatException>(() =>
                IslandAwayReport.FromWire(new Dictionary<string, object> { ["awaySeconds"] = 5.0 }));
            StringAssert.StartsWith("IslandAwayReport.", error.Path);
        }

        [Test]
        public void UnknownEnumValuesBecomeUnknown()
        {
            Assert.AreEqual(ErrorCode.Unknown, ErrorCodeWire.FromWire("SOMETHING_NEW"));
            Assert.AreEqual(NotificationKind.FriendJoined, NotificationKindWire.FromWire("friend_joined"));
            Assert.AreEqual(NotificationKind.Unknown, NotificationKindWire.FromWire("raid_landed"));
        }

        [Test]
        public void OptionalFieldsAreOmittedNotNull()
        {
            var wire = new IslandSyncRequest { Meta = new RequestMeta { ClientVersion = "1.0.0", Platform = Platform.Ios } }.ToWire();
            Assert.IsFalse(wire.ContainsKey("adopt"));
            Assert.IsFalse(wire.ContainsKey("mirror"));
        }

        [Test]
        public void JsonRoundTripsEscapesAndNumbers()
        {
            const string json = "{\"a\":[1,2.5,-3],\"b\":\"q\\\"\\n\u00e9\",\"c\":null,\"d\":true}";
            var value = WireJson.Parse(json);
            Assert.AreEqual(json, WireJson.Serialize(value));
            Assert.Throws<WireFormatException>(() => WireJson.Parse("{\"a\":1,\"a\":2}"));
            Assert.Throws<WireFormatException>(() => WireJson.Parse("[1,]"));
        }

        /// <summary>
        /// The shared fixtures (contracts/fixtures/wire-samples.json) are also checked by
        /// the TypeScript suite; together the two prove both sides read and write one shape.
        /// </summary>
        [Test]
        public void SharedWireSamplesRoundTrip()
        {
            var parsers = new Dictionary<string, System.Func<object?, IWireObject>>
            {
                ["IslandSyncRequest"] = v => IslandSyncRequest.FromWire(v),
                ["IslandSyncResponse"] = v => IslandSyncResponse.FromWire(v),
                ["PlayerDoc"] = v => PlayerDoc.FromWire(v),
                ["ErrorBody"] = v => ErrorBody.FromWire(v),
            };

            var root = (Dictionary<string, object>)WireJson.Parse(File.ReadAllText("contracts/fixtures/wire-samples.json"))!;
            var samples = (List<object>)root["samples"];
            Assert.IsTrue(samples.Count > 0);
            foreach (Dictionary<string, object> sample in samples)
            {
                var contract = (string)sample["contract"];
                Assert.IsTrue(parsers.ContainsKey(contract), "add a parser for " + contract);
                var parsed = parsers[contract](sample["value"]);
                Assert.AreEqual(WireJson.Serialize(sample["value"], sortKeys: true),
                                WireJson.Serialize(parsed.ToWire(), sortKeys: true), contract);
            }
        }

        [Test]
        public void CallablesCarryTheirContractNames()
        {
            Assert.AreEqual("island-sync", Callables.IslandSync.Name);
            Assert.AreEqual("player-create", Callables.PlayerCreate.Name);
        }
    }
}
