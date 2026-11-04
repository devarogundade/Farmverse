#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Config;
using Farmverse.Application.Ports;
using Farmverse.Contracts;
using Farmverse.Core.Logging;
using Farmverse.Core.Results;
using Firebase.RemoteConfig;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// Remote Config with compiled-in defaults and real-time updates (11 §3). A
    /// fetch is given <see cref="FetchTimeout"/>; past that the boot continues on
    /// cached or default values. Against the local emulators nothing is fetched
    /// (Remote Config has no emulator, and the real project's values — maintenance,
    /// minimum version, catalogue version — must not steer a local session).
    /// </summary>
    public sealed class RemoteConfigService : IRemoteConfig
    {
        public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(8);

        private readonly FirebaseRemoteConfig _config;
        private readonly FirebaseEnvironment _environment;
        private readonly IMainThread _main;
        private readonly ILog _log;
        private bool _configured;

        public RemoteConfigService(FirebaseRemoteConfig config, FirebaseEnvironment environment, IMainThread main, ILog log)
        {
            _config = config;
            _environment = environment;
            _main = main;
            _log = log.For("RemoteConfig");
        }

        public event Action? Updated;

        public async Task<Result> FetchAndActivate(CancellationToken cancellation)
        {
            try
            {
                if (!_configured)
                {
                    await _config.SetDefaultsAsync(new Dictionary<string, object>
                    {
                        [RemoteKeys.Maintenance] = RemoteKeys.MaintenanceDefault,
                        [RemoteKeys.MinClientVersion] = RemoteKeys.MinClientVersionDefault,
                        [RemoteKeys.IslandShadow] = RemoteKeys.IslandShadowDefault,
                    });
                    if (_environment.UsesEmulators) _log.Info("local emulators: compiled-in defaults only");
                    else
                    {
                        await _config.SetConfigSettingsAsync(new ConfigSettings { MinimumFetchIntervalInMilliseconds = (ulong)_environment.RemoteConfigMinFetchIntervalMs });
                        _config.OnConfigUpdateListener += OnUpdate;
                    }
                    _configured = true;
                }
                if (_environment.UsesEmulators) return Result.Ok();
                var fetch = _config.FetchAndActivateAsync();
                if (await Task.WhenAny(fetch, Task.Delay(FetchTimeout, cancellation)) != fetch)
                {
                    _log.Warn("fetch took too long; using cached values");
                    return Result.Fail(AppError.Of(ErrorCode.Timeout));
                }
                await fetch;
                return Result.Ok();
            }
            catch (Exception error)
            {
                // cached or default values still apply; the boot continues
                _log.Warn("fetch failed; using cached values", null, error);
                return Result.Fail(AppError.FromException(ErrorCode.NetworkRequired, error));
            }
        }

        public string GetString(string key, string fallback)
        {
            var value = _config.GetValue(key);
            return value.Source == ValueSource.StaticValue ? fallback : value.StringValue;
        }

        public long GetLong(string key, long fallback)
        {
            var value = _config.GetValue(key);
            return value.Source == ValueSource.StaticValue ? fallback : value.LongValue;
        }

        public bool GetBool(string key, bool fallback)
        {
            var value = _config.GetValue(key);
            return value.Source == ValueSource.StaticValue ? fallback : value.BooleanValue;
        }

        private async void OnUpdate(object sender, ConfigUpdateEventArgs args)
        {
            if (args.Error != RemoteConfigError.None) return;
            try
            {
                await _config.ActivateAsync();
                _main.Post(() => Updated?.Invoke());
            }
            catch (Exception error)
            {
                _log.Warn("real-time activation failed", null, error);
            }
        }
    }
}
