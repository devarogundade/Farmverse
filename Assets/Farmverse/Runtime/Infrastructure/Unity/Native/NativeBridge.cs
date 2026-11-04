#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Farmverse.Infrastructure.Unity.Native
{
    /// <summary>A native step failed. <see cref="Code"/> is <c>cancelled</c>, <c>unsupported</c>, <c>unavailable</c> or <c>failed</c>.</summary>
    public sealed class NativeBridgeException : Exception
    {
        public const string Cancelled = "cancelled";
        public const string Unsupported = "unsupported";
        public const string Unavailable = "unavailable";
        public const string Failed = "failed";

        public NativeBridgeException(string code, string message) : base(message) => Code = code;

        public string Code { get; }
    }

    /// <summary>
    /// The in-repo native plug-ins (Plugins/Android/*.java, Plugins/iOS/*.mm):
    /// one call in, one callback out, as a task. Android answers through an
    /// <see cref="AndroidJavaProxy"/>; iOS through a C function pointer carrying
    /// a request id. Either way the task completes on Unity's main thread.
    /// </summary>
    internal static class NativeBridge
    {
        public const string AndroidPackage = "com.farmverse.nativebridge";

        private static readonly Dictionary<int, TaskCompletionSource<string>> Pending = new Dictionary<int, TaskCompletionSource<string>>();
        private static int _next;
        private static SynchronizationContext? _main;

        /// <summary>Remember Unity's main-thread context; call once from the main thread (the composition root does).</summary>
        public static void Initialize() => _main ??= SynchronizationContext.Current;

        /// <summary>Start a request; the returned id goes to the native side and comes back with the answer.</summary>
        public static (int Id, Task<string> Answer) Begin(CancellationToken cancellation)
        {
            var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            int id;
            lock (Pending)
            {
                id = ++_next;
                Pending[id] = source;
            }
            if (cancellation.CanBeCanceled)
                cancellation.Register(() => Complete(id, null, NativeBridgeException.Cancelled, "cancelled"));
            return (id, source.Task);
        }

        /// <summary>Called by the native side (any thread): <paramref name="error"/> null means success.</summary>
        public static void Complete(int id, string? value, string? error, string? message)
        {
            TaskCompletionSource<string>? source;
            lock (Pending)
            {
                if (!Pending.TryGetValue(id, out source)) return;
                Pending.Remove(id);
            }
            void Finish()
            {
                if (error == null) source.TrySetResult(value ?? string.Empty);
                else source.TrySetException(new NativeBridgeException(error, message ?? error));
            }
            if (_main != null && SynchronizationContext.Current != _main) _main.Post(_ => Finish(), null);
            else Finish();
        }

        public static NativeBridgeException NotOnThisPlatform(string feature) =>
            new NativeBridgeException(NativeBridgeException.Unsupported, feature + " is not available on " + UnityEngine.Application.platform);

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>The Java side's <c>FarmverseCallback</c>, answering one request.</summary>
        internal sealed class AndroidCallback : AndroidJavaProxy
        {
            private readonly int _id;

            public AndroidCallback(int id) : base(AndroidPackage + ".FarmverseCallback") => _id = id;

            // names and signatures match FarmverseCallback.java
            public void onSuccess(string value) => Complete(_id, value, null, null);
            public void onError(string code, string message) => Complete(_id, null, string.IsNullOrEmpty(code) ? NativeBridgeException.Failed : code, message);
        }

        public static AndroidJavaObject CurrentActivity()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return player.GetStatic<AndroidJavaObject>("currentActivity");
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>The iOS side calls this with the request id it was given.</summary>
        public delegate void IosCallback(int id, string? value, string? error, string? message);

        [AOT.MonoPInvokeCallback(typeof(IosCallback))]
        public static void OnIosAnswer(int id, string? value, string? error, string? message) => Complete(id, value, error, message);
#endif
    }
}
