#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace Farmverse.Contracts
{
    /// <summary>
    /// Strict conversions between wire values and C# values, used by the generated
    /// <c>ToWire</c> / <c>FromWire</c> methods.
    ///
    /// The Firebase Unity SDKs hand back maps as <c>Dictionary&lt;string, object&gt;</c>
    /// (Firestore) or <c>IDictionary</c> with object keys (Functions), lists as
    /// <c>IList</c>, integers as <c>long</c> and other numbers as <c>double</c>.
    /// Every shape is accepted; every mismatch throws <see cref="WireFormatException"/>
    /// naming the exact path, because a mismatch means the contract is broken.
    /// </summary>
    public static class Wire
    {
        /// <summary>The largest integer a double carries exactly (2^53 - 1).</summary>
        public const long MaxSafeInteger = 9007199254740991L;

        // ------------------------------------------------------------------
        // maps
        // ------------------------------------------------------------------

        public static Dictionary<string, object> AsMap(object? value, string path)
        {
            switch (value)
            {
                case Dictionary<string, object> map:
                    return map;
                case IDictionary<string, object> generic:
                    return new Dictionary<string, object>(generic);
                case IDictionary untyped:
                {
                    var copy = new Dictionary<string, object>(untyped.Count);
                    foreach (DictionaryEntry entry in untyped)
                    {
                        if (!(entry.Key is string key))
                        {
                            throw new WireFormatException(path, "map key is not a string");
                        }
                        copy[key] = entry.Value!;
                    }
                    return copy;
                }
                case null:
                    throw new WireFormatException(path, "expected an object, got null");
                default:
                    throw new WireFormatException(path, "expected an object, got " + value.GetType().Name);
            }
        }

        public static T Required<T>(Dictionary<string, object> map, string key, string path,
                                    Func<object?, string, T> convert)
        {
            if (!map.TryGetValue(key, out var value) || value == null)
            {
                throw new WireFormatException(path, "is required");
            }
            return convert(value, path);
        }

        public static T? OptionalValue<T>(Dictionary<string, object> map, string key, string path,
                                          Func<object?, string, T> convert) where T : struct
        {
            if (!map.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }
            return convert(value, path);
        }

        public static T? OptionalRef<T>(Dictionary<string, object> map, string key, string path,
                                        Func<object?, string, T> convert) where T : class
        {
            if (!map.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }
            return convert(value, path);
        }

        public static void ExpectConst(Dictionary<string, object> map, string key, string expected, string path)
        {
            var actual = Required(map, key, path + "." + key, AsString);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                throw new WireFormatException(path + "." + key, "expected '" + expected + "', got '" + actual + "'");
            }
        }

        // ------------------------------------------------------------------
        // scalars
        // ------------------------------------------------------------------

        public static string AsString(object? value, string path)
        {
            if (value is string text)
            {
                return text;
            }
            throw new WireFormatException(path, "expected a string, got " + Describe(value));
        }

        public static bool AsBool(object? value, string path)
        {
            if (value is bool flag)
            {
                return flag;
            }
            throw new WireFormatException(path, "expected a boolean, got " + Describe(value));
        }

        public static long AsLong(object? value, string path)
        {
            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case short s: return s;
                case byte b: return b;
                case sbyte sb: return sb;
                case ushort us: return us;
                case uint ui: return ui;
                case ulong ul:
                    if (ul > long.MaxValue)
                    {
                        throw new WireFormatException(path, "integer out of range");
                    }
                    return (long)ul;
                case double d: return FromFloating(d, path);
                case float f: return FromFloating(f, path);
                case decimal m:
                    if (m != decimal.Truncate(m) || m > long.MaxValue || m < long.MinValue)
                    {
                        throw new WireFormatException(path, "expected an integer, got " + m);
                    }
                    return (long)m;
                default:
                    throw new WireFormatException(path, "expected an integer, got " + Describe(value));
            }
        }

        public static int AsInt(object? value, string path)
        {
            var l = AsLong(value, path);
            if (l < int.MinValue || l > int.MaxValue)
            {
                throw new WireFormatException(path, "integer " + l + " does not fit in 32 bits");
            }
            return (int)l;
        }

        public static double AsDouble(object? value, string path)
        {
            switch (value)
            {
                case double d: return d;
                case float f: return f;
                case long l: return l;
                case int i: return i;
                case decimal m: return (double)m;
                case short s: return s;
                case byte b: return b;
                case uint ui: return ui;
                case ulong ul: return ul;
                default:
                    throw new WireFormatException(path, "expected a number, got " + Describe(value));
            }
        }

        public static object? AsAny(object? value, string path) => value;

        // ------------------------------------------------------------------
        // collections
        // ------------------------------------------------------------------

        public static List<T> AsList<T>(object? value, string path, Func<object?, string, T> convert)
        {
            if (!(value is IList list) || value is string)
            {
                throw new WireFormatException(path, "expected an array, got " + Describe(value));
            }
            var result = new List<T>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                result.Add(convert(list[i], path + "[" + i + "]"));
            }
            return result;
        }

        public static Dictionary<string, T> AsDictionary<T>(object? value, string path, Func<object?, string, T> convert)
        {
            var map = AsMap(value, path);
            var result = new Dictionary<string, T>(map.Count);
            foreach (var pair in map)
            {
                result[pair.Key] = convert(pair.Value, path + "." + pair.Key);
            }
            return result;
        }

        public static List<object> ListToWire<T>(IReadOnlyList<T> list, Func<T, object> write)
        {
            var result = new List<object>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                result.Add(write(list[i]));
            }
            return result;
        }

        public static Dictionary<string, object> MapToWire<T>(IReadOnlyDictionary<string, T> map, Func<T, object> write)
        {
            var result = new Dictionary<string, object>(map.Count);
            foreach (var pair in map)
            {
                result[pair.Key] = write(pair.Value);
            }
            return result;
        }

        // ------------------------------------------------------------------

        private static long FromFloating(double d, string path)
        {
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d)
            {
                throw new WireFormatException(path, "expected an integer, got " + d);
            }
            if (d > MaxSafeInteger || d < -MaxSafeInteger)
            {
                throw new WireFormatException(path, "integer " + d + " is beyond 2^53 and may have lost precision");
            }
            return (long)d;
        }

        private static string Describe(object? value) => value == null ? "null" : value.GetType().Name;
    }
}
