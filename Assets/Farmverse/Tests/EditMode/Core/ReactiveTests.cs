#nullable enable
using System;
using System.Collections.Generic;
using Farmverse.Core.Reactive;
using Farmverse.Core.Scheduling;
using NUnit.Framework;

namespace Farmverse.Tests.Core
{
    [TestFixture]
    public sealed class ReactiveTests
    {
        [Test]
        public void EmitsCurrentThenOnlyChanges()
        {
            var property = new ReactiveProperty<int>(1);
            var seen = new List<int>();
            var subscription = property.Subscribe(seen.Add);
            property.Value = 1;
            property.Value = 2;
            subscription.Dispose();
            property.Value = 3;
            CollectionAssert.AreEqual(new[] { 1, 2 }, seen);
        }

        [Test]
        public void HandlersMayUnsubscribeWhileNotified()
        {
            var property = new ReactiveProperty<int>(0);
            IDisposable? self = null;
            var calls = 0;
            self = property.Subscribe(_ => { calls++; self?.Dispose(); }, emitCurrent: false);
            property.Value = 1;
            property.Value = 2;
            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, property.SubscriberCount);
        }

        [Test]
        public void CompositeDisposesEverythingOnceAndLateAddsImmediately()
        {
            var bag = new CompositeDisposable();
            var disposed = 0;
            bag.Add(Disposable.Create(() => disposed++));
            bag.Add(Disposable.Create(() => disposed++));
            bag.Dispose();
            bag.Dispose();
            Assert.AreEqual(2, disposed);
            bag.Add(Disposable.Create(() => disposed++));
            Assert.AreEqual(3, disposed);
        }

        [Test]
        public void ManualSchedulerCompletesInDueOrder()
        {
            var scheduler = new ManualScheduler();
            var order = new List<string>();
            scheduler.Delay(TimeSpan.FromSeconds(2), default).ContinueWith(_ => order.Add("b"), System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
            scheduler.Delay(TimeSpan.FromSeconds(1), default).ContinueWith(_ => order.Add("a"), System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously);
            scheduler.Advance(TimeSpan.FromSeconds(3));
            CollectionAssert.AreEqual(new[] { "a", "b" }, order);
            Assert.AreEqual(0, scheduler.PendingCount);
        }
    }
}
