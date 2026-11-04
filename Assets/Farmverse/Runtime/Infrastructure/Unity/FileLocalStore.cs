#nullable enable
using System.IO;
using Farmverse.Application.Ports;
using Farmverse.Core.Logging;

namespace Farmverse.Infrastructure.Unity
{
    /// <summary>
    /// <see cref="ILocalStore"/> as one file per key under persistentDataPath, written
    /// atomically (temp file + replace) so a kill mid-write never corrupts the
    /// pending-action outbox. PlayerPrefs is avoided: size limits and no atomicity.
    /// </summary>
    public sealed class FileLocalStore : ILocalStore
    {
        private readonly string _root;
        private readonly ILog _log;

        public FileLocalStore(ILog log, string? root = null)
        {
            _root = root ?? Path.Combine(UnityEngine.Application.persistentDataPath, "farmverse");
            _log = log.For("LocalStore");
            Directory.CreateDirectory(_root);
        }

        public string? Read(string key)
        {
            var path = PathOf(key);
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (IOException error)
            {
                _log.Warn("read failed: " + key, null, error);
                return null;
            }
        }

        public void Write(string key, string value)
        {
            var path = PathOf(key);
            var temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, value);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (IOException error)
            {
                _log.Warn("write failed: " + key, null, error);
            }
        }

        public void Delete(string key)
        {
            var path = PathOf(key);
            if (File.Exists(path)) File.Delete(path);
        }

        private string PathOf(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) key = key.Replace(c, '_');
            return Path.Combine(_root, key + ".json");
        }
    }
}
