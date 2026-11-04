#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Farmverse.Presentation.UI.Game
{
    /// <summary>
    /// The game's text in the player's language. Every label the screens make goes through <see cref="Of"/>:
    /// the English text is the key. Numbers are taken out first ("+80 gems" → "+{0} gems") so one entry covers
    /// every amount, then put back. Tables are JSON files in Resources/Localization/{iso}.json:
    /// <c>{"entries":[{"k":"Save your farm","v":"Guarda tu granja"}]}</c>. A missing entry shows the English.
    ///
    /// In the Editor, every text shown is collected into Resources/Localization/en.json (the source table for
    /// translators) when Play stops: play through the screens, then hand that file over for translation.
    /// </summary>
    public static class Loc
    {
        [Serializable] private sealed class Entry { public string k = ""; public string v = ""; }
        [Serializable] private sealed class Table { public List<Entry> entries = new List<Entry>(); }

        private static readonly Regex Number = new Regex(@"(?<![A-Za-z0-9_#.])[+\-]?\d[\d,.]*", RegexOptions.Compiled);
        private static Dictionary<string, string> _table = new Dictionary<string, string>();
        /// <summary>
        /// Entries with a {x} slot ("{x} is ready!", "Raiders {x} · landing in {0}s"): text made at runtime from a
        /// name or a direction. The slot is translated on its own ("barn", "from the east"), so one entry covers
        /// every building or direction. Longest first, so the most specific wins.
        /// </summary>
        private static List<(Regex re, string value)> _templates = new List<(Regex, string)>();
        private static string _iso = "en";
#if UNITY_EDITOR
        private static readonly SortedSet<string> Seen = new SortedSet<string>(StringComparer.Ordinal);
#endif

        /// <summary>The language shown (ISO code).</summary>
        public static string Iso => _iso;

        /// <summary>True when a table for the language ships with the game (English always).</summary>
        public static bool Has(string iso) => iso == "en" || Resources.Load<TextAsset>("Localization/" + iso) != null;

        /// <summary>Switch language (loads its table; English needs none).</summary>
        public static void Use(string iso)
        {
            _iso = iso;
            _table = new Dictionary<string, string>(StringComparer.Ordinal);
            _templates = new List<(Regex, string)>();
            if (iso == "en") return;
            var asset = Resources.Load<TextAsset>("Localization/" + iso);
            if (asset == null) return;
            try
            {
                var t = JsonUtility.FromJson<Table>(asset.text);
                foreach (var e in t?.entries ?? new List<Entry>())
                    if (!string.IsNullOrEmpty(e.k) && !string.IsNullOrEmpty(e.v)) _table[e.k] = e.v;
                foreach (var kv in _table)
                    if (kv.Key.Contains("{x}"))
                        _templates.Add((new Regex("^" + Regex.Escape(kv.Key).Replace("\\{x}", "(?<x>.+?)") + "$", RegexOptions.CultureInvariant), kv.Value));
                _templates.Sort((a, b) => b.re.ToString().Length.CompareTo(a.re.ToString().Length));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Loc] " + iso + ": " + e.Message);
            }
        }

        /// <summary>The text in the player's language.</summary>
        public static string Of(string? text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (!HasLetters(text!)) return text!;
            var numbers = new List<string>();
            var key = Number.Replace(text!, m =>
            {
                numbers.Add(m.Value);
                return "{" + (numbers.Count - 1).ToString(CultureInfo.InvariantCulture) + "}";
            });
#if UNITY_EDITOR
            Seen.Add(key);
#endif
            if (_table.Count == 0) return text!;
            if (!_table.TryGetValue(key, out var translated) && !Template(key, out translated)) return text!;
            if (numbers.Count == 0) return translated;
            var sb = new StringBuilder(translated);
            for (var i = numbers.Count - 1; i >= 0; i--) sb.Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", numbers[i]);
            return sb.ToString();
        }

        /// <summary>A {x} entry that fits the text, with its slot translated too (and only then).</summary>
        private static bool Template(string key, out string translated)
        {
            foreach (var (re, value) in _templates)
            {
                var m = re.Match(key);
                if (!m.Success) continue;
                var slot = Slot(m.Groups["x"].Value);
                if (slot == null) continue;
                translated = value.Replace("{x}", slot);
                // a name that opens the sentence starts it with a capital
                if (translated.Length > 0 && char.IsLower(translated[0])) translated = char.ToUpperInvariant(translated[0]) + translated.Substring(1);
                return true;
            }
            translated = "";
            return false;
        }

        /// <summary>A slot's translation: as written, or by its capitalised form ("barn" → "Barn" → "grange").</summary>
        private static string? Slot(string s)
        {
            if (s.Length == 0) return null;
            if (_table.TryGetValue(s, out var v)) return v;
            if (!char.IsLower(s[0])) return null;
            if (!_table.TryGetValue(char.ToUpperInvariant(s[0]) + s.Substring(1), out v) || v.Length == 0) return null;
            // a name inside a sentence stays lower-case where the language has case
            return char.IsUpper(v[0]) && v.Length > 1 && !char.IsUpper(v[1]) ? char.ToLowerInvariant(v[0]) + v.Substring(1) : v;
        }

        private static bool HasLetters(string s)
        {
            foreach (var c in s) if (char.IsLetter(c)) return true;
            return false;
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Hook()
        {
            UnityEngine.Application.quitting -= SaveSeen;
            UnityEngine.Application.quitting += SaveSeen;
        }

        /// <summary>Merge the texts seen this Play into the English source table.</summary>
        private static void SaveSeen()
        {
            const string path = "Assets/Farmverse/Content/Resources/Localization/en.json";
            var all = new SortedSet<string>(Seen, StringComparer.Ordinal);
            if (System.IO.File.Exists(path))
            {
                try
                {
                    var old = JsonUtility.FromJson<Table>(System.IO.File.ReadAllText(path));
                    foreach (var e in old?.entries ?? new List<Entry>()) all.Add(e.k);
                }
                catch (Exception) { /* rewritten below */ }
            }
            var table = new Table();
            foreach (var k in all) table.entries.Add(new Entry { k = k, v = k });
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(table, true));
            Debug.Log($"[Loc] {all.Count} texts in {path}");
        }
#endif
    }
}
