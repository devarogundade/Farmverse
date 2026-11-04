#nullable enable
using System;
using System.Collections.Concurrent;
using Farmverse.Application.Ports;
using UnityEngine;

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>Runs posted actions on the main thread at the next Update (SDK callbacks may arrive on worker threads).</summary>
    public sealed class UnityMainThread : MonoBehaviour, IMainThread
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private int _mainThreadId;

        private void Awake() => _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        public void Post(Action action)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId) action();
            else _queue.Enqueue(action);
        }

        private void Update()
        {
            while (_queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
            }
        }
    }
}
