#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Core.Reactive;
using Farmverse.Core.Results;

namespace Farmverse.Application.Ports
{
    /// <summary>Firebase app + App Check initialisation (the first boot step).</summary>
    public interface IPlatformBootstrap
    {
        Task<Result> Initialize(CancellationToken cancellation);
    }

    /// <summary>Remote Config (11 §3): defaults compiled in, fetched values activated at boot.</summary>
    public interface IRemoteConfig
    {
        Task<Result> FetchAndActivate(CancellationToken cancellation);
        string GetString(string key, string fallback);
        long GetLong(string key, long fallback);
        bool GetBool(string key, bool fallback);
        /// <summary>Raised when a real-time update was activated.</summary>
        event Action? Updated;
    }

    public interface IConnectivity
    {
        IReadOnlyReactiveProperty<bool> IsOnline { get; }
    }

    /// <summary>Small persistent key/value strings (pending action batches, cached catalogue, preferences).</summary>
    public interface ILocalStore
    {
        string? Read(string key);
        void Write(string key, string value);
        void Delete(string key);
    }

    /// <summary>Crashlytics behind a port: non-fatals, breadcrumbs and keys, never personal data.</summary>
    public interface ICrashReporter
    {
        void SetUser(string uid);
        void SetKey(string key, string value);
        void Breadcrumb(string message);
        void RecordNonFatal(string name, string message, IReadOnlyDictionary<string, string>? fields = null);
    }

    /// <summary>Typed analytics events (11 §6); parameters are short strings and numbers only.</summary>
    public interface IAnalytics
    {
        void Event(string name, IReadOnlyDictionary<string, object>? parameters = null);
        void SetUserProperty(string name, string value);
    }

    /// <summary>Push notifications: permission, token, and taps that open a route.</summary>
    public interface IPushService
    {
        Task<Result<string>> RequestPermissionAndToken(CancellationToken cancellation);
        /// <summary>A notification was tapped: its route and its data payload (args as <c>arg_*</c>).</summary>
        event Action<string, IReadOnlyDictionary<string, string>>? Opened;
    }

    /// <summary>A stable id for this install (the device document's key for push tokens).</summary>
    public interface IInstallationId
    {
        Task<string> Get(CancellationToken cancellation);
    }

    /// <summary>Main-thread marshalling for SDK callbacks that may arrive on other threads.</summary>
    public interface IMainThread
    {
        void Post(Action action);
    }

    /// <summary>The installed app: version and platform, for every request's meta.</summary>
    public interface IAppInfo
    {
        string Version { get; }
        Farmverse.Contracts.Platform Platform { get; }
        string Locale { get; }
    }

}
