#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Farmverse.Application.Ports;
using Farmverse.Core.Logging;
using Firebase.Installations;

namespace Farmverse.Infrastructure.Firebase
{
    /// <summary>
    /// The install's id (no longer composed since push was removed; kept until the web move): the Firebase Installations id
    /// (stable across launches, reset on reinstall), or — if that service is
    /// unreachable — a random id kept in local storage, so registration still
    /// keys one device document per install.
    /// </summary>
    public sealed class FirebaseInstallationId : IInstallationId
    {
        public const string FallbackKey = "install.id";
        private static readonly Regex Valid = new Regex("^[A-Za-z0-9_-]{8,64}$");

        private readonly ILocalStore _local;
        private readonly ILog _log;
        private string? _cached;

        public FirebaseInstallationId(ILocalStore local, ILog log)
        {
            _local = local;
            _log = log.For("Installations");
        }

        public async Task<string> Get(CancellationToken cancellation)
        {
            if (_cached != null) return _cached;
            try
            {
                var id = await FirebaseInstallations.DefaultInstance.GetIdAsync();
                if (!string.IsNullOrEmpty(id) && Valid.IsMatch(id)) return _cached = id;
            }
            catch (Exception error)
            {
                _log.Warn("installation id unavailable, using a local one", null, error);
            }
            var stored = _local.Read(FallbackKey);
            if (stored == null || !Valid.IsMatch(stored))
            {
                stored = Guid.NewGuid().ToString("N");
                _local.Write(FallbackKey, stored);
            }
            return _cached = stored;
        }
    }
}
