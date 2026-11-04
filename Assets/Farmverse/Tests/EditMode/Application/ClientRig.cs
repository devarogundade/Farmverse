#nullable enable
using Farmverse.Application.Remote;
using Farmverse.Core.Logging;
using Farmverse.Core.Scheduling;
using Farmverse.Core.Time;

namespace Farmverse.Tests.Application
{
    /// <summary>The client's server path wired with fakes: a scripted server, a hand-driven clock, local storage.</summary>
    internal sealed class ClientRig
    {
        public const string Uid = "uidAAAAAAAAAAAAAAAAAAAA";

        public readonly FakeTransport Server = new FakeTransport();
        public readonly MemoryLocalStore Local = new MemoryLocalStore();
        public readonly FakeConnectivity Connectivity = new FakeConnectivity();
        public readonly ManualScheduler Scheduler = new ManualScheduler();
        public readonly ManualClock DeviceClock = new ManualClock(1_800_000_000_000L);
        public readonly ManualTimer Timer = new ManualTimer();
        public readonly ServerClock Clock;

        public ClientRig()
        {
            Clock = new ServerClock(Timer, DeviceClock);
        }

        public CallableClient Client() => new CallableClient(Server, Clock, NullLog.Instance);
    }
}
