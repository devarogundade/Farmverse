#nullable enable
using System.Threading;

namespace Farmverse.Tests.Core
{
    /// <summary>
    /// Unity runs EditMode tests with a <see cref="SynchronizationContext"/> that defers
    /// <c>await</c> continuations to the next editor tick. Async engine code under test
    /// must instead resume inline when a test completes a task, so fixtures that drive
    /// async code remove the context for the duration of each test.
    /// </summary>
    internal sealed class InlineContinuations
    {
        private SynchronizationContext? _saved;

        public void Enter()
        {
            _saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
        }

        public void Exit() => SynchronizationContext.SetSynchronizationContext(_saved);
    }
}
